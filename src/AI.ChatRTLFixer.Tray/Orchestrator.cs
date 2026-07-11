using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Core.Settings;
using AI.ChatRTLFixer.Diagnostics;
using AI.ChatRTLFixer.Fonts;
using AI.ChatRTLFixer.Injectors;
using AI.ChatRTLFixer.Profiles;

namespace AI.ChatRTLFixer.Tray;

/// <summary>Coordinates detection and CDP attachment as a bounded per-process state machine.</summary>
public sealed class Orchestrator : IDisposable
{
    private readonly SafeLogger _logger;
    private readonly ProfileRegistry _profiles;
    private readonly IProcessWatcher _watcher;
    private readonly ISettingsStore _settingsStore;
    private readonly Dictionary<int, RuntimeEntry> _entries = new();
    private readonly object _gate = new();
    private AppSettings _settings;
    private bool _disposed;

    public event EventHandler? StateChanged;
    public IReadOnlyCollection<AppProfile> Profiles => _profiles.All;
    public AppSettings Settings => _settings;
    public IReadOnlyCollection<RuntimeAppStatus> RuntimeStatuses => Entries().Select(x => new RuntimeAppStatus(x.App, x.State, x.Detail, x.CooldownUntilUtc)).ToList();

    public Orchestrator(SafeLogger logger, ProfileRegistry profiles, IProcessWatcher watcher, ISettingsStore settingsStore, AppSettings initialSettings)
    {
        _logger = logger; _profiles = profiles; _watcher = watcher; _settingsStore = settingsStore; _settings = initialSettings;
    }

    public void Start()
    {
        _watcher.AppChanged += OnAppChanged;
        _watcher.AppExited += OnAppExited;
        _watcher.Start();
        _logger.Log(LogLevel.Information, LogCategories.App, "started");
    }

    private async void OnAppChanged(object? sender, DetectedApp app)
    {
        try { await ReconcileAsync(app); }
        catch (Exception ex) { _logger.Log(LogLevel.Error, LogCategories.App, "reconcile-failed", ("app", app.AppId), ("pid", app.ProcessId), ("msg", SafeLogger.Redact(ex.Message))); }
    }

    private async Task ReconcileAsync(DetectedApp app)
    {
        if (!_profiles.TryGet(app.AppId, out var profile)) return;
        var entry = GetOrAdd(app);
        await entry.Gate.WaitAsync();
        try
        {
            entry.App = app;
            if (!_settings.GlobalEnabled || (_settings.Apps.TryGetValue(app.AppId, out var setting) && !setting.Enabled))
            {
                Transition(entry, AppRuntimeState.DisabledByUser, "global-or-app-disabled");
                return;
            }
            if (profile.Status is SupportStatus.Unsupported or SupportStatus.Planned || profile.UiTechnology != UiTechnology.Electron || profile.Cdp is null)
            {
                Transition(entry, AppRuntimeState.Unsupported, "profile-does-not-support-runtime-injection");
                return;
            }
            Transition(entry, AppRuntimeState.Detected, app.MatchReason);
            if (app.DebugPort is int port)
            {
                // An already-running Electron process can publish its endpoint a
                // moment after its command line becomes visible. Retry only to
                // the configured discovery deadline, with backoff.
                await WaitForCdpAndAttachAsync(entry, profile, port);
                return;
            }

            // Never close or restart another application. If its local debug
            // endpoint was enabled by the application/user, the watcher will
            // observe the port and attach on the next event-driven scan.
            Transition(entry, AppRuntimeState.RunningNoDebugPort, "waiting-for-existing-local-endpoint");
        }
        finally { entry.Gate.Release(); StateChanged?.Invoke(this, EventArgs.Empty); }
    }

    private async Task AttachAsync(RuntimeEntry entry, AppProfile profile, int port)
    {
        if (entry.Adapter is null)
        {
            entry.Adapter = new CdpAdapter(_logger);
            entry.Adapter.Detached += (_, _) => OnAdapterDetached(entry);
        }
        var timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.DiscoveryTimeoutSeconds, 2, 60));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(entry.ExitToken);
        timeoutCts.CancelAfter(timeout);
        Transition(entry, AppRuntimeState.CdpDiscovered, $"port={port}");
        AttachResult result;
        try { result = await entry.Adapter.AttachToPortAsync(profile, port, timeoutCts.Token); }
        catch (OperationCanceledException) { result = AttachResult.Failed(AttachFailure.Timeout, "discovery-timeout"); }
        if (!result.Success)
        {
            Transition(entry, result.Failure == AttachFailure.Timeout ? AppRuntimeState.CdpUnsupported : AppRuntimeState.CdpUnsupported, result.Failure?.ToString() ?? "discovery-failed");
            return;
        }
        Transition(entry, AppRuntimeState.Attached, $"port={port}");
        try
        {
            await InjectAsync(entry.Adapter, profile, entry.ExitToken);
            Transition(entry, AppRuntimeState.InjectionSucceeded, "injected");
        }
        catch (Exception ex)
        {
            Transition(entry, AppRuntimeState.InjectionFailed, SafeLogger.Redact(ex.Message));
        }
    }

    private async Task WaitForCdpAndAttachAsync(RuntimeEntry entry, AppProfile profile, int port)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.DiscoveryTimeoutSeconds, 2, 60));
        var delay = 300;
        while (DateTime.UtcNow < deadline && !entry.ExitToken.IsCancellationRequested)
        {
            await AttachAsync(entry, profile, port);
            if (entry.State is AppRuntimeState.InjectionSucceeded or AppRuntimeState.InjectionFailed) return;
            if (entry.State == AppRuntimeState.CdpUnsupported) { await Task.Delay(delay, entry.ExitToken).ContinueWith(_ => { }); delay = Math.Min(delay * 2, 2000); }
        }
        Transition(entry, AppRuntimeState.CdpUnsupported, "cdp-discovery-timeout");
    }

    private async Task InjectAsync(CdpAdapter adapter, AppProfile profile, CancellationToken ct)
    {
        var fontCss = FontPack.BuildFontStyle(FontPack.FontFamilyCss(_settings.SelectedFont, _settings.CustomFontPath), profile.Selectors.FontScope, FontPack.LoadVazirmatnBase64());
        await adapter.InjectAsync(new InjectionPayload { FontCss = fontCss, Css = CssBuilder.Build(profile.Selectors), Script = ScriptBuilder.Build(profile, _settings.CopyMode), CopyMode = _settings.CopyMode }, ct);
        _logger.Log(LogLevel.Information, LogCategories.Injector, "succeeded", ("app", profile.AppId));
    }

    private async void OnAdapterDetached(RuntimeEntry entry)
    {
        if (_disposed || entry.ExitToken.IsCancellationRequested) return;
        Transition(entry, AppRuntimeState.Detected, "local-endpoint-disconnected");
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await Task.Delay(500, entry.ExitToken);
            if (!entry.ExitToken.IsCancellationRequested) await ReconcileAsync(entry.App);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.Cdp, "reattach-failed",
                ("app", entry.App.AppId), ("msg", SafeLogger.Redact(ex.Message)));
        }
    }

    private void OnAppExited(object? sender, DetectedApp app)
    {
        RuntimeEntry? entry;
        lock (_gate) { _entries.TryGetValue(app.ProcessId, out entry); _entries.Remove(app.ProcessId); }
        if (entry is null) return;
        entry.ExitCts.Cancel();
        Transition(entry, AppRuntimeState.Exited, "process-exited");
        _ = entry.Adapter?.DisposeAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisableAllAsync()
    {
        foreach (var entry in Entries())
        {
            await DisableEntryAsync(entry, "disabled-by-user");
        }
        _logger.Log(LogLevel.Information, LogCategories.Restore, "disabled-all");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies the global kill switch immediately to processes already detected.</summary>
    public async Task SetGlobalEnabledAsync(bool enabled)
    {
        _settings.GlobalEnabled = enabled;
        if (!enabled)
        {
            await DisableAllAsync();
            return;
        }

        await ReconcileExistingAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Enables or disables one profile and restores its live page immediately when disabled.</summary>
    public async Task SetAppEnabledAsync(string appId, bool enabled)
    {
        _settings.Apps[appId] = new AppToggleState { Enabled = enabled };
        var entries = Entries().Where(entry => string.Equals(entry.App.AppId, appId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!enabled)
        {
            foreach (var entry in entries) await DisableEntryAsync(entry, "app-disabled-by-user");
        }
        else if (_settings.GlobalEnabled)
        {
            foreach (var entry in entries) await ReconcileAsync(entry.App);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reapplies the current font and copy preferences to already-attached pages.</summary>
    public async Task RefreshAttachedAsync()
    {
        foreach (var entry in Entries())
        {
            await entry.Gate.WaitAsync();
            try
            {
                if (entry.State != AppRuntimeState.InjectionSucceeded || entry.Adapter is null ||
                    !_profiles.TryGet(entry.App.AppId, out var profile)) continue;

                await entry.Adapter.RestoreAsync(entry.ExitToken);
                await InjectAsync(entry.Adapter, profile, entry.ExitToken);
                Transition(entry, AppRuntimeState.InjectionSucceeded, "preferences-updated");
            }
            catch (Exception ex)
            {
                Transition(entry, AppRuntimeState.InjectionFailed, SafeLogger.Redact(ex.Message));
            }
            finally
            {
                entry.Gate.Release();
            }
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task DisableEntryAsync(RuntimeEntry entry, string detail)
    {
        await entry.Gate.WaitAsync();
        try
        {
            Transition(entry, AppRuntimeState.DisabledByUser, detail);
            if (entry.Adapter is not null) await entry.Adapter.RestoreAsync(CancellationToken.None);
        }
        catch
        {
            // Restoring is best effort; closing the target app always returns it to a clean state.
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private async Task ReconcileExistingAsync()
    {
        foreach (var app in _watcher.Snapshot()) await ReconcileAsync(app);
    }

    private RuntimeEntry GetOrAdd(DetectedApp app)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(app.ProcessId, out var entry)) _entries[app.ProcessId] = entry = new RuntimeEntry(app);
            return entry;
        }
    }
    private IReadOnlyList<RuntimeEntry> Entries() { lock (_gate) return _entries.Values.ToList(); }
    private void Transition(RuntimeEntry entry, AppRuntimeState state, string detail)
    {
        if (entry.State == state && entry.Detail == detail) return;
        var previous = entry.State;
        entry.State = state; entry.Detail = detail;
        _logger.Log(LogLevel.Information, LogCategories.Profile, "state-changed", ("app", entry.App.AppId), ("pid", entry.App.ProcessId), ("from", previous.ToString()), ("to", state.ToString()));
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _watcher.Stop();
        foreach (var entry in Entries()) { entry.ExitCts.Cancel(); try { entry.Adapter?.DisposeAsync().AsTask().Wait(); } catch { } entry.Gate.Dispose(); entry.ExitCts.Dispose(); }
        _watcher.Dispose();
    }

    private sealed class RuntimeEntry
    {
        public RuntimeEntry(DetectedApp app) => App = app;
        public DetectedApp App { get; set; }
        public AppRuntimeState State { get; set; } = AppRuntimeState.Unknown;
        public string? Detail { get; set; }
        public DateTime CooldownUntilUtc { get; set; }
        public CdpAdapter? Adapter { get; set; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public CancellationTokenSource ExitCts { get; } = new();
        public CancellationToken ExitToken => ExitCts.Token;
    }
}

public sealed record RuntimeAppStatus(DetectedApp App, AppRuntimeState State, string? Detail, DateTime CooldownUntilUtc);
