using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
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
    private readonly NotifyIcon _notify;
    private readonly ToolStripMenuItem _globalToggleItem;
    private readonly SynchronizationContext _uiContext;

    public TrayApplicationContext(Orchestrator orchestrator, SafeLogger logger, ISettingsStore settingsStore)
    {
        _orchestrator = orchestrator;
        _logger = logger;
        _settingsStore = settingsStore;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _orchestrator.StateChanged += (_, _) =>
        {
            _uiContext.Post(_ => RebuildMenu(), null);
        };

        _globalToggleItem = new ToolStripMenuItem("Enabled", null, (_, _) => ToggleGlobal());

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
        var detected = new ToolStripMenuItem("Detected Apps");
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

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Mi("Settings...", OpenSettings));
        menu.Items.Add(Mi("Open logs", OpenLogs));
        menu.Items.Add(Mi("Export Detection Report", ExportDetectionReportAsync));
        menu.Items.Add(Mi("Reset runtime changes", async () => await _orchestrator.DisableAllAsync()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Mi("About", ShowAbout));
        menu.Items.Add(Mi("Exit", ExitApp));
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

    private void OpenSettings() => new SettingsForm(_orchestrator, _settingsStore, _logger).Show();

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
        AppRuntimeState.RunningNoDebugPort => "Detected, waiting for local endpoint",
        AppRuntimeState.CdpUnsupported => "Detected, CDP unavailable",
        AppRuntimeState.InjectionSucceeded => "Attached",
        AppRuntimeState.Unsupported => "Unsupported / Planned",
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
            $"{Constants.ProductName} v{Constants.AppVersion}\n\n" +
            "A free and open-source Windows tray tool that improves RTL text " +
            "rendering inside AI desktop chat applications. It focuses only on " +
            "the chat area and keeps code, commands, paths and English text " +
            "left-to-right.\n\n" +
            "No telemetry. No external network calls. Only local loopback " +
            "communication with debug-enabled target apps.\n\n" +
            "GitHub: " + Constants.GitHubLink,
            "About " + Constants.ProductName,
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
