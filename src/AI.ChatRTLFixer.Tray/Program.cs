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
        AppPaths.EnsureDirectories();
        var logger = new SafeLogger(AppPaths.LogPath, LogLevel.Information, developerMode: false);

        logger.Log(LogLevel.Information, LogCategories.App, "launching", ("version", Constants.AppVersion));

        var settingsStore = new SettingsStore(logger);
        var settings = settingsStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

        var profiles = new ProfileRegistry();
        var portPicker = new PortPicker();
        var watcher = new ProcessWatcher(profiles, logger);
        watcher.SetPortRange(settings.PortRange.Min, settings.PortRange.Max);
        watcher.Configure(settings.ReconciliationIntervalSeconds, settings.DeveloperDiagnosticsEnabled, settings.InitialScanDelayMs);
        var relaunch = new RelaunchService(logger, portPicker);
        var orchestrator = new Orchestrator(logger, profiles, watcher, relaunch, settingsStore, settings);

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(orchestrator, logger, settingsStore));
    }
}
