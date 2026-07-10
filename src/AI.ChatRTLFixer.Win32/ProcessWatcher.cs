using System.Diagnostics;
using System.Management;
using System.Text;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Diagnostics;
using AI.ChatRTLFixer.Profiles;

namespace AI.ChatRTLFixer.Win32;

/// <summary>
/// Maintains a diffed snapshot of target processes. The first poll is a full
/// startup snapshot; later polls reconcile the complete process list so the
/// watcher never depends on an application starting after this tool.
/// </summary>
public sealed class ProcessWatcher : IProcessWatcher
{
    private readonly ProfileRegistry _profiles;
    private readonly SafeLogger _logger;
    private readonly Dictionary<int, DetectedApp> _byPid = new();
    private readonly object _gate = new();
    private Timer? _timer;
    private int _portMin = 49152;
    private int _portMax = 65535;
    private int _intervalMs = 3000;
    private int _initialDelayMs;
    private bool _developerDiagnostics;
    private int _polling;
    private bool _initialScanComplete;

    public ProcessWatcher(ProfileRegistry profiles, SafeLogger logger)
    {
        _profiles = profiles;
        _logger = logger;
    }

    public event EventHandler<DetectedApp>? AppChanged;
    public event EventHandler<DetectedApp>? AppExited;

    public void SetPortRange(int min, int max)
    {
        _portMin = Math.Clamp(min, 1, 65535);
        _portMax = Math.Clamp(max, 1, 65535);
        if (_portMin > _portMax) (_portMin, _portMax) = (_portMax, _portMin);
    }

    public void Configure(int reconciliationIntervalSeconds, bool developerDiagnostics, int initialScanDelayMs = 0)
    {
        _intervalMs = Math.Clamp(reconciliationIntervalSeconds, 2, 5) * 1000;
        _developerDiagnostics = developerDiagnostics;
        _initialDelayMs = Math.Clamp(initialScanDelayMs, 0, 10000);
    }

    public IReadOnlyList<DetectedApp> Snapshot()
    {
        lock (_gate) return _byPid.Values.OrderBy(x => x.AppId).ThenBy(x => x.ProcessId).ToList();
    }

    public void Start()
    {
        Stop();
        _initialScanComplete = false;
        _timer = new Timer(_ => PollSafe(), null, _initialDelayMs, _intervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void PollSafe()
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        try { Poll(); }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.ProcessWatcher, "poll-error", ("msg", SafeLogger.Redact(ex.Message)));
        }
        finally { Volatile.Write(ref _polling, 0); }
    }

    private void Poll()
    {
        var initial = !_initialScanComplete;
        if (initial) _logger.Log(LogLevel.Information, LogCategories.ProcessWatcher, "initial-scan-start");

        var seenPids = new HashSet<int>();
        var matched = 0;
        var candidates = 0;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                ProcessSnapshot snapshot;
                try { snapshot = ReadSnapshot(process); }
                catch { continue; }

                if (IsElectronChild(snapshot.CommandLine)) continue;
                candidates++;
                if (!_profiles.TryMatchProcess(snapshot.Name, snapshot.ExecutablePath, snapshot.ProductName,
                    snapshot.FileDescription, snapshot.WindowTitles, snapshot.CommandLine, out var profile, out var reason))
                {
                    if (_developerDiagnostics && LooksLikeAiCandidate(snapshot))
                        _logger.Log(LogLevel.Debug, LogCategories.ProcessWatcher, "ignored-candidate", ("pid", snapshot.ProcessId), ("name", snapshot.Name), ("reason", "no-profile-match"));
                    continue;
                }

                matched++;
                seenPids.Add(snapshot.ProcessId);
                var port = ParseDebugPort(snapshot.CommandLine);
                var detected = new DetectedApp
                {
                    AppId = profile.AppId,
                    ProcessId = snapshot.ProcessId,
                    ProcessName = snapshot.Name,
                    ExecutablePath = snapshot.ExecutablePath,
                    CommandLine = snapshot.CommandLine,
                    HasDebugPort = port is not null,
                    DebugPort = port,
                    PortMin = _portMin,
                    PortMax = _portMax,
                    ProductName = snapshot.ProductName,
                    FileDescription = snapshot.FileDescription,
                    WindowTitles = snapshot.WindowTitles,
                    ParentProcessId = snapshot.ParentProcessId,
                    MatchReason = reason,
                };

                bool changed;
                lock (_gate)
                {
                    changed = !_byPid.TryGetValue(detected.ProcessId, out var old) || !SameState(old, detected);
                    _byPid[detected.ProcessId] = detected;
                }
                if (!changed) continue;
                _logger.Log(LogLevel.Information, LogCategories.ProcessWatcher, "app-detected",
                    ("app", detected.AppId), ("pid", detected.ProcessId), ("name", detected.ProcessName), ("reason", detected.MatchReason));
                AppChanged?.Invoke(this, detected);
            }
        }

        List<DetectedApp> exited;
        lock (_gate)
        {
            exited = _byPid.Where(pair => !seenPids.Contains(pair.Key)).Select(pair => pair.Value).ToList();
            foreach (var app in exited) _byPid.Remove(app.ProcessId);
        }
        foreach (var app in exited)
        {
            // Only tracked applications ever reach this path.
            _logger.Log(LogLevel.Information, LogCategories.ProcessWatcher, "app-exited", ("app", app.AppId), ("pid", app.ProcessId));
            AppExited?.Invoke(this, app);
        }
        if (initial)
        {
            _initialScanComplete = true;
            _logger.Log(LogLevel.Information, LogCategories.ProcessWatcher, "initial-scan-complete", ("matched", matched), ("candidates", candidates));
        }
    }

    private static bool SameState(DetectedApp left, DetectedApp right) =>
        left.AppId == right.AppId && left.HasDebugPort == right.HasDebugPort && left.DebugPort == right.DebugPort &&
        string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase) && left.MatchReason == right.MatchReason;

    private static bool IsElectronChild(string? commandLine) =>
        commandLine?.Contains("--type=", StringComparison.OrdinalIgnoreCase) == true;

    private static bool LooksLikeAiCandidate(ProcessSnapshot snapshot) =>
        (snapshot.Name + " " + snapshot.ProductName + " " + snapshot.FileDescription).Contains("claude", StringComparison.OrdinalIgnoreCase) ||
        (snapshot.Name + " " + snapshot.ProductName + " " + snapshot.FileDescription).Contains("codex", StringComparison.OrdinalIgnoreCase) ||
        (snapshot.Name + " " + snapshot.ProductName + " " + snapshot.FileDescription).Contains("zcode", StringComparison.OrdinalIgnoreCase);

    private static ProcessSnapshot ReadSnapshot(Process process)
    {
        var path = TryGetPath(process);
        var version = TryGetVersionInfo(path);
        var wmi = TryGetWmi(process.Id);
        var title = TryGetWindowTitle(process);
        return new ProcessSnapshot(process.Id, process.ProcessName, path, wmi.CommandLine, wmi.ParentProcessId,
            version.ProductName, version.FileDescription, string.IsNullOrWhiteSpace(title) ? [] : [title]);
    }

    private static (string? CommandLine, int? ParentProcessId) TryGetWmi(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT CommandLine,ParentProcessId FROM Win32_Process WHERE ProcessId = {pid}");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    return (item["CommandLine"] as string, item["ParentProcessId"] is uint parent ? checked((int)parent) : null);
                }
            }
        }
        catch { }
        return (null, null);
    }

    private static (string? ProductName, string? FileDescription) TryGetVersionInfo(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                return (info.ProductName, info.FileDescription);
            }
        }
        catch { }
        return (null, null);
    }

    private static string? TryGetPath(Process process) { try { return process.MainModule?.FileName; } catch { return null; } }
    private static string? TryGetWindowTitle(Process process) { try { return process.MainWindowTitle; } catch { return null; } }

    internal static int? ParseDebugPort(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        var marker = "--remote-debugging-port";
        var start = commandLine.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var rest = commandLine[(start + marker.Length)..].TrimStart(' ', '=', '\t', '"');
        var digits = new StringBuilder();
        foreach (var character in rest) { if (char.IsDigit(character)) digits.Append(character); else break; }
        return int.TryParse(digits.ToString(), out var port) && port is > 0 and <= 65535 ? port : null;
    }

    public void Dispose() => Stop();

    private sealed record ProcessSnapshot(int ProcessId, string Name, string? ExecutablePath, string? CommandLine,
        int? ParentProcessId, string? ProductName, string? FileDescription, string[] WindowTitles);
}
