using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Localization;
using AI.ChatRTLFixer.Core.Settings;
using AI.ChatRTLFixer.Diagnostics;
using AI.ChatRTLFixer.Win32;

namespace AI.ChatRTLFixer.Tray;

/// <summary>
/// Hosts the NotifyIcon tray menu and wires UI actions to the orchestrator.
/// No telemetry; all communication is local loopback (127.0.0.1) only.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Orchestrator _orchestrator;
    private readonly SafeLogger _logger;
    private readonly ISettingsStore _settingsStore;
    private readonly UpdateChecker _updateChecker;
    private readonly NotifyIcon _notify;
    private readonly ToolStripMenuItem _globalToggleItem;
    private readonly SynchronizationContext _uiContext;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext(Orchestrator orchestrator, SafeLogger logger, ISettingsStore settingsStore, UpdateChecker updateChecker)
    {
        _orchestrator = orchestrator;
        _logger = logger;
        _settingsStore = settingsStore;
        _updateChecker = updateChecker;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _orchestrator.StateChanged += (_, _) =>
        {
            _uiContext.Post(_ => { RebuildMenu(); NotifyIfRelaunchNeeded(); }, null);
        };

        _globalToggleItem = new ToolStripMenuItem(Loc.T("menu.on"), null, (_, _) => ToggleGlobal());

        _notify = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = Constants.ProductName,
            Visible = true,
        };
        _notify.DoubleClick += (_, _) => OpenSettings();
        _notify.ContextMenuStrip = new ContextMenuStrip();

        _orchestrator.Start();
        RebuildMenu();
        if (_orchestrator.Settings.CheckForUpdatesOnStartup)
            _ = CheckForUpdatesAsync(interactive: false);
    }

    /// <summary>
    /// Loads the tray icon from the application executable's own icon
    /// (the embedded ApplicationIcon). Falls back to the generic system
    /// icon if extraction fails for any reason.
    /// </summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null) return icon;
        }
        catch
        {
            // ignore and fall back
        }
        return SystemIcons.Application;
    }

    private void RebuildMenu()
    {
        var menu = _notify.ContextMenuStrip!;
        menu.Items.Clear();
        menu.Items.Add(_globalToggleItem);
        _globalToggleItem.Checked = _orchestrator.Settings.GlobalEnabled;

        menu.Items.Add(new ToolStripSeparator());
        var detected = new ToolStripMenuItem(Loc.T("detected.title"));
        foreach (var status in _orchestrator.RuntimeStatuses)
        {
            var profile = _orchestrator.Profiles.FirstOrDefault(p => p.AppId == status.App.AppId);
            var display = profile?.DisplayName ?? status.App.AppId;
            var item = new ToolStripMenuItem($"{display}: {Readable(status.State)}") { Tag = status.App.AppId };
            detected.DropDownItems.Add(item);
        }
        if (detected.DropDownItems.Count == 0)
            detected.DropDownItems.Add("(none)").Enabled = false;
        menu.Items.Add(detected);

        // Apps that need a relaunch with RTL Fix (detected without a debug port).
        // Never automatic: every entry here requires the user to click it, then
        // confirm the warning dialog, before anything is closed or restarted.
        var pending = _orchestrator.PendingRelaunch;
        if (pending.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
            var relaunchMenu = new ToolStripMenuItem(Loc.T("relaunch.menu"));
            foreach (var app in pending)
            {
                var displayName = app.AppId;
                if (_orchestrator.Profiles.FirstOrDefault(p => p.AppId == displayName) is { } prof)
                    displayName = prof.DisplayName;
                relaunchMenu.DropDownItems.Add(Mi(displayName, () => RelaunchAppAsync(app)));
            }
            menu.Items.Add(relaunchMenu);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Mi(Loc.T("button.settings"), OpenSettings));
        var advanced = new ToolStripMenuItem(Loc.T("menu.advanced"));
        advanced.DropDownItems.Add(Mi(Loc.T("button.checkUpdates"), () => CheckForUpdatesAsync(interactive: true)));
        advanced.DropDownItems.Add(Mi(Loc.T("menu.openLogs"), OpenLogs));
        advanced.DropDownItems.Add(Mi(Loc.T("menu.exportReport"), ExportDetectionReportAsync));
        advanced.DropDownItems.Add(Mi(Loc.T("menu.resetRuntime"), async () => await _orchestrator.DisableAllAsync()));

        // One-time setup that ends the close-and-reopen cycle: put the loopback
        // debugging flags on the app's own shortcuts so every future start
        // already exposes the endpoint and the fixer just attaches.
        var persistent = new ToolStripMenuItem(Loc.T("persistent.menu"));
        foreach (var status in _orchestrator.RuntimeStatuses)
        {
            var app = status.App;
            if (string.IsNullOrEmpty(app.ExecutablePath)) continue;
            var profile = _orchestrator.Profiles.FirstOrDefault(p => p.AppId == app.AppId);
            var display = profile?.DisplayName ?? app.AppId;
            var configured = _orchestrator.Settings.Apps.TryGetValue(app.AppId, out var toggle) && toggle.PersistentLaunchConfigured;
            persistent.DropDownItems.Add(configured
                ? Mi(Loc.T("persistent.turnOff", display), () => SetPersistentLaunchAsync(app, display, enable: false))
                : Mi(Loc.T("persistent.setUp", display), () => SetPersistentLaunchAsync(app, display, enable: true)));
        }
        if (persistent.DropDownItems.Count > 0) advanced.DropDownItems.Add(persistent);

        menu.Items.Add(advanced);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Mi(Loc.T("button.about"), ShowAbout));
        menu.Items.Add(Mi(Loc.T("button.exit"), ExitApp));
    }

    /// <summary>
    /// Tells the user, once per app, that something they just opened cannot be
    /// fixed until it is relaunched — and opens Settings, where the explanation
    /// and the button live, if they click the notification.
    ///
    /// <para>
    /// Without this the app simply appeared not to work: nothing was wrong on
    /// screen, and the only way to find out was to open the tray menu. Tracking
    /// which apps have already been announced keeps the reconciliation loop from
    /// popping the same balloon every few seconds.
    /// </para>
    /// </summary>
    private void NotifyIfRelaunchNeeded()
    {
        if (!_orchestrator.Settings.GlobalEnabled) return;

        var pending = _orchestrator.PendingRelaunch
            .Select(app => app.AppId)
            .Distinct()
            .ToList();

        // Forget apps that no longer need anything, so closing and reopening an
        // app legitimately announces it again.
        _announcedRelaunch.IntersectWith(pending);

        var fresh = pending.Where(id => !_announcedRelaunch.Contains(id)).ToList();
        if (fresh.Count == 0) return;
        foreach (var id in fresh) _announcedRelaunch.Add(id);

        var names = fresh
            .Select(id => _orchestrator.Profiles.FirstOrDefault(p => p.AppId == id)?.DisplayName ?? id)
            .ToList();

        var text = names.Count == 1
            ? Loc.T("relaunch.needed.one", names[0])
            : Loc.T("relaunch.needed.many", names.Count);

        _notify.BalloonTipClicked -= OnRelaunchBalloonClicked;
        _notify.BalloonTipClicked += OnRelaunchBalloonClicked;
        _notify.ShowBalloonTip(5000, Constants.ProductName, text, ToolTipIcon.Warning);
    }

    private void OnRelaunchBalloonClicked(object? sender, EventArgs e) => OpenSettings();

    private readonly HashSet<string> _announcedRelaunch = new(StringComparer.OrdinalIgnoreCase);


    /// <summary>
    /// Turns a failed relaunch into a sentence that says what happened and what
    /// to do. A bare error code ("did-not-stay-open") tells the user nothing;
    /// these are the cases the safety checks now stop BEFORE anything is closed.
    /// </summary>
    internal static string ExplainRelaunchFailure(string display, string? detail)
    {
        if (detail is null) return Loc.T("relaunch.failed", display, "unknown");
        if (detail.StartsWith("other-windows-open", StringComparison.Ordinal))
            return Loc.T("relaunch.blocked.otherWindows", display);
        if (detail == "executable-not-found")
            return Loc.T("relaunch.blocked.notFound", display);
        if (detail == "did-not-stay-open")
            return Loc.T("relaunch.blocked.didNotStayOpen", display);
        if (detail == "reopened-without-fix")
            return Loc.T("relaunch.blocked.reopenedWithoutFix", display);
        return Loc.T("relaunch.failed", display, detail);
    }

    private static ToolStripMenuItem Mi(string text, Action handler)
        => new(text, null, (_, _) => handler());

    private static ToolStripMenuItem Mi(string text, Func<Task> handlerAsync)
        => new(text, null, async (_, _) => await handlerAsync());

    private async void ToggleGlobal()
    {
        await _orchestrator.SetGlobalEnabledAsync(!_orchestrator.Settings.GlobalEnabled);
        await _settingsStore.SaveAsync(_orchestrator.Settings, CancellationToken.None);
        RebuildMenu();
    }

    private void OpenSettings()
    {
        if (_settingsForm is null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_orchestrator, _settingsStore, _logger, _updateChecker);
            _settingsForm.FormClosed += (_, _) => _settingsForm = null;
            _settingsForm.Show();
        }

        // A form opened from a NotifyIcon menu click or double-click is visible
        // but does not receive real input focus by default — Windows' foreground
        // lock leaves it inert to clicks until the user manually clicks its
        // title bar first. Force activation so every control responds right away.
        if (_settingsForm.WindowState == FormWindowState.Minimized)
            _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.TopMost = true;
        _settingsForm.TopMost = false;
        _settingsForm.Activate();
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        var result = await _updateChecker.CheckAsync(CancellationToken.None);
        _uiContext.Post(_ => PresentUpdateResult(result, interactive), null);
    }

    private void PresentUpdateResult(UpdateCheckResult result, bool interactive)
    {
        if (result.IsUpdateAvailable)
        {
            if (interactive)
            {
                var open = MessageBox.Show(
                    $"Version {result.LatestVersion} is available. Open the GitHub release page?",
                    Constants.ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (open == DialogResult.Yes && result.ReleasePage is not null)
                    UpdateChecker.OpenReleasePage(result.ReleasePage);
            }
            else
            {
                _notify.ShowBalloonTip(5000, Constants.ProductName,
                    $"Version {result.LatestVersion} is available. Use 'Check for updates' in the tray menu to open it.",
                    ToolTipIcon.Info);
            }
            return;
        }

        if (interactive)
            MessageBox.Show(result.Message, Constants.ProductName, MessageBoxButtons.OK,
                result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    /// <summary>
    /// Adds or removes the loopback debugging flags on an app's own Windows
    /// shortcuts. This is the setting that ends the close-and-reopen cycle: an
    /// Electron app binds its debugging endpoint once at startup and nothing can
    /// enable it on a process that is already running, so the only way to attach
    /// without restarting anything is for the app to have been started with the
    /// flags in the first place.
    /// </summary>
    private async Task SetPersistentLaunchAsync(DetectedApp app, string display, bool enable)
    {
        var exe = app.ExecutablePath;
        if (string.IsNullOrEmpty(exe)) return;

        var service = new ShortcutLaunchService(_logger);
        var shortcuts = service.FindShortcuts(exe);
        if (shortcuts.Count == 0)
        {
            // A packaged app has no shortcut to edit and never will, so telling
            // the user to go and pin one would send them after something that
            // cannot work.
            var message = PersistentLaunchFlags.IsWindowsPackagedApp(exe)
                ? Loc.T("persistent.packaged", display)
                : Loc.T("persistent.noShortcut", display);

            MessageBox.Show(message, Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var writable = shortcuts.Count(s => !s.Location.EndsWith("(system-wide)", StringComparison.Ordinal));
        if (enable)
        {
            var confirm = MessageBox.Show(
                Loc.T("persistent.confirmBodyWindows", display, writable),
                Loc.T("persistent.confirmTitle", display), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;
        }

        var port = PersistentLaunchFlags.DeriveStablePort(
            app.AppId, _orchestrator.Settings.PortRange.Min, _orchestrator.Settings.PortRange.Max);
        var result = enable ? service.Install(exe, port) : service.Remove(exe);

        if (result.Success)
        {
            await _orchestrator.SetPersistentLaunchAsync(app.AppId, enable, enable ? port : null);
            await _settingsStore.SaveAsync(_orchestrator.Settings, CancellationToken.None);
            var skipped = result.Skipped.Count > 0
                ? Loc.T("persistent.skipped", result.Skipped.Count)
                : string.Empty;
            MessageBox.Show(
                enable
                    ? Loc.T("persistent.doneWindows", result.Updated.Count, display) + skipped
                    : Loc.T("persistent.removed", display) + skipped,
                Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(
                Loc.T("persistent.failed", display, result.Detail ?? "unknown"),
                Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RelaunchAppAsync(DetectedApp app)
    {
        var display = _orchestrator.Profiles.FirstOrDefault(p => p.AppId == app.AppId)?.DisplayName ?? app.AppId;

        Func<RelaunchWarning, Task<bool>> consent = _ =>
        {
            // Say WHY before asking: an app that is already open cannot be
            // joined, so the restart is the fix, not an inconvenience.
            var body = Loc.T("relaunch.why", display) + "\n\n" + Loc.T("relaunch.warnUnsaved", display);
            // MessageBox.Show is thread-safe; the handler runs off the UI thread
            // via async void, which is fine for a modal box.
            var dr = MessageBox.Show(
                Loc.T("relaunch.confirmBody", body),
                Loc.T("relaunch.confirmTitle", display),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            return Task.FromResult(dr == DialogResult.Yes);
        };

        try
        {
            var result = await _orchestrator.RelaunchAsync(app, consent);
            if (result.Success)
            {
                _notify.ShowBalloonTip(3000, Constants.ProductName, Loc.T("relaunch.done", display), ToolTipIcon.Info);
            }
            else if (result.ManualReopen)
            {
                MessageBox.Show(
                    ExplainRelaunchFailure(display, result.Detail),
                    Loc.T("relaunch.manualTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (!result.UserConsented)
            {
                // User declined — nothing to do.
            }
            else
            {
                _notify.ShowBalloonTip(5000, Constants.ProductName,
                    ExplainRelaunchFailure(display, result.Detail), ToolTipIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogCategories.Relaunch, "ui-relaunch-failed", ("app", app.AppId), ("msg", SafeLogger.Redact(ex.Message)));
        }
    }

    private async Task ExportDetectionReportAsync()
    {
        try
        {
            var includePaths = MessageBox.Show("Include executable paths in this user-requested diagnostic export?", "Export Detection Report", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            var path = await DetectionReportExporter.ExportAsync(_orchestrator, includePaths, CancellationToken.None);
            _notify.ShowBalloonTip(3000, Constants.ProductName, "Detection report exported.", ToolTipIcon.Info);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.App, "export-report-failed", ("msg", SafeLogger.Redact(ex.Message)));
        }
    }

    private static string Readable(AppRuntimeState state) => state switch
    {
        AppRuntimeState.RunningNoDebugPort => Loc.T("state.waitingEndpoint"),
        AppRuntimeState.RelaunchRequired or AppRuntimeState.RelaunchPromptShown => Loc.T("state.needsRelaunch"),
        AppRuntimeState.Relaunching or AppRuntimeState.WaitingForCdp => Loc.T("state.waitingEndpoint"),
        AppRuntimeState.CdpUnsupported or AppRuntimeState.DebugArgsIgnored => Loc.T("state.needsRelaunch"),
        AppRuntimeState.InjectionSucceeded => Loc.T("state.working"),
        AppRuntimeState.DisabledByUser => Loc.T("state.disabled"),
        AppRuntimeState.Unsupported => Loc.T("state.unsupported"),
        _ => state.ToString(),
    };

    private void OpenLogs()
    {
        try
        {
            if (!File.Exists(AppPaths.LogPath)) File.WriteAllText(AppPaths.LogPath, "");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{AppPaths.LogPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.App, "open-logs-failed", ("msg", SafeLogger.Redact(ex.Message)));
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            Loc.T("about.body", Constants.ProductName, Constants.AppVersion, Constants.GitHubLink),
            Loc.T("button.about") + " — " + Constants.ProductName,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void ExitApp()
    {
        _notify.Visible = false;
        try
        {
            // Wait for the restore commands instead of exiting while they are
            // still in flight. This makes "Exit" keep its runtime-only promise.
            await _orchestrator.DisableAllAsync();
        }
        finally
        {
            ExitThread();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _orchestrator.Dispose();
            _notify.Dispose();
        }
        base.Dispose(disposing);
    }
}
