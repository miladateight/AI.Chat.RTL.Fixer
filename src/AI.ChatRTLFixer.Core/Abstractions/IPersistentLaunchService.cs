namespace AI.ChatRTLFixer.Core.Abstractions;

/// <summary>One shortcut that launches a target app.</summary>
public sealed class LaunchShortcut
{
    /// <summary>Full path of the .lnk file.</summary>
    public required string Path { get; init; }

    /// <summary>Where it lives, for display: "Start menu", "Desktop", "Taskbar".</summary>
    public required string Location { get; init; }

    /// <summary>Arguments currently stored in the shortcut.</summary>
    public string Arguments { get; init; } = string.Empty;

    /// <summary>True when the shortcut already carries the debugging flags.</summary>
    public bool HasDebugArguments { get; init; }
}

/// <summary>Outcome of installing or removing persistent launch flags.</summary>
public sealed class PersistentLaunchResult
{
    public bool Success { get; init; }

    /// <summary>Shortcuts that were successfully rewritten.</summary>
    public IReadOnlyList<LaunchShortcut> Updated { get; init; } = [];

    /// <summary>Shortcuts found but not writable (for example a machine-wide entry).</summary>
    public IReadOnlyList<LaunchShortcut> Skipped { get; init; } = [];

    /// <summary>Port the shortcuts were pointed at.</summary>
    public int? Port { get; init; }

    public string? Detail { get; init; }
}

/// <summary>
/// Makes a target app start with its loopback debugging endpoint already
/// enabled, by rewriting the shortcuts the user launches it from. This is what
/// removes the need to close and reopen the app on every future session: after
/// one setup, every normal start already exposes the endpoint and the fixer
/// simply attaches.
/// </summary>
public interface IPersistentLaunchService
{
    /// <summary>Finds every shortcut that launches <paramref name="executablePath"/>.</summary>
    IReadOnlyList<LaunchShortcut> FindShortcuts(string executablePath);

    /// <summary>
    /// Points every writable shortcut for <paramref name="executablePath"/> at
    /// <paramref name="port"/>. Idempotent: existing debugging flags are
    /// replaced, never duplicated.
    /// </summary>
    PersistentLaunchResult Install(string executablePath, int port);

    /// <summary>Restores every shortcut to its original arguments.</summary>
    PersistentLaunchResult Remove(string executablePath);
}
