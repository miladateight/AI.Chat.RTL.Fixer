using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Localization;
using AI.ChatRTLFixer.Core.Settings;
using AI.ChatRTLFixer.Diagnostics;
using AI.ChatRTLFixer.Profiles;
using AI.ChatRTLFixer.Win32;

namespace AI.ChatRTLFixer.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instanceMutex = new Mutex(initiallyOwned: true, "Local\\AIChatRTLFixer", out var isFirstInstance);
        if (!isFirstInstance) return;

        AppPaths.EnsureDirectories();
        using var logger = new SafeLogger(AppPaths.LogPath, LogLevel.Information, developerMode: false);

        logger.Log(LogLevel.Information, LogCategories.App, "launching", ("version", Constants.AppVersion));

        var settingsStore = new SettingsStore(logger);
        var settings = settingsStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        // The installer can create this entry before the first application run.
        // Read Windows as the source of truth so the Settings checkbox is accurate.
        settings.StartWithWindows = StartupManager.IsEnabled();

        var profiles = new ProfileRegistry();
        var watcher = new ProcessWatcher(profiles, logger);
        watcher.Configure(settings.ReconciliationIntervalSeconds, settings.DeveloperDiagnosticsEnabled, settings.InitialScanDelayMs, settings.EnableBrowserTargets);
        var portPicker = new PortPicker();
        var relaunchService = new RelaunchService(logger, portPicker, settings.PortRange.Min, settings.PortRange.Max);
        var orchestrator = new Orchestrator(logger, profiles, watcher, relaunchService, settingsStore, settings);
        var updateChecker = new UpdateChecker(logger);

        ApplicationConfiguration.Initialize();

        // Language before any other window: every message from here on, including
        // the very first relaunch prompt, has to be in a language the user reads.
        Loc.SetLanguage(settings.UiCulture);
        if (!settings.HasChosenLanguage)
        {
            using var picker = new LanguagePickerForm();
            // Closing the picker without choosing keeps the default rather than
            // blocking startup, and leaves the question to be asked again next run.
            if (picker.ShowDialog() == DialogResult.OK)
            {
                settings.UiCulture = picker.SelectedCode;
                settingsStore.SaveAsync(settings, CancellationToken.None).GetAwaiter().GetResult();
            }
            Loc.SetLanguage(settings.UiCulture);
            logger.Log(LogLevel.Information, LogCategories.App, "language-selected", ("code", Loc.Current.Code));
        }

        Application.Run(new TrayApplicationContext(orchestrator, logger, settingsStore, updateChecker));
    }
}
