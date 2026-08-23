namespace AI.ChatRTLFixer.Core;

/// <summary>
/// Helpers for making a target app's local debugging flags PERSISTENT, so the
/// app already exposes its loopback endpoint the next time the user starts it
/// normally and never has to be closed and reopened again.
///
/// <para>
/// Attaching to an Electron/Chromium process that is ALREADY running is not
/// possible: the debugging endpoint is bound once, during process startup, from
/// the command line. Nothing can turn it on afterwards short of injecting a
/// remote thread into another process, which this app deliberately does not do.
/// The supported alternative is to put the flags on the shortcuts the user
/// launches the app from, so every future start already has the endpoint and
/// only the current session needs one final relaunch.
/// </para>
///
/// <para>
/// That requires a port that stays the same across restarts, because a shortcut
/// is static: <see cref="DeriveStablePort"/> maps an app id onto the dynamic
/// port range deterministically, so the same app always gets the same port on
/// the same machine without storing a lookup table.
/// </para>
/// </summary>
public static class PersistentLaunchFlags
{
    /// <summary>
    /// Deterministically maps an app id onto <paramref name="min"/>..<paramref name="max"/>.
    /// Stable across restarts and machines: the same id always yields the same
    /// port, so a shortcut written once keeps working.
    /// </summary>
    /// <remarks>
    /// FNV-1a rather than <see cref="string.GetHashCode()"/>, whose value is
    /// randomised per process in .NET Core and would hand out a different port
    /// on every run.
    /// </remarks>
    public static int DeriveStablePort(string appId, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(appId)) throw new ArgumentException("appId is required", nameof(appId));
        if (min < 1 || min > 65535) throw new ArgumentOutOfRangeException(nameof(min));
        if (max < min || max > 65535) throw new ArgumentOutOfRangeException(nameof(max));

        unchecked
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            var hash = offsetBasis;
            foreach (var c in appId)
            {
                hash ^= char.ToLowerInvariant(c);
                hash *= prime;
            }
            var span = (uint)(max - min + 1);
            return min + (int)(hash % span);
        }
    }

    /// <summary>
    /// Builds the loopback debugging arguments for <paramref name="port"/>.
    /// The bind address is always 127.0.0.1: the endpoint must never be
    /// reachable from outside the machine.
    /// </summary>
    public static IReadOnlyList<string> BuildDebugArguments(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        return
        [
            $"--remote-debugging-port={port}",
            $"--remote-debugging-address={Constants.LoopbackAddress}",
        ];
    }

    /// <summary>
    /// True when <paramref name="executablePath"/> belongs to a Windows packaged
    /// (MSIX/Store) app, which lives under <c>Program Files\WindowsApps</c>.
    ///
    /// <para>
    /// This matters because such an app has no .lnk to edit: Windows starts it
    /// through package activation, and even its pinned tile resolves to an
    /// AppsFolder entry rather than a shortcut carrying arguments. Persistent
    /// launch flags therefore cannot be installed for it, and the UI needs to
    /// say so instead of reporting a missing shortcut the user could go and
    /// create.
    /// </para>
    /// </summary>
    public static bool IsWindowsPackagedApp(string? executablePath)
        => !string.IsNullOrWhiteSpace(executablePath) &&
           executablePath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="arguments"/> already carries a debugging flag.</summary>
    public static bool HasRemoteDebuggingArguments(IEnumerable<string> arguments)
        => arguments.Count() != LaunchArgumentSanitizer.RemoveRemoteDebuggingArguments(arguments).Count;

    /// <summary>
    /// Rewrites an existing argument string so it carries exactly the debugging
    /// flags for <paramref name="port"/>: any previous debugging flags are
    /// stripped first, so applying this repeatedly is idempotent and never
    /// accumulates duplicates. All other arguments keep their original order.
    /// </summary>
    public static string ApplyDebugArguments(string? existingArguments, int port)
    {
        var tokens = Tokenize(existingArguments);
        var kept = LaunchArgumentSanitizer.RemoveRemoteDebuggingArguments(tokens);
        return string.Join(' ', kept.Concat(BuildDebugArguments(port)));
    }

    /// <summary>
    /// Removes every debugging flag from <paramref name="existingArguments"/>,
    /// restoring the shortcut to how the app's own installer left it.
    /// </summary>
    public static string RemoveDebugArguments(string? existingArguments)
        => string.Join(' ', LaunchArgumentSanitizer.RemoveRemoteDebuggingArguments(Tokenize(existingArguments)));

    /// <summary>
    /// Splits a Windows argument string on whitespace while keeping quoted runs
    /// (such as a path containing spaces) intact.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return [];
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var c in arguments)
        {
            if (c == '"') { inQuotes = !inQuotes; current.Append(c); continue; }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }
}
