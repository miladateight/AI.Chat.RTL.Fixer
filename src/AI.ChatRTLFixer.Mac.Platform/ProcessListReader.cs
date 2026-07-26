using System.Diagnostics;

namespace AI.ChatRTLFixer.Mac;

/// <summary>
/// Reads the current process list via <c>ps</c> (present on every macOS
/// install; no WMI/procfs equivalent exists). Two passes are used because a
/// single-line format cannot unambiguously separate a path-with-spaces
/// field from the fields that follow it: one pass reads the executable path
/// (<c>comm</c>), the other reads the full command line (<c>command</c>),
/// each joined back to its pid.
/// </summary>
internal static class ProcessListReader
{
    public sealed record ProcessSnapshot(int ProcessId, string Name, string? ExecutablePath, string? CommandLine, int? ParentProcessId);

    public static IReadOnlyList<ProcessSnapshot> ListProcesses()
    {
        var ppids = RunPs("-Ao pid=,ppid=");
        var execPaths = RunPs("-Ao pid=,comm=");
        var commandLines = RunPs("-Ao pid=,command=");

        var ppidByPid = new Dictionary<int, int>();
        foreach (var line in ppids)
        {
            var trimmed = line.TrimStart();
            var sep = trimmed.IndexOf(' ');
            if (sep < 0) continue;
            if (int.TryParse(trimmed[..sep], out var pid) && int.TryParse(trimmed[(sep + 1)..].Trim(), out var ppid))
                ppidByPid[pid] = ppid;
        }

        var execByPid = new Dictionary<int, string>();
        foreach (var line in execPaths)
        {
            if (!TrySplitPidAndRest(line, out var pid, out var rest)) continue;
            execByPid[pid] = rest;
        }

        var results = new List<ProcessSnapshot>();
        foreach (var line in commandLines)
        {
            if (!TrySplitPidAndRest(line, out var pid, out var rest)) continue;
            execByPid.TryGetValue(pid, out var execPath);
            ppidByPid.TryGetValue(pid, out var ppid);
            var name = Path.GetFileName(execPath ?? rest.Split(' ', 2)[0]);
            if (string.IsNullOrWhiteSpace(name)) continue;
            results.Add(new ProcessSnapshot(pid, name, execPath, rest, ppid == 0 ? null : ppid));
        }
        return results;
    }

    private static bool TrySplitPidAndRest(string line, out int pid, out string rest)
    {
        pid = 0;
        rest = string.Empty;
        var trimmed = line.TrimStart();
        var sep = trimmed.IndexOf(' ');
        if (sep < 0) return false;
        if (!int.TryParse(trimmed[..sep], out pid)) return false;
        rest = trimmed[(sep + 1)..].TrimStart();
        return !string.IsNullOrWhiteSpace(rest);
    }

    private static IReadOnlyList<string> RunPs(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("ps", args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return [];
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch
        {
            return [];
        }
    }
}
