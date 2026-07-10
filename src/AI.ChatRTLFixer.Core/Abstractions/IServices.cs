using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Core.Settings;

namespace AI.ChatRTLFixer.Core.Abstractions;

/// <summary>Loads and saves <see cref="AppSettings"/> atomically.</summary>
public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken ct);
    Task SaveAsync(AppSettings settings, CancellationToken ct);
}

/// <summary>Result of detecting a running target process.</summary>
public sealed class DetectedApp
{
    public required string AppId { get; init; }
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public string? ExecutablePath { get; init; }

    /// <summary>Full command line if available (for detecting an already-open debug port).</summary>
    public string? CommandLine { get; init; }

    /// <summary>True if the process command line already contains --remote-debugging-port.</summary>
    public bool HasDebugPort { get; init; }

    /// <summary>Parsed debug port if <see cref="HasDebugPort"/> is true.</summary>
    public int? DebugPort { get; init; }

    /// <summary>Inclusive min port for the random free picker (from settings).</summary>
    public int PortMin { get; init; } = 49152;

    /// <summary>Inclusive max port for the random free picker (from settings).</summary>
    public int PortMax { get; init; } = 65535;

    public string MatchReason { get; init; } = "process-name";
    public string? ProductName { get; init; }
    public string? FileDescription { get; init; }
    public string[] WindowTitles { get; init; } = [];
    public int? ParentProcessId { get; init; }
}

/// <summary>Watches for supported AI desktop apps starting and exiting.</summary>
public interface IProcessWatcher : IDisposable
{
    /// <summary>Current snapshot of detected supported apps.</summary>
    IReadOnlyList<DetectedApp> Snapshot();

    /// <summary>Raised when a supported app appears or its state changes.</summary>
    event EventHandler<DetectedApp>? AppChanged;

    /// <summary>Raised when a previously detected app exits.</summary>
    event EventHandler<DetectedApp>? AppExited;

    void Start();
    void Stop();
}

/// <summary>Picks a random free TCP port on 127.0.0.1 within a range.</summary>
public interface IPortPicker
{
    /// <summary>Returns a free port, or null if none found in the range.</summary>
    int? PickFreePort(int min, int max);
}

/// <summary>Outcome of a relaunch attempt.</summary>
public sealed class RelaunchResult
{
    public required bool Success { get; init; }
    public required bool UserConsented { get; init; }
    public int? NewProcessId { get; init; }
    public int? DebugPort { get; init; }
    public string? Detail { get; init; }

    /// <summary>True when relaunch is unsafe; profile should be Experimental/Unsupported.</summary>
    public bool Unsafe { get; init; }

    /// <summary>When true, the user chose (or was advised) manual reopen instead of automatic relaunch.</summary>
    public bool ManualReopen { get; init; }

    /// <summary>The command string shown to the user for manual reopen, when applicable.</summary>
    public string? ManualCommand { get; init; }

    /// <summary>Whether the new process command line was observed to retain the CDP arguments.</summary>
    public bool DebugArgsVerified { get; init; }
}

/// <summary>
/// Relaunches an Electron app with CDP debug args. NEVER closes or restarts an
/// app without explicit user consent.
/// </summary>
public interface IRelaunchService
{
    /// <summary>
    /// Ask the user (via the provided consent callback) then relaunch. Preserves
    /// the original command-line arguments as much as safely possible.
    /// </summary>
    Task<RelaunchResult> RelaunchWithRtlFixAsync(
        DetectedApp app,
        AppProfile profile,
        Func<RelaunchWarning, Task<bool>> consentCallback,
        CancellationToken ct);
}

/// <summary>Warning shown to the user before relaunch.</summary>
public sealed class RelaunchWarning
{
    public required string AppDisplayName { get; init; }
    public required string Message { get; init; }
}
