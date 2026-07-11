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
