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

    /// <summary>Finds a profile whose process names match the given process name (case-insensitive).</summary>
    public bool TryMatchProcess(string processName, out AppProfile profile)
    {
        var name = Path.GetFileNameWithoutExtension(processName);
        profile = null!;
        foreach (var p in _byId.Values)
        {
            foreach (var pn in p.ProcessNames)
            {
                if (string.Equals(pn, name, StringComparison.OrdinalIgnoreCase))
                {
                    profile = p;
                    return true;
                }
            }
        }
        return false;
    }
}