using System.Diagnostics;
using System.Management;
using System.Text;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Diagnostics;
using AI.ChatRTLFixer.Profiles;

namespace AI.ChatRTLFixer.Win32;

/// <summary>
/// Watches for supported AI desktop apps. Uses <see cref="Process"/> for
/// detection and WMI to read command lines (to detect an already-open
/// --remote-debugging-port). Polls on a timer to avoid ETW complexity.
/// </summary>
public sealed class ProcessWatcher : IProcessWatcher
{
    private readonly ProfileRegistry _profiles;
    private readonly SafeLogger _logger;
    private Timer? _timer;
    private readonly Dictionary<int, DetectedApp> _byPid = new();
    private readonly object _gate = new();

    public ProcessWatcher(ProfileRegistry profiles, SafeLogger logger)
    {
        _profiles = profiles;
        _logger = logger;
    }

    public IReadOnlyList<DetectedApp> Snapshot()
    {
        lock (_gate) return _byPid.Values.ToList();
    }

    public event EventHandler<DetectedApp>? AppChanged;
    public event EventHandler<string>? AppExited;

    public void Start()
    {
        _timer?.Dispose();
        _timer = new Timer(_ => PollSafe(), null, 0, 2000);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void PollSafe()
    {
        try { Poll(); }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.ProcessWatcher, "poll-error", ("msg", SafeLogger.Redact(ex.Message)));
        }
    }

    private void Poll()
    {
        var seenPids = new HashSet<int>();
        foreach (var proc in Process.GetProcesses())
        {
            string name;
            try { name = proc.ProcessName; }
            catch { continue; }

            if (!_profiles.TryMatchProcess(name, out var profile)) continue;

            seenPids.Add(proc.Id);
            var cmdLine = TryGetCommandLine(proc);
            var hasPort = cmdLine is not null && cmdLine.Contains("--remote-debugging-port", StringComparison.OrdinalIgnoreCase);
            int? port = null;
            if (hasPort && cmdLine is not null)
            {
                port = ParseDebugPort(cmdLine);
            }

            var detected = new DetectedApp
            {
                AppId = profile.AppId,
                ProcessId = proc.Id,
                ProcessName = name,
                ExecutablePath = TryGetPath(proc),
                CommandLine = cmdLine,
                HasDebugPort = hasPort,
                DebugPort = port,
            };

            lock (_gate)
            {
                _byPid[proc.Id] = detected;
            }
            AppChanged?.Invoke(this, detected);
        }

        // Raise exit for pids no longer present.
        List<int> exited;
        lock (_gate)
        {
            exited = _byPid.Keys.Where(p => !seenPids.Contains(p)).ToList();
            foreach (var p in exited) _byPid.Remove(p);
        }
        foreach (var p in exited)
        {
            AppExited?.Invoke(this, p.ToString());
        }
    }

    private static int? ParseDebugPort(string cmdLine)
    {
        var idx = cmdLine.IndexOf("--remote-debugging-port", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var rest = cmdLine.Substring(idx + "--remote-debugging-port".Length);
        // skip separator
        rest = rest.TrimStart(' ', '=', '\t');
        var digits = new StringBuilder();
        foreach (var ch in rest)
        {
            if (char.IsDigit(ch)) digits.Append(ch); else break;
        }
        return int.TryParse(digits.ToString(), out var p) ? p : null;
    }

    private static string? TryGetCommandLine(Process proc)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + proc.Id);
            foreach (var mo in searcher.Get().Cast<ManagementObject>())
            {
                return mo["CommandLine"] as string;
            }
        }
        catch { }
        return null;
    }

    private static string? TryGetPath(Process proc)
    {
        try { return proc.MainModule?.FileName; }
        catch { return null; }
    }

    public void Dispose()
    {
        Stop();
    }
}