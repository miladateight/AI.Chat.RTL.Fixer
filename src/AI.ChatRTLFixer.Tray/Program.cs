using AI.ChatRTLFixer.Core;
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

        var profiles = new ProfileRegistry();
        var watcher = new ProcessWatcher(profiles, logger);
        watcher.Configure(settings.ReconciliationIntervalSeconds, settings.DeveloperDiagnosticsEnabled, settings.InitialScanDelayMs);
        var orchestrator = new Orchestrator(logger, profiles, watcher, settingsStore, settings);

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(orchestrator, logger, settingsStore));
    }
}
