using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Core.Settings;
using AI.ChatRTLFixer.Diagnostics;
using AI.ChatRTLFixer.Fonts;
using AI.ChatRTLFixer.Injectors;
using AI.ChatRTLFixer.Profiles;

namespace AI.ChatRTLFixer.Tray;

/// <summary>Coordinates detection, CDP and relaunch as a bounded per-process state machine.</summary>
public sealed class Orchestrator : IDisposable
{
    private readonly SafeLogger _logger;
    private readonly ProfileRegistry _profiles;
    private readonly IProcessWatcher _watcher;
    private readonly IRelaunchService _relaunch;
    private readonly ISettingsStore _settingsStore;
    private readonly Dictionary<int, RuntimeEntry> _entries = new();
    private readonly object _gate = new();
    private AppSettings _settings;
    private bool _disposed;

    public event EventHandler? StateChanged;
    public IReadOnlyCollection<AppProfile> Profiles => _profiles.All;
    public AppSettings Settings => _settings;
    public IReadOnlyCollection<DetectedApp> PendingRelaunch => Entries().Where(x => x.State is AppRuntimeState.RelaunchRequired or AppRuntimeState.RelaunchPromptShown).Select(x => x.App).ToList();
    public IReadOnlyCollection<RuntimeAppStatus> RuntimeStatuses => Entries().Select(x => new RuntimeAppStatus(x.App, x.State, x.Detail, x.CooldownUntilUtc)).ToList();

    public Orchestrator(SafeLogger logger, ProfileRegistry profiles, IProcessWatcher watcher, IRelaunchService relaunch, ISettingsStore settingsStore, AppSettings initialSettings)
    {
        _logger = logger; _profiles = profiles; _watcher = watcher; _relaunch = relaunch; _settingsStore = settingsStore; _settings = initialSettings;
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
                await WaitForCdpAndAttachAsync(entry, profile, port, afterRelaunch: false);
                return;
            }

            Transition(entry, AppRuntimeState.RunningNoDebugPort, "no-remote-debugging-port");
            if (DateTime.UtcNow < entry.CooldownUntilUtc)
            {
                _logger.Log(LogLevel.Information, LogCategories.Relaunch, "cooldown-active", ("app", app.AppId), ("pid", app.ProcessId));
                return;
            }
            Transition(entry, AppRuntimeState.RelaunchRequired, "requires-relaunch");
            if (_settings.AutoRelaunchAfterConsent)
                await RelaunchEntryAsync(entry, profile, _ => Task.FromResult(true));
            else
                Transition(entry, AppRuntimeState.RelaunchPromptShown, "awaiting-user-consent");
        }
        finally { entry.Gate.Release(); StateChanged?.Invoke(this, EventArgs.Empty); }
    }

    private async Task AttachAsync(RuntimeEntry entry, AppProfile profile, int port, bool afterRelaunch)
    {
        entry.Adapter ??= new CdpAdapter(_logger);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.DiscoveryTimeoutSeconds, 2, 60));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(entry.ExitToken);
        timeoutCts.CancelAfter(timeout);
        Transition(entry, afterRelaunch ? AppRuntimeState.WaitingForCdp : AppRuntimeState.CdpDiscovered, $"port={port}");
        AttachResult result;
        try { result = await entry.Adapter.AttachToPortAsync(profile, port, timeoutCts.Token); }
        catch (OperationCanceledException) { result = AttachResult.Failed(AttachFailure.Timeout, "discovery-timeout"); }
        if (!result.Success)
        {
            Transition(entry, result.Failure == AttachFailure.Timeout ? AppRuntimeState.CdpUnsupported : AppRuntimeState.CdpUnsupported, result.Failure?.ToString() ?? "discovery-failed");
            if (afterRelaunch) entry.CooldownUntilUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.RelaunchCooldownSeconds, 10, 3600));
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

    public async Task<RelaunchResult> RelaunchAsync(DetectedApp app, Func<RelaunchWarning, Task<bool>>? consentCallback)
    {
        if (!_profiles.TryGet(app.AppId, out var profile)) return new RelaunchResult { Success = false, UserConsented = false, Detail = "unknown-profile" };
        var entry = GetOrAdd(app);
        await entry.Gate.WaitAsync();
        try
        {
            // A null callback is deliberately a rejection: no implicit destructive action.
            if (consentCallback is null) return new RelaunchResult { Success = false, UserConsented = false, Detail = "consent-required" };
            return await RelaunchEntryAsync(entry, profile, consentCallback);
        }
        finally { entry.Gate.Release(); StateChanged?.Invoke(this, EventArgs.Empty); }
    }

    private async Task<RelaunchResult> RelaunchEntryAsync(RuntimeEntry entry, AppProfile profile, Func<RelaunchWarning, Task<bool>> consent)
    {
        if (profile.Status is SupportStatus.Unsupported or SupportStatus.Planned || profile.Cdp is null)
        {
            Transition(entry, AppRuntimeState.Unsupported, "relaunch-not-permitted-for-profile");
            return new RelaunchResult { Success = false, UserConsented = false, Detail = "unsupported-profile" };
        }
        if (DateTime.UtcNow < entry.CooldownUntilUtc)
            return new RelaunchResult { Success = false, UserConsented = true, Detail = "cooldown-active" };
        Transition(entry, AppRuntimeState.Relaunching, "user-consented");
        var result = await _relaunch.RelaunchWithRtlFixAsync(entry.App, profile, consent, entry.ExitToken);
        if (!result.Success)
        {
            Transition(entry, AppRuntimeState.RelaunchRequired, result.Detail ?? "relaunch-failed");
            entry.CooldownUntilUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.RelaunchCooldownSeconds, 10, 3600));
            return result;
        }
        if (!result.DebugArgsVerified)
        {
            Transition(entry, AppRuntimeState.DebugArgsIgnored, "relaunch-args-not-observed");
            entry.CooldownUntilUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.RelaunchCooldownSeconds, 10, 3600));
            return result;
        }
        if (result.DebugPort is not int port) return result;
        Transition(entry, AppRuntimeState.WaitingForCdp, $"port={port}");
        await WaitForCdpAndAttachAsync(entry, profile, port, afterRelaunch: true);
        return result;
    }

    private async Task WaitForCdpAndAttachAsync(RuntimeEntry entry, AppProfile profile, int port, bool afterRelaunch)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.DiscoveryTimeoutSeconds, 2, 60));
        var delay = 300;
        while (DateTime.UtcNow < deadline && !entry.ExitToken.IsCancellationRequested)
        {
            await AttachAsync(entry, profile, port, afterRelaunch);
            if (entry.State is AppRuntimeState.InjectionSucceeded or AppRuntimeState.InjectionFailed) return;
            if (entry.State == AppRuntimeState.CdpUnsupported) { await Task.Delay(delay, entry.ExitToken).ContinueWith(_ => { }); delay = Math.Min(delay * 2, 2000); }
        }
        // The newly-created process must expose the args. The watcher will update it if it appears.
        var current = _watcher.Snapshot().FirstOrDefault(x => x.AppId == entry.App.AppId && x.ProcessId != entry.App.ProcessId);
        var argsIgnored = afterRelaunch && current?.HasDebugPort == false;
        Transition(entry, argsIgnored ? AppRuntimeState.DebugArgsIgnored : AppRuntimeState.CdpUnsupported,
            argsIgnored ? "debug-args-ignored" : "cdp-discovery-timeout");
        if (afterRelaunch) entry.CooldownUntilUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.RelaunchCooldownSeconds, 10, 3600));
    }

    private async Task InjectAsync(CdpAdapter adapter, AppProfile profile, CancellationToken ct)
    {
        var fontCss = FontPack.BuildFontStyle(FontPack.FontFamilyCss(_settings.SelectedFont, _settings.CustomFontPath), profile.Selectors.FontScope, FontPack.LoadVazirmatnBase64());
        await adapter.InjectAsync(new InjectionPayload { FontCss = fontCss, Css = CssBuilder.Build(profile.Selectors), Script = ScriptBuilder.Build(profile, _settings.CopyMode), CopyMode = _settings.CopyMode }, ct);
        _logger.Log(LogLevel.Information, LogCategories.Injector, "succeeded", ("app", profile.AppId));
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
            Transition(entry, AppRuntimeState.DisabledByUser, "disabled-by-user");
            if (entry.Adapter is not null) { try { await entry.Adapter.RestoreAsync(CancellationToken.None); } catch { } }
        }
        _logger.Log(LogLevel.Information, LogCategories.Restore, "disabled-all");
        StateChanged?.Invoke(this, EventArgs.Empty);
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
