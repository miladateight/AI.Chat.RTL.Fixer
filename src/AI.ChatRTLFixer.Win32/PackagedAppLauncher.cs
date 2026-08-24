using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AI.ChatRTLFixer.Win32;

/// <summary>
/// Reopens a Store-installed (MSIX) app the way Windows itself does.
///
/// <para>
/// Starting the executable inside <c>Program Files\WindowsApps</c> directly
/// produces a process with no package identity. Some packaged apps tolerate
/// that; others re-launch themselves properly and let the directly-started
/// process exit, which leaves a window where the app is simply gone. This is
/// the recovery path: activate the package so the app comes back the same way
/// the Start menu would open it.
/// </para>
///
/// <para>
/// Activation cannot carry command-line arguments, so a recovered app has no
/// debugging endpoint — it is a rescue, not a fix. Being open without the fix is
/// strictly better than being closed.
/// </para>
/// </summary>
public static class PackagedAppLauncher
{
    /// <summary>
    /// Derives the package family name from a WindowsApps executable path.
    /// The folder is <c>Name_Version_Arch__PublisherId</c> and the family name
    /// is <c>Name_PublisherId</c>.
    /// </summary>
    /// <returns>The family name, or null when the path is not a packaged app.</returns>
    public static string? TryGetPackageFamilyName(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        var match = Regex.Match(
            executablePath,
            @"\\WindowsApps\\(?<name>[^\\_]+)_[^\\]*?__(?<publisher>[A-Za-z0-9]+)\\",
            RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return match.Groups["name"].Value + "_" + match.Groups["publisher"].Value;
    }

    /// <summary>
    /// Opens a packaged app through Launch Services. Returns false when the path
    /// is not a packaged app or the shell refuses the activation.
    /// </summary>
    public static bool TryActivate(string? executablePath, string appId = "App")
    {
        var family = TryGetPackageFamilyName(executablePath);
        if (family is null) return false;
        try
        {
            // UseShellExecute is required: shell:AppsFolder is a shell namespace
            // path, not something CreateProcess can run.
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = $"shell:AppsFolder\\{family}!{appId}",
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True when any process is currently running from <paramref name="executablePath"/>.
    /// Used to decide whether a relaunch left the user with nothing open.
    /// </summary>
    public static bool IsAnyInstanceRunning(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        var name = Path.GetFileNameWithoutExtension(executablePath);
        try
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (!process.HasExited) return true;
                }
            }
        }
        catch
        {
            // Cannot enumerate: assume something is running rather than launching
            // a duplicate copy on top of the user's session.
            return true;
        }
        return false;
    }
}
