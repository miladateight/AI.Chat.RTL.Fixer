using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Core.Settings;
using AI.ChatRTLFixer.Diagnostics;
using AI.ChatRTLFixer.Fonts;
using AI.ChatRTLFixer.Injectors;
using AI.ChatRTLFixer.Profiles;
using AI.ChatRTLFixer.Win32;

namespace AI.ChatRTLFixer.Tray;

/// <summary>Coordinates detection, CDP attachment and relaunch as a bounded per-process state machine.</summary>
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
    public IReadOnlyCollection<DetectedApp> PendingRelaunch =>
        Entries().Where(x => x.State is AppRuntimeState.RelaunchRequired or AppRuntimeState.RelaunchPromptShown).Select(x => x.App).ToList();
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
            // Opt-in by design: a profile the user has never explicitly ticked in
            // Settings (or consented to relaunch) has no entry here and must stay
            // untouched, even if a process happens to match its detection rules.
            if (!_settings.GlobalEnabled || !(_settings.Apps.TryGetValue(app.AppId, out var setting) && setting.Enabled))
            {
                Transition(entry, AppRuntimeState.DisabledByUser, "global-or-app-disabled");
                return;
            }
            if (!profile.SupportsRuntimeInjection)
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

            // No debug port. The fixer never enables one without explicit consent:
            // either the app already had one relaunched-and-remembered, or the
            // user must click through the tray's relaunch action.
            Transition(entry, AppRuntimeState.RunningNoDebugPort, "no-remote-debugging-port");
            if (DateTime.UtcNow < entry.CooldownUntilUtc)
            {
                _logger.Log(LogLevel.Information, LogCategories.Relaunch, "cooldown-active", ("app", app.AppId), ("pid", app.ProcessId));
                return;
            }
            Transition(entry, AppRuntimeState.RelaunchRequired, "requires-relaunch");
            var alreadyConsented = _settings.Apps.TryGetValue(app.AppId, out var toggle) && toggle.RelaunchConsentGranted;
            if (_settings.AutoRelaunchAfterConsent && alreadyConsented)
                await RelaunchEntryAsync(entry, profile, _ => Task.FromResult(true));
            else
                Transition(entry, AppRuntimeState.RelaunchPromptShown, "awaiting-user-consent");
        }
        finally { entry.Gate.Release(); StateChanged?.Invoke(this, EventArgs.Empty); }
    }

    private async Task AttachAsync(RuntimeEntry entry, AppProfile profile, int port, bool afterRelaunch)
    {
        if (entry.Adapter is null)
        {
            entry.Adapter = new CdpAdapter(_logger);
            entry.Adapter.Detached += (_, _) => OnAdapterDetached(entry);
        }
        var timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.DiscoveryTimeoutSeconds, 2, 60));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(entry.ExitToken);
        timeoutCts.CancelAfter(timeout);
        Transition(entry, afterRelaunch ? AppRuntimeState.WaitingForCdp : AppRuntimeState.CdpDiscovered, $"port={port}");
        AttachResult result;
        try { result = await entry.Adapter.AttachToPortAsync(profile, port, timeoutCts.Token); }
        catch (OperationCanceledException) { result = AttachResult.Failed(AttachFailure.Timeout, "discovery-timeout"); }
        if (!result.Success)
        {
            Transition(entry, AppRuntimeState.CdpUnsupported, result.Failure?.ToString() ?? "discovery-failed");
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

    /// <summary>
    /// Explicit, user-initiated relaunch (tray "Relaunch with RTL Fix…" click).
    /// A null consent callback is deliberately treated as a rejection: no
    /// implicit destructive action is ever taken. When the callback returns
    /// true, that consent is remembered for this app so future detections
    /// (e.g. the app closed and reopened later) can relaunch automatically
    /// without asking again, subject to <see cref="AppSettings.AutoRelaunchAfterConsent"/>.
    /// </summary>
    public async Task<RelaunchResult> RelaunchAsync(DetectedApp app, Func<RelaunchWarning, Task<bool>>? consentCallback)
    {
        if (!_profiles.TryGet(app.AppId, out var profile)) return new RelaunchResult { Success = false, UserConsented = false, Detail = "unknown-profile" };
        var entry = GetOrAdd(app);
        await entry.Gate.WaitAsync();
        try
        {
            if (consentCallback is null) return new RelaunchResult { Success = false, UserConsented = false, Detail = "consent-required" };
            Func<RelaunchWarning, Task<bool>> wrapped = async warning =>
            {
                var consented = await consentCallback(warning);
                if (consented) await PersistRelaunchConsentAsync(app.AppId);
                return consented;
            };
            return await RelaunchEntryAsync(entry, profile, wrapped);
        }
        finally { entry.Gate.Release(); StateChanged?.Invoke(this, EventArgs.Empty); }
    }

    private async Task PersistRelaunchConsentAsync(string appId)
    {
        if (_settings.Apps.TryGetValue(appId, out var existing) && existing.RelaunchConsentGranted) return;
        var enabled = _settings.Apps.TryGetValue(appId, out var current) ? current.Enabled : true;
        _settings.Apps[appId] = new AppToggleState { Enabled = enabled, RelaunchConsentGranted = true };
        await _settingsStore.SaveAsync(_settings, CancellationToken.None);
    }

    private async Task<RelaunchResult> RelaunchEntryAsync(RuntimeEntry entry, AppProfile profile, Func<RelaunchWarning, Task<bool>> consent)
    {
        if (!profile.SupportsRuntimeInjection)
        {
            Transition(entry, AppRuntimeState.Unsupported, "relaunch-not-permitted-for-profile");
            return new RelaunchResult { Success = false, UserConsented = false, Detail = "unsupported-profile" };
        }
        if (DateTime.UtcNow < entry.CooldownUntilUtc)
            return new RelaunchResult { Success = false, UserConsented = true, Detail = "cooldown-active" };
        Transition(entry, AppRuntimeState.Relaunching, "user-consented");
        var result = await _relaunch.RelaunchWithRtlFixAsync(entry.App, profile, _settings.EnableBrowserTargets, consent, entry.ExitToken);
        if (!result.Success)
        {
            Transition(entry, AppRuntimeState.RelaunchRequired, result.Detail ?? "relaunch-failed");
            entry.CooldownUntilUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.RelaunchCooldownSeconds, 10, 3600));
            return result;
        }
        if (result.DebugPort is not int port) return result;
        // DebugArgsVerified only means we could confirm the flag on the SPAWNED
        // pid within a short window. MSIX/packaged Electron apps (Claude, ChatGPT,
        // Codex, …) frequently re-exec or hand the window off to a different pid,
        // so that check often reports false even though the relaunched instance
        // DID bind the debug port. The authoritative success signal is whether CDP
        // actually comes up on 127.0.0.1:port — so we always proceed to attach and
        // only fall back to DebugArgsIgnored inside WaitForCdpAndAttachAsync if the
        // endpoint never appears.
        if (!result.DebugArgsVerified)
            _logger.Log(LogLevel.Information, LogCategories.Relaunch, "args-unverified-attaching-anyway", ("app", profile.AppId), ("port", port));
        Transition(entry, AppRuntimeState.WaitingForCdp, $"port={port}");
        await WaitForCdpAndAttachAsync(entry, profile, port, afterRelaunch: true);
        return result;
    }

    private async Task WaitForCdpAndAttachAsync(RuntimeEntry entry, AppProfile profile, int port, bool afterRelaunch)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.DiscoveryTimeoutSeconds, 2, 60));
        // Right after a relaunch, the target's debug port typically comes up
        // within ~1-3s of Electron/Chromium startup. Poll tightly at first so we
        // attach and inject the instant it appears instead of leaving the app
        // visibly un-fixed for an extra second or two of unnecessary backoff.
        var delay = afterRelaunch ? 120 : 300;
        var maxDelay = afterRelaunch ? 1000 : 2000;
        while (DateTime.UtcNow < deadline && !entry.ExitToken.IsCancellationRequested)
        {
            await AttachAsync(entry, profile, port, afterRelaunch);
            if (entry.State is AppRuntimeState.InjectionSucceeded or AppRuntimeState.InjectionFailed) return;
            if (entry.State == AppRuntimeState.CdpUnsupported)
            {
                try { await Task.Delay(delay, entry.ExitToken); }
                catch (OperationCanceledException) { return; }
                delay = Math.Min(delay * 2, maxDelay);
            }
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
        _ = DisposeEntryAdapterAsync(entry);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task DisposeEntryAdapterAsync(RuntimeEntry entry)
    {
        try
        {
            await entry.Gate.WaitAsync();
            try
            {
                if (entry.Adapter is not null) await entry.Adapter.DisposeAsync();
            }
            finally
            {
                entry.Gate.Release();
            }
        }
        catch (ObjectDisposedException) { }
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
    /// <summary>
    /// Records that an app's shortcuts now carry the loopback debugging flags
    /// (or no longer do). Only the state is stored here — the shortcuts
    /// themselves are rewritten by the platform's
    /// <see cref="IPersistentLaunchService"/> before this is called, and only
    /// after the user confirmed it.
    /// </summary>
    public async Task SetPersistentLaunchAsync(string appId, bool configured, int? port)
    {
        if (!_settings.Apps.TryGetValue(appId, out var toggle))
        {
            toggle = new AppToggleState();
            _settings.Apps[appId] = toggle;
        }
        toggle.PersistentLaunchConfigured = configured;
        toggle.PersistentLaunchPort = configured ? port : null;
        _logger.Log(LogLevel.Information, LogCategories.Relaunch, "persistent-launch-set",
            ("app", appId), ("configured", configured), ("port", port ?? 0));
        await ReconcileExistingAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

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

    /// <summary>
    /// Makes browser targeting an explicit runtime opt-in. Disabling it restores
    /// any live browser injection before the watcher stops tracking browsers.
    /// </summary>
    public async Task SetBrowserTargetsEnabledAsync(bool enabled)
    {
        if (_settings.EnableBrowserTargets == enabled) return;

        if (!enabled)
        {
            var browserEntries = Entries()
                .Where(entry => BrowserGuard.IsBrowser(entry.App.ProcessName, entry.App.ExecutablePath))
                .ToList();
            foreach (var entry in browserEntries)
                await DisableEntryAsync(entry, "browser-targeting-disabled");
        }

        _settings.EnableBrowserTargets = enabled;
        _watcher.SetBrowserTargetsEnabled(enabled);
        if (enabled && _settings.GlobalEnabled) await ReconcileExistingAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Enables or disables one profile and restores its live page immediately when disabled.</summary>
    public async Task SetAppEnabledAsync(string appId, bool enabled)
    {
        var consentGranted = _settings.Apps.TryGetValue(appId, out var existing) && existing.RelaunchConsentGranted;
        _settings.Apps[appId] = new AppToggleState { Enabled = enabled, RelaunchConsentGranted = consentGranted };
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
        _watcher.AppChanged -= OnAppChanged;
        _watcher.AppExited -= OnAppExited;
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
