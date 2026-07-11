using System.Diagnostics;
using System.Management;
using System.Text;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Profiles;
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
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;
    private int _intervalMs = 15000;
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

    public void Configure(int reconciliationIntervalSeconds, bool developerDiagnostics, int initialScanDelayMs = 0)
    {
        _intervalMs = Math.Clamp(reconciliationIntervalSeconds, 5, 60) * 1000;
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
        StartProcessEvents();
        _timer = new Timer(_ => PollSafe(), null, _initialDelayMs, _intervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        StopProcessEvents();
    }

    private void StartProcessEvents()
    {
        try
        {
            _startWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            _stopWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            _startWatcher.EventArrived += OnProcessEvent;
            _stopWatcher.EventArrived += OnProcessEvent;
            _startWatcher.Start();
            _stopWatcher.Start();
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Debug, LogCategories.ProcessWatcher, "process-events-unavailable", ("msg", SafeLogger.Redact(ex.Message)));
            StopProcessEvents();
        }
    }

    private void StopProcessEvents()
    {
        foreach (var watcher in new[] { _startWatcher, _stopWatcher })
        {
            if (watcher is null) continue;
            try { watcher.EventArrived -= OnProcessEvent; watcher.Stop(); } catch { }
            watcher.Dispose();
        }
        _startWatcher = null;
        _stopWatcher = null;
    }

    private void OnProcessEvent(object sender, EventArrivedEventArgs args)
    {
        // A short debounce lets a new GUI process publish its window and command
        // line before the scan. The periodic timer remains a low-frequency
        // fallback on systems where WMI process events are unavailable.
        try { _timer?.Change(150, _intervalMs); } catch (ObjectDisposedException) { }
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
                AppProfile profile;
                string reason;
                try
                {
                    var name = process.ProcessName;
                    var title = TryGetWindowTitle(process);
                    if (!_profiles.TryMatchProcess(name, out profile))
                    {
                        // Only GUI processes need the more expensive path and
                        // version-info fallback. Most processes are rejected by
                        // name without touching WMI or their executable image.
                        if (string.IsNullOrWhiteSpace(title)) continue;
                        var path = TryGetPath(process);
                        var version = TryGetVersionInfo(path);
                        if (!_profiles.TryMatchProcess(name, path, version.ProductName,
                            version.FileDescription, [title], null, out profile, out reason))
                        {
                            if (_developerDiagnostics)
                            {
                                var ignored = new ProcessSnapshot(process.Id, name, path, null, null,
                                    version.ProductName, version.FileDescription, [title]);
                                if (LooksLikeAiCandidate(ignored))
                                    _logger.Log(LogLevel.Debug, LogCategories.ProcessWatcher, "ignored-candidate", ("pid", ignored.ProcessId), ("name", ignored.Name), ("reason", "no-profile-match"));
                            }
                            continue;
                        }
                        snapshot = ReadMatchedSnapshot(process, name, path, version, title);
                    }
                    else
                    {
                        reason = "process-name";
                        var path = TryGetPath(process);
                        snapshot = ReadMatchedSnapshot(process, name, path, TryGetVersionInfo(path), title);
                    }
                }
                catch { continue; }

                if (IsElectronChild(snapshot.CommandLine)) continue;
                if (IsNonGuiBackend(snapshot.ExecutablePath, snapshot.CommandLine)) continue;
                candidates++;

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

    // CLI/agent backends share a process name and even an executable name with a
    // desktop GUI, but have no chat window to inject into. They must never be
    // treated as a target: that would create a permanently unavailable entry
    // for a headless session instead of a chat window. Identified by
    // headless CLI signatures on the command line, or by living under a known
    // CLI install directory rather than the packaged desktop-app location.
    private static bool IsNonGuiBackend(string? executablePath, string? commandLine)
    {
        var cl = commandLine ?? string.Empty;
        var px = executablePath ?? string.Empty;
        if (cl.Contains("app-server", StringComparison.OrdinalIgnoreCase)) return true;      // codex agent backend
        if (cl.Contains("stream-json", StringComparison.OrdinalIgnoreCase)) return true;     // claude-code headless I/O
        if (cl.Contains("--stdio", StringComparison.OrdinalIgnoreCase)) return true;         // MCP/stdio agents
        if (px.Contains("\\claude-code\\", StringComparison.OrdinalIgnoreCase)) return true; // claude-code CLI dir
        if (px.Contains("\\.codex\\", StringComparison.OrdinalIgnoreCase)) return true;      // codex CLI dir
        return false;
    }

    private static bool LooksLikeAiCandidate(ProcessSnapshot snapshot) =>
        (snapshot.Name + " " + snapshot.ProductName + " " + snapshot.FileDescription).Contains("claude", StringComparison.OrdinalIgnoreCase) ||
        (snapshot.Name + " " + snapshot.ProductName + " " + snapshot.FileDescription).Contains("codex", StringComparison.OrdinalIgnoreCase) ||
        (snapshot.Name + " " + snapshot.ProductName + " " + snapshot.FileDescription).Contains("zcode", StringComparison.OrdinalIgnoreCase);

    private static ProcessSnapshot ReadMatchedSnapshot(
        Process process, string name, string? path,
        (string? ProductName, string? FileDescription) version, string? title)
    {
        var wmi = TryGetWmi(process.Id);
        return new ProcessSnapshot(process.Id, name, path, wmi.CommandLine, wmi.ParentProcessId,
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
