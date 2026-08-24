using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Localization;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Diagnostics;
using AI.ChatRTLFixer.Profiles;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AI.ChatRTLFixer.Mac;

/// <summary>
/// Menu-bar-only application: no main window, matching the Windows tray app's
/// shape. Bootstraps the same Orchestrator/Watcher/Relaunch pipeline as
/// Program.cs on Windows, wired to macOS-specific IProcessWatcher/IRelaunchService.
/// </summary>
public sealed class App : Application
{
    private TrayApp? _trayApp;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            AppPaths.EnsureDirectories();
            var logger = new SafeLogger(AppPaths.LogPath, LogLevel.Information, developerMode: false);
            logger.Log(LogLevel.Information, LogCategories.App, "launching", ("version", Constants.AppVersion));

            var settingsStore = new SettingsStore(logger);
            var settings = settingsStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            settings.StartWithWindows = StartupManager.IsEnabled();

            var profiles = new ProfileRegistry();
            var watcher = new ProcessWatcher(profiles, logger);
            watcher.Configure(settings.ReconciliationIntervalSeconds, settings.DeveloperDiagnosticsEnabled, settings.InitialScanDelayMs, settings.EnableBrowserTargets);
            var portPicker = new PortPicker();
            var relaunchService = new RelaunchService(logger, portPicker, settings.PortRange.Min, settings.PortRange.Max);
            var orchestrator = new Orchestrator(logger, profiles, watcher, relaunchService, settingsStore, settings);
            var updateChecker = new UpdateChecker(logger);

            // Language before any other window, so every message from here on --
            // including the first relaunch prompt -- is in a language the user reads.
            Loc.SetLanguage(settings.UiCulture);
            if (!settings.HasChosenLanguage)
            {
                var picker = new LanguagePickerWindow();
                picker.Show();
                _ = picker.Selection.ContinueWith(task =>
                {
                    if (task.Result is not string code) return;
                    settings.UiCulture = code;
                    settingsStore.SaveAsync(settings, CancellationToken.None).GetAwaiter().GetResult();
                    Loc.SetLanguage(code);
                    logger.Log(LogLevel.Information, LogCategories.App, "language-selected", ("code", Loc.Current.Code));
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => _trayApp?.Refresh());
                }, TaskScheduler.Default);
            }

            _trayApp = new TrayApp(desktop, orchestrator, logger, settingsStore, updateChecker);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
