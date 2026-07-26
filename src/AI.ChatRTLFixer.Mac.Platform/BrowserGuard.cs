namespace AI.ChatRTLFixer.Mac;

/// <summary>
/// Identifies consumer web browsers so browser targeting remains an explicit,
/// user-controlled opt-in rather than an accidental profile match.
/// </summary>
public static class BrowserGuard
{
    private static readonly HashSet<string> ProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "google chrome", "chromium", "firefox", "microsoft edge", "brave browser",
        "opera", "vivaldi", "waterfox", "librewolf", "safari",
    };

    public static bool IsBrowser(string? processName, string? executablePath)
    {
        var name = Path.GetFileNameWithoutExtension(processName ?? executablePath ?? string.Empty);
        return ProcessNames.Contains(name);
    }
}
