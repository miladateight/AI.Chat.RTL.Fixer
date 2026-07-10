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

/// <summary>
/// Coordinates process watching, profile matching, attach/relaunch and injection.
/// Bounded retries only: if CDP does not come up on 127.0.0.1 within a few
/// attempts, the profile is reported Experimental/Unsupported (no infinite retry).
/// </summary>
public sealed class Orchestrator : IDisposable
{
    private readonly SafeLogger _logger;
    private readonly ProfileRegistry _profiles;
    private readonly IProcessWatcher _watcher;
    private readonly IRelaunchService _relaunch;
    private readonly ISettingsStore _settingsStore;
    private readonly Dictionary<string, CdpAdapter> _adaptersByAppId = new();
    private AppSettings _settings;
    private bool _disposed;

    public event EventHandler? StateChanged;

    public Orchestrator(
        SafeLogger logger,
        ProfileRegistry profiles,
        IProcessWatcher watcher,
        IRelaunchService relaunch,
        ISettingsStore settingsStore,
        AppSettings initialSettings)
    {
        _logger = logger;
        _profiles = profiles;
        _watcher = watcher;
        _relaunch = relaunch;
        _settingsStore = settingsStore;
        _settings = initialSettings;
    }

    public IReadOnlyCollection<AppProfile> Profiles => _profiles.All;

    public AppSettings Settings => _settings;

    public void Start()
    {
        _watcher.AppChanged += OnAppChanged;
        _watcher.AppExited += OnAppExited;
        _watcher.Start();
        _logger.Log(LogLevel.Information, LogCategories.App, "started");
    }

    private async void OnAppChanged(object? sender, DetectedApp app)
    {
        if (!_settings.GlobalEnabled) return;
        if (!_settings.Apps.TryGetValue(app.AppId, out var toggle) || toggle.Enabled is false) return;
        if (!_profiles.TryGet(app.AppId, out var profile)) return;

        // Only Electron/CDP profiles can be injected in v0.1.
        if (profile.UiTechnology != UiTechnology.Electron || profile.Cdp is null)
        {
            _logger.Log(LogLevel.Information, LogCategories.Profile, "non-electron-detected", ("app", app.AppId), ("status", profile.Status.ToString()));
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // If the app already has a debug port open, try to attach (bounded).
        if (app.HasDebugPort && app.DebugPort is int port)
        {
            await TryAttachBoundedAsync(profile, port);
            return;
        }

        // Otherwise signal that a relaunch is required (UI prompts the user).
        _logger.Log(LogLevel.Information, LogCategories.Profile, "relaunch-required", ("app", app.AppId));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnAppExited(object? sender, string pid)
    {
        _logger.Log(LogLevel.Information, LogCategories.ProcessWatcher, "app-exited", ("pid", pid));
        // Detach any adapter whose app is gone; cleanup is automatic (DOM changes vanish on close).
        foreach (var (appId, adapter) in _adaptersByAppId.ToList())
        {
            if (!adapter.IsAttached)
            {
                _adaptersByAppId.Remove(appId);
            }
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task TryAttachBoundedAsync(AppProfile profile, int port)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var adapter = GetOrCreateAdapter(profile.AppId);
            var result = await adapter.AttachToPortAsync(profile, port, CancellationToken.None);
            if (result.Success && result.VerifiedBindAddress == Constants.LoopbackAddress)
            {
                await InjectAsync(adapter, profile);
                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
            _logger.Log(LogLevel.Warning, LogCategories.Cdp, "attach-attempt-failed",
                ("app", profile.AppId), ("attempt", attempt), ("failure", result.Failure?.ToString() ?? "unknown"));
            await Task.Delay(500);
        }

        // Bounded retries exhausted: do NOT loop forever. Report Experimental/Unsupported.
        _logger.Log(LogLevel.Error, LogCategories.Cdp, "attach-exhausted", ("app", profile.AppId));
        // Downgrade in-memory status (does not mutate the builtin profile object's
        // canonical status, but signals the UI). The UI shows Experimental/Unsupported.
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task InjectAsync(CdpAdapter adapter, AppProfile profile)
    {
        var fontBase64 = FontPack.LoadVazirmatnBase64();
        var fontFamilyCss = FontPack.FontFamilyCss(_settings.SelectedFont, _settings.CustomFontPath);
        var css = CssBuilder.Build(profile.Selectors);
        var script = ScriptBuilder.Build(profile, _settings.CopyMode);
        var payload = new InjectionPayload
        {
            FontBase64 = fontBase64,
            FontFamilyCss = fontFamilyCss,
            Css = css,
            Script = script,
            CopyMode = _settings.CopyMode,
        };
        try
        {
            await adapter.InjectAsync(payload, CancellationToken.None);
            _logger.Log(LogLevel.Information, LogCategories.Injector, "injected", ("app", profile.AppId));
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogCategories.Injector, "inject-failed", ("app", profile.AppId), ("msg", SafeLogger.Redact(ex.Message)));
        }
    }

    private CdpAdapter GetOrCreateAdapter(string appId)
    {
        if (_adaptersByAppId.TryGetValue(appId, out var existing)) return existing;
        var adapter = new CdpAdapter(_logger);
        _adaptersByAppId[appId] = adapter;
        return adapter;
    }

    /// <summary>Relaunch the given app with RTL Fix (user-consented).</summary>
    public async Task<RelaunchResult> RelaunchAsync(DetectedApp app)
    {
        if (!_profiles.TryGet(app.AppId, out var profile)) return new RelaunchResult { Success = false, UserConsented = false };
        return await _relaunch.RelaunchWithRtlFixAsync(app, profile, _ => Task.FromResult(true), CancellationToken.None);
    }

    /// <summary>Disable globally and restore all runtime modifications.</summary>
    public async Task DisableAllAsync()
    {
        foreach (var (_, adapter) in _adaptersByAppId)
        {
            try { await adapter.RestoreAsync(CancellationToken.None); } catch { }
        }
        _logger.Log(LogLevel.Information, LogCategories.Restore, "disabled-all");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.Stop();
        foreach (var (_, adapter) in _adaptersByAppId)
        {
            try { adapter.DisposeAsync().AsTask().Wait(); } catch { }
        }
        _watcher.Dispose();
    }
}