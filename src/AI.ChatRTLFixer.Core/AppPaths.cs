namespace AI.ChatRTLFixer.Core;

/// <summary>Resolved filesystem paths under %AppData%.</summary>
public static class AppPaths
{
    public static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.AppDataFolder);

    public static string SettingsPath => Path.Combine(AppDataRoot, Constants.SettingsFileName);

    public static string LogsDir => Path.Combine(AppDataRoot, "logs");

    public static string LogPath => Path.Combine(LogsDir, Constants.LogFileName);

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(LogsDir);
    }
}
