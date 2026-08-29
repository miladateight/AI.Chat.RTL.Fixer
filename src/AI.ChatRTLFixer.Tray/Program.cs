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
        // Held for the life of the process. The second copy does not exit here:
        // it reads settings first so it can say so in the user's own language,
        // which is the whole point of an app for people who read right-to-left.
        using var instanceMutex = new Mutex(initiallyOwned: true, "Local\\AIChatRTLFixer", out var isFirstInstance);

        AppPaths.EnsureDirectories();
        using var logger = new SafeLogger(AppPaths.LogPath, LogLevel.Information, developerMode: false);

        logger.Log(LogLevel.Information, LogCategories.App, "launching", ("version", Constants.AppVersion));

        var settingsStore = new SettingsStore(logger);
        var settings = settingsStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        // The installer can create this entry before the first application run.
        // Read Windows as the source of truth so the Settings checkbox is accurate.
        //
        // Not in the packaged build: a package's writes to the Run key land in a
        // virtualised copy that the Windows startup scan never reads, so the
        // value there would describe nothing. Startup for that build is the
        // package's own startup task, controlled from Settings > Apps > Startup.
        if (!PackageContext.IsPackaged)
            settings.StartWithWindows = StartupManager.IsEnabled();

        if (!isFirstInstance)
        {
            // Exiting quietly made a tray app look broken: nothing appeared, so
            // the natural next move was to launch it again. Now it says where the
            // copy that is already running can be found.
            ApplicationConfiguration.Initialize();
            Loc.SetLanguage(settings.UiCulture);
            MessageBox.Show(
                Loc.T("app.alreadyRunning"),
                Constants.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

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
