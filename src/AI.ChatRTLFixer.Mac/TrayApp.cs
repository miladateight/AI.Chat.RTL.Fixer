using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AI.ChatRTLFixer.Mac;

/// <summary>
/// Menu-bar (NSStatusItem, via Avalonia's cross-platform TrayIcon/NativeMenu)
/// equivalent of the Windows tray's TrayApplicationContext. No telemetry; all
/// communication is local loopback (127.0.0.1) only.
/// </summary>
public sealed class TrayApp
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly Orchestrator _orchestrator;
    private readonly SafeLogger _logger;
    private readonly ISettingsStore _settingsStore;
    private readonly UpdateChecker _updateChecker;
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenu _menu = new();
    private SettingsWindow? _settingsWindow;

    public TrayApp(IClassicDesktopStyleApplicationLifetime lifetime, Orchestrator orchestrator, SafeLogger logger, ISettingsStore settingsStore, UpdateChecker updateChecker)
    {
        _lifetime = lifetime;
        _orchestrator = orchestrator;
        _logger = logger;
        _settingsStore = settingsStore;
        _updateChecker = updateChecker;

        _trayIcon = new TrayIcon
        {
            Icon = LoadAppIcon(),
            ToolTipText = Constants.ProductName,
            IsVisible = true,
            Menu = _menu,
        };
        _trayIcon.Clicked += (_, _) => OpenSettings();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _trayIcon });

        _orchestrator.StateChanged += (_, _) => Dispatcher.UIThread.Post(RebuildMenu);

        _orchestrator.Start();
        RebuildMenu();
        if (_orchestrator.Settings.CheckForUpdatesOnStartup)
            _ = CheckForUpdatesAsync(interactive: false);
    }

    private static WindowIcon? LoadAppIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://AI.ChatRTLFixer.Mac/Assets/app-logo.ico"));
            return new WindowIcon(new Bitmap(stream));
        }
        catch
        {
            return null;
        }
    }

    private void RebuildMenu()
    {
        // Avalonia's macOS native menu exporter tracks the NativeMenu instance
        // it was handed and throws if TrayIcon.Menu is ever reassigned to a
        // *different* NativeMenu object ("The menu being updated does not
        // match") — confirmed by a real crash on macOS CI. So the same
        // instance is kept for the app's lifetime and cleared/repopulated
        // in place instead of being replaced.
        _menu.Items.Clear();
        var settings = _orchestrator.Settings;

        var toggle = new NativeMenuItem("RTL Fixer on") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = settings.GlobalEnabled };
        toggle.Click += async (_, _) => await ToggleGlobalAsync();
        _menu.Add(toggle);
        _menu.Add(new NativeMenuItemSeparator());

        var detected = new NativeMenuItem("Detected Apps") { Menu = new NativeMenu() };
        foreach (var status in _orchestrator.RuntimeStatuses)
        {
            var profile = _orchestrator.Profiles.FirstOrDefault(p => p.AppId == status.App.AppId);
            var display = profile?.DisplayName ?? status.App.AppId;
            detected.Menu!.Add(new NativeMenuItem($"{display}: {Readable(status.State)}") { IsEnabled = false });
        }
        if (detected.Menu!.Items.Count == 0)
            detected.Menu.Add(new NativeMenuItem("(none)") { IsEnabled = false });
        _menu.Add(detected);

        var pending = _orchestrator.PendingRelaunch;
        if (pending.Count > 0)
        {
            _menu.Add(new NativeMenuItemSeparator());
            var relaunchMenu = new NativeMenuItem("Relaunch with RTL Fix…") { Menu = new NativeMenu() };
            foreach (var app in pending)
            {
                var displayName = _orchestrator.Profiles.FirstOrDefault(p => p.AppId == app.AppId)?.DisplayName ?? app.AppId;
                var item = new NativeMenuItem(displayName);
                item.Click += async (_, _) => await RelaunchAppAsync(app);
                relaunchMenu.Menu!.Add(item);
            }
            _menu.Add(relaunchMenu);
        }

        _menu.Add(new NativeMenuItemSeparator());
        _menu.Add(Mi("Settings...", OpenSettings));
        var advanced = new NativeMenuItem("Advanced") { Menu = new NativeMenu() };
        advanced.Menu.Add(MiAsync("Check for updates", () => CheckForUpdatesAsync(interactive: true)));
        advanced.Menu.Add(Mi("Open logs", OpenLogs));
        advanced.Menu.Add(MiAsync("Export detection report", ExportDetectionReportAsync));
        advanced.Menu.Add(MiAsync("Reset runtime changes", () => _orchestrator.DisableAllAsync()));
        _menu.Add(advanced);
        _menu.Add(new NativeMenuItemSeparator());
        _menu.Add(Mi("About", ShowAbout));
        _menu.Add(Mi("Exit", ExitApp));
    }

    private static NativeMenuItem Mi(string text, Action handler)
    {
        var item = new NativeMenuItem(text);
        item.Click += (_, _) => handler();
        return item;
    }

    private static NativeMenuItem MiAsync(string text, Func<Task> handlerAsync)
    {
        var item = new NativeMenuItem(text);
        item.Click += async (_, _) => await handlerAsync();
        return item;
    }

    private async Task ToggleGlobalAsync()
    {
        await _orchestrator.SetGlobalEnabledAsync(!_orchestrator.Settings.GlobalEnabled);
        await _settingsStore.SaveAsync(_orchestrator.Settings, CancellationToken.None);
        RebuildMenu();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_orchestrator, _settingsStore, _logger, _updateChecker);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        var result = await _updateChecker.CheckAsync(CancellationToken.None);
        await Dispatcher.UIThread.InvokeAsync(() => PresentUpdateResult(result, interactive));
    }

    private void PresentUpdateResult(UpdateCheckResult result, bool interactive)
    {
        if (result.IsUpdateAvailable)
        {
            if (interactive)
            {
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var open = await Dialogs.ConfirmAsync(Constants.ProductName,
                        $"Version {result.LatestVersion} is available. Open the GitHub release page?");
                    if (open && result.ReleasePage is not null) UpdateChecker.OpenReleasePage(result.ReleasePage);
                });
            }
            // Non-interactive (startup) checks stay silent rather than
            // popping a window unasked; "Check for updates" in the menu
            // always shows the result on demand.
            return;
        }

        if (interactive) Dialogs.Info(Constants.ProductName, result.Message);
    }

    private async Task RelaunchAppAsync(DetectedApp app)
    {
        Func<RelaunchWarning, Task<bool>> consent = warning =>
            Dialogs.ConfirmAsync($"Relaunch {warning.AppDisplayName}", $"{warning.Message}\n\nProceed with relaunch?");

        try
        {
            var result = await _orchestrator.RelaunchAsync(app, consent);
            if (result.Success)
            {
                _logger.Log(LogLevel.Information, LogCategories.Relaunch, "ui-relaunched", ("app", app.AppId), ("port", result.DebugPort ?? 0));
            }
            else if (result.ManualReopen && result.ManualCommand is not null)
            {
                Dialogs.Info("Manual reopen required",
                    $"Automatic relaunch was not possible. Please close the app and reopen it manually:\n\n{result.ManualCommand}");
            }
            else if (!result.UserConsented)
            {
                // User declined — nothing to do.
            }
            else
            {
                Dialogs.Warn(Constants.ProductName, $"Relaunch of {app.AppId} failed: {result.Detail ?? "unknown"}.");
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
            var includePaths = await Dialogs.ConfirmAsync("Export Detection Report",
                "Include executable paths in this user-requested diagnostic export?");
            var path = await DetectionReportExporter.ExportAsync(_orchestrator, includePaths, CancellationToken.None);
            RevealInFinder(path);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.App, "export-report-failed", ("msg", SafeLogger.Redact(ex.Message)));
        }
    }

    private static string Readable(AppRuntimeState state) => state switch
    {
        AppRuntimeState.RunningNoDebugPort => "Detected, waiting for local endpoint",
        AppRuntimeState.RelaunchRequired or AppRuntimeState.RelaunchPromptShown => "Detected — click \"Relaunch with RTL Fix\" to enable",
        AppRuntimeState.Relaunching => "Relaunching…",
        AppRuntimeState.WaitingForCdp => "Relaunched, waiting for local endpoint",
        AppRuntimeState.CdpUnsupported => "Detected, CDP unavailable",
        AppRuntimeState.DebugArgsIgnored => "Detected, debug args ignored by app",
        AppRuntimeState.InjectionSucceeded => "Attached",
        AppRuntimeState.Unsupported => "Unsupported / Planned",
        _ => state.ToString(),
    };

    private void OpenLogs()
    {
        try
        {
            if (!File.Exists(AppPaths.LogPath)) File.WriteAllText(AppPaths.LogPath, "");
            RevealInFinder(AppPaths.LogPath);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.App, "open-logs-failed", ("msg", SafeLogger.Redact(ex.Message)));
        }
    }

    private static void RevealInFinder(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open", ["-R", path]) { UseShellExecute = false });

    private void ShowAbout()
    {
        Dialogs.Info("About " + Constants.ProductName,
            $"{Constants.ProductName} v{Constants.AppVersion}\n\n" +
            "A free and open-source menu bar tool that improves RTL text " +
            "rendering inside AI desktop chat applications. It focuses only on " +
            "the chat area and keeps code, commands, paths and English text " +
            "left-to-right.\n\n" +
            "No telemetry or analytics. Optional update checks contact only GitHub; " +
            "target-app communication stays on local loopback.\n\n" +
            "GitHub: " + Constants.GitHubLink);
    }

    private async void ExitApp()
    {
        _trayIcon.IsVisible = false;
        try
        {
            await _orchestrator.DisableAllAsync();
        }
        finally
        {
            _orchestrator.Dispose();
            _lifetime.Shutdown();
        }
    }
}
