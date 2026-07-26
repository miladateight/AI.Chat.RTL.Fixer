using System.Diagnostics;
using System.Text;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Diagnostics;
using AI.ChatRTLFixer.Profiles;

namespace AI.ChatRTLFixer.Mac;

/// <summary>
/// Maintains a diffed snapshot of target processes, mirroring the Windows
/// <c>ProcessWatcher</c>. macOS has no WMI, so process data comes from
/// shelling out to <c>ps</c> (present on every macOS install, no extra
/// entitlements needed for same-user processes) instead of a push-based
/// event API. Detection is therefore poll-only; the interval is the same
/// low-frequency fallback the Windows watcher already uses when WMI process
/// events are unavailable.
/// </summary>
public sealed class ProcessWatcher : IProcessWatcher
{
    private readonly ProfileRegistry _profiles;
    private readonly SafeLogger _logger;
    private readonly Dictionary<int, DetectedApp> _byPid = new();
    private readonly object _gate = new();
    private Timer? _timer;
    private int _intervalMs = 5000;
    private int _initialDelayMs;
    private bool _developerDiagnostics;
    private volatile bool _browserTargetsEnabled;
    private int _polling;
    private bool _initialScanComplete;

    public ProcessWatcher(ProfileRegistry profiles, SafeLogger logger)
    {
        _profiles = profiles;
        _logger = logger;
    }

    public event EventHandler<DetectedApp>? AppChanged;
    public event EventHandler<DetectedApp>? AppExited;

    public void Configure(int reconciliationIntervalSeconds, bool developerDiagnostics, int initialScanDelayMs = 0, bool browserTargetsEnabled = false)
    {
        // No push-based process events on macOS, so poll faster than the
        // Windows default to keep detection responsive.
        _intervalMs = Math.Clamp(reconciliationIntervalSeconds, 3, 60) * 1000;
        _developerDiagnostics = developerDiagnostics;
        _initialDelayMs = Math.Clamp(initialScanDelayMs, 0, 10000);
        _browserTargetsEnabled = browserTargetsEnabled;
    }

    public void SetBrowserTargetsEnabled(bool enabled)
    {
        _browserTargetsEnabled = enabled;
        if (!enabled)
        {
            List<DetectedApp> exited;
            lock (_gate)
            {
                exited = _byPid.Values.Where(app => BrowserGuard.IsBrowser(app.ProcessName, app.ExecutablePath)).ToList();
                foreach (var app in exited) _byPid.Remove(app.ProcessId);
            }
            foreach (var app in exited) AppExited?.Invoke(this, app);
        }
        try { _timer?.Change(enabled ? 0 : _intervalMs, _intervalMs); } catch (ObjectDisposedException) { }
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

        foreach (var snapshot in ProcessListReader.ListProcesses())
        {
            AppProfile profile;
            string reason;
            var name = snapshot.Name;

            if (!_browserTargetsEnabled && BrowserGuard.IsBrowser(name, snapshot.ExecutablePath)) continue;

            if (!_profiles.TryMatchProcess(name, out profile))
            {
                // macOS process titles aren't cheaply queryable per-pid without
                // Accessibility/CGWindowList entitlements, so unlike Windows this
                // path only tries the executable path/version-style fields.
                if (!_profiles.TryMatchProcess(name, snapshot.ExecutablePath, null, null, [], null, out profile, out reason))
                {
                    if (_developerDiagnostics && LooksLikeAiCandidate(snapshot))
                        _logger.Log(LogLevel.Debug, LogCategories.ProcessWatcher, "ignored-candidate", ("pid", snapshot.ProcessId), ("name", name), ("reason", "no-profile-match"));
                    continue;
                }
            }
            else
            {
                reason = "process-name";
            }

            if (IsElectronChild(snapshot.CommandLine)) continue;
            if (!_browserTargetsEnabled && BrowserGuard.IsBrowser(name, snapshot.ExecutablePath)) continue;
            if (IsNonGuiBackend(snapshot.ExecutablePath, snapshot.CommandLine)) continue;
            candidates++;
            matched++;
            seenPids.Add(snapshot.ProcessId);

            var port = ParseDebugPort(snapshot.CommandLine);
            var detected = new DetectedApp
            {
                AppId = profile.AppId,
                ProcessId = snapshot.ProcessId,
                ProcessName = name,
                ExecutablePath = snapshot.ExecutablePath,
                CommandLine = snapshot.CommandLine,
                HasDebugPort = port is not null,
                DebugPort = port,
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

        List<DetectedApp> exited;
        lock (_gate)
        {
            exited = _byPid.Where(pair => !seenPids.Contains(pair.Key)).Select(pair => pair.Value).ToList();
            foreach (var app in exited) _byPid.Remove(app.ProcessId);
        }
        foreach (var app in exited)
        {
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
        string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.Ordinal) && left.MatchReason == right.MatchReason;

    private static bool IsElectronChild(string? commandLine) =>
        commandLine?.Contains("--type=", StringComparison.OrdinalIgnoreCase) == true;

    // Mirrors the Windows watcher's headless-CLI exclusion, adapted to mac paths.
    private static bool IsNonGuiBackend(string? executablePath, string? commandLine)
    {
        var cl = commandLine ?? string.Empty;
        var px = executablePath ?? string.Empty;
        if (cl.Contains("app-server", StringComparison.OrdinalIgnoreCase)) return true;
        if (cl.Contains("stream-json", StringComparison.OrdinalIgnoreCase)) return true;
        if (cl.Contains("--stdio", StringComparison.OrdinalIgnoreCase)) return true;
        if (px.Contains("/claude-code/", StringComparison.OrdinalIgnoreCase)) return true;
        if (px.Contains("/.codex/", StringComparison.OrdinalIgnoreCase)) return true;
        if (px.Contains("/.traycer/cli/", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool LooksLikeAiCandidate(ProcessListReader.ProcessSnapshot snapshot) =>
        snapshot.Name.Contains("claude", StringComparison.OrdinalIgnoreCase) ||
        snapshot.Name.Contains("codex", StringComparison.OrdinalIgnoreCase) ||
        snapshot.Name.Contains("zcode", StringComparison.OrdinalIgnoreCase) ||
        snapshot.Name.Contains("traycer", StringComparison.OrdinalIgnoreCase);

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
}
