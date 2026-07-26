using AI.ChatRTLFixer.Core.Profiles;

namespace AI.ChatRTLFixer.Profiles;

/// <summary>
/// Looks up a profile by app id. Built-in profiles are registered here; future
/// versions can add user-loaded profiles without changing call sites.
/// </summary>
public sealed class ProfileRegistry
{
    private readonly Dictionary<string, AppProfile> _byId;

    public ProfileRegistry() : this(BuiltinProfiles.All) { }

    public ProfileRegistry(IEnumerable<AppProfile> profiles)
    {
        _byId = profiles.ToDictionary(p => p.AppId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<AppProfile> All => _byId.Values;

    public bool TryGet(string appId, out AppProfile profile) => _byId.TryGetValue(appId, out profile!);

    /// <summary>Finds a profile using independent process, path, version-info, title and command-line signals.</summary>
    public bool TryMatchProcess(string processName, out AppProfile profile)
        => TryMatchProcess(processName, null, null, null, null, null, out profile, out _);

    public bool TryMatchProcess(
        string processName, string? executablePath, string? productName, string? fileDescription,
        IEnumerable<string>? windowTitles, string? commandLine, out AppProfile profile, out string reason)
    {
        var name = Path.GetFileNameWithoutExtension(processName);
        profile = null!;
        reason = string.Empty;
        foreach (var p in _byId.Values)
        {
            if (p.ProcessNames.Any(pn => string.Equals(Path.GetFileNameWithoutExtension(pn), name, StringComparison.OrdinalIgnoreCase)))
                return Match(p, "process-name", out profile, out reason);
            if (MatchesAny(executablePath, p.ExecutablePathPatterns))
                return Match(p, "executable-path", out profile, out reason);
            if (MatchesAny(productName, p.ProductNamePatterns) || MatchesAny(fileDescription, p.ProductNamePatterns))
                return Match(p, "version-info", out profile, out reason);
            if (windowTitles?.Any(title => MatchesWindowTitle(title, p.WindowTitlePatterns)) == true)
                return Match(p, "window-title", out profile, out reason);
            if (MatchesAny(commandLine, p.CommandLinePatterns))
                return Match(p, "command-line", out profile, out reason);
        }
        return false;
    }

    private static bool Match(AppProfile candidate, string matchReason, out AppProfile profile, out string reason)
    {
        profile = candidate;
        reason = matchReason;
        return true;
    }

    private static bool MatchesAny(string? value, IEnumerable<string> patterns)
        => !string.IsNullOrWhiteSpace(value) && patterns.Any(pattern =>
            value.Contains(pattern.Replace("**", string.Empty).Trim('*', '\\', '/'), StringComparison.OrdinalIgnoreCase));

    private static readonly char[] TitleSeparators = [' ', '-', '–', '—', '|', ':', '·', '•', '(', ')', '[', ']'];

    // A viewer/editor window showing a document or media file (e.g. an image
    // named "ChatGPT-screenshot.png") must never be mistaken for the real
    // desktop app just because the filename happens to contain the app's
    // name. Window titles are the weakest signal TryMatchProcess uses, so
    // only accept them when the pattern is the app's own title, not a
    // substring buried inside an unrelated file name or document heading.
    private static readonly string[] NonTargetTitleExtensions =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico", ".heic",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf",
        ".mp4", ".mp3", ".wav", ".mov", ".avi", ".mkv", ".zip", ".rar", ".7z",
    ];

    private static bool MatchesWindowTitle(string? title, IEnumerable<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var trimmed = title.Trim();
        if (NonTargetTitleExtensions.Any(ext => trimmed.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return false;

        var tokens = trimmed.Split(TitleSeparators, StringSplitOptions.RemoveEmptyEntries);
        return patterns.Any(pattern =>
        {
            var needle = pattern.Replace("**", string.Empty).Trim('*', '\\', '/');
            return tokens.Any(token => string.Equals(token, needle, StringComparison.OrdinalIgnoreCase));
        });
    }
}
