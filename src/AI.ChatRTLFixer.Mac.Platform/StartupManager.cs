using System.Diagnostics;
using System.Security;

namespace AI.ChatRTLFixer.Mac;

/// <summary>
/// Manages "Start at login" via a per-user LaunchAgent plist under
/// <c>~/Library/LaunchAgents</c> (no admin required, mirrors the Windows
/// HKCU Run-key approach). Reversible: disabling removes the plist and
/// unloads it from launchd.
/// </summary>
public static class StartupManager
{
    private const string Label = "com.aichatrtlfixer.tray";

    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "LaunchAgents", $"{Label}.plist");

    public static bool IsEnabled() => File.Exists(PlistPath);

    public static void SetEnabled(bool enabled, string exePath)
    {
        var path = PlistPath;
        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentException("The startup executable path is required.", nameof(exePath));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, BuildPlist(exePath));
            RunLaunchctl($"load -w \"{path}\"");
        }
        else
        {
            if (File.Exists(path))
            {
                RunLaunchctl($"unload \"{path}\"");
                try { File.Delete(path); } catch { }
            }
        }
    }

    internal static string BuildPlist(string exePath)
    {
        var escapedPath = SecurityElement.Escape(exePath) ?? string.Empty;
        return
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>Label</key>
            <string>{Label}</string>
            <key>ProgramArguments</key>
            <array>
                <string>{escapedPath}</string>
            </array>
            <key>RunAtLoad</key>
            <true/>
        </dict>
        </plist>
        """;
    }

    private static void RunLaunchctl(string args)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("launchctl", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            proc?.WaitForExit(3000);
        }
        catch { }
    }
}
