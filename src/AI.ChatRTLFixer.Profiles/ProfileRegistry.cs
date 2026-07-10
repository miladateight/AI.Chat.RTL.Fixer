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
            if (windowTitles?.Any(title => MatchesAny(title, p.WindowTitlePatterns)) == true)
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
}
