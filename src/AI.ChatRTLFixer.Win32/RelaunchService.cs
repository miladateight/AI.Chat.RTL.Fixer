using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Diagnostics;

namespace AI.ChatRTLFixer.Win32;

/// <summary>
/// Relaunches an Electron app with CDP debug args. NEVER closes or restarts an
/// app without explicit user consent. Handles Electron's single-instance lock:
/// if relaunching with debug args does not enable CDP (because a second instance
/// is rejected), the profile is reported as unsafe and manual reopen is advised.
/// No infinite retry.
/// </summary>
public sealed class RelaunchService : IRelaunchService
{
    private readonly SafeLogger _logger;
    private readonly IPortPicker _portPicker;

    public RelaunchService(SafeLogger logger, IPortPicker portPicker)
    {
        _logger = logger;
        _portPicker = portPicker;
    }

    public async Task<RelaunchResult> RelaunchWithRtlFixAsync(
        DetectedApp app,
        AppProfile profile,
        Func<RelaunchWarning, Task<bool>> consentCallback,
        CancellationToken ct)
    {
        if (profile.Cdp is null)
            return new RelaunchResult { Success = false, UserConsented = false, Detail = "profile has no CDP strategy", Unsafe = true };

        var warning = new RelaunchWarning
        {
            AppDisplayName = profile.DisplayName,
            Message = $"Before continuing, review unsaved work, in-flight messages or sensitive sessions. " +
                      $"'{profile.DisplayName}' will be closed and reopened with the RTL Fix debug mode " +
                      $"(local loopback only, 127.0.0.1).",
        };

        var consented = await consentCallback(warning);
        if (!consented)
        {
            _logger.Log(LogLevel.Information, LogCategories.Relaunch, "user-declined", ("app", profile.AppId));
            return new RelaunchResult { Success = false, UserConsented = false };
        }

        var port = _portPicker.PickFreePort(app.PortMin, app.PortMax);
        if (port is null)
            return new RelaunchResult { Success = false, UserConsented = true, Detail = "no free port", Unsafe = true };

        var exe = app.ExecutablePath;
        if (string.IsNullOrEmpty(exe))
        {
            // Cannot safely relaunch without the executable path -> manual reopen.
            var manual = BuildManualCommand(profile, port.Value);
            _logger.Log(LogLevel.Warning, LogCategories.Relaunch, "no-exe-path", ("app", profile.AppId));
            return new RelaunchResult { Success = false, UserConsented = true, ManualReopen = true, ManualCommand = manual, Unsafe = true };
        }

        // Build args: preserve original args (minus any existing debug args) and append ours.
        var originalArgs = ParseArgs(app.CommandLine);
        var debugArgs = profile.Cdp.LaunchArgs.Select(a => a.Replace("${port}", port.Value.ToString()));
        var finalArgs = originalArgs.Concat(debugArgs).ToArray();

        // Fully terminate the target app before relaunching. Electron chat apps
        // (Claude, ChatGPT/Codex, …) minimise to the tray on window-close, so
        // CloseMainWindow alone never releases Electron's single-instance lock:
        // a debug relaunch would just be forwarded to the surviving instance and
        // exit without ever binding a debug port. We close windows gracefully
        // first, then force-terminate every process running the SAME executable
        // (main GUI + renderer/GPU children). Termination is scoped by exact
        // executable path, so an unrelated process that merely shares a name
        // (e.g. a "claude" CLI vs. Claude Desktop) is never touched.
        try
        {
            foreach (var pid in FindProcessIdsByExecutable(exe, app.ProcessId))
            {
                try
                {
                    using var pr = Process.GetProcessById(pid);
                    if (pr.MainWindowHandle != IntPtr.Zero) pr.CloseMainWindow();
                }
                catch { }
            }
            await Task.Delay(1000, ct);
            // The single-instance lock is held by the MAIN process, so relaunching
            // only requires THAT process to be gone. Auxiliary processes
            // (crashpad-handler is built to outlive the app; a slow renderer/GPU
            // child) do not hold the lock, and blocking on them would leave the app
            // closed and never reopened — the "relaunch just closes it" bug. So we
            // keep force-killing whatever is alive, but only abort if the original
            // main process itself refuses to die.
            List<int> alive;
            var deadline = DateTime.UtcNow.AddSeconds(6);
            while (true)
            {
                alive = FindProcessIdsByExecutable(exe, app.ProcessId);
                if (alive.Count == 0 || DateTime.UtcNow >= deadline) break;
                foreach (var pid in alive)
                {
                    try { using var pr = Process.GetProcessById(pid); pr.Kill(entireProcessTree: true); } catch { }
                }
                await Task.Delay(200, ct);
            }
            if (alive.Contains(app.ProcessId))
            {
                _logger.Log(LogLevel.Warning, LogCategories.Relaunch, "close-failed", ("app", profile.AppId), ("reason", "main-still-running"));
                return new RelaunchResult { Success = false, UserConsented = true, Detail = "close-failed:main-still-running", Unsafe = true };
            }
            if (alive.Count > 0)
                _logger.Log(LogLevel.Information, LogCategories.Relaunch, "relaunch-with-lingering", ("app", profile.AppId), ("count", alive.Count));
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.Relaunch, "close-failed", ("app", profile.AppId), ("msg", SafeLogger.Redact(ex.Message)));
        }

        try
        {
            var newProc = StartTarget(exe, finalArgs, useShell: false);
            if (newProc is null)
                return new RelaunchResult { Success = false, UserConsented = true, Unsafe = true, Detail = "process-start-returned-null" };
            // If the new instance vanished within ~1.5s, it was almost certainly
            // forwarded to a surviving single-instance lock and exited. Wait for the
            // lock to clear and start once more so the user actually gets a window.
            await Task.Delay(1500, ct);
            if (FindProcessIdsByExecutable(exe, app.ProcessId).Count == 0)
            {
                _logger.Log(LogLevel.Information, LogCategories.Relaunch, "start-vanished-retry", ("app", profile.AppId));
                await Task.Delay(1000, ct);
                newProc = StartTarget(exe, finalArgs, useShell: false) ?? newProc;
            }
            var newPid = TryGetId(newProc);
            // Verify by scanning EVERY process running this executable, not just the
            // pid we spawned: packaged/MSIX Electron apps often re-exec into a new
            // pid, so the flag may live on a sibling process. This is only a
            // diagnostic hint now — the orchestrator attaches based on the actual
            // CDP endpoint regardless of this result.
            var argsVerified = WaitForDebugArgs(exe, app.ProcessId, port.Value, TimeSpan.FromSeconds(4));
            _logger.Log(LogLevel.Information, LogCategories.Relaunch, argsVerified ? "args-verified" : "args-ignored", ("app", profile.AppId), ("port", port.Value));
            _logger.Log(LogLevel.Information, LogCategories.Relaunch, "relaunched", ("app", profile.AppId), ("port", port.Value));
            // NOTE: the orchestrator verifies CDP comes up on 127.0.0.1 with a BOUNDED
            // number of retries. If it does not (e.g. Electron single-instance rejected
            // the second instance), the orchestrator reports Experimental/Unsupported.
            return new RelaunchResult
            {
                Success = true,
                UserConsented = true,
                NewProcessId = newPid,
                DebugPort = port,
                DebugArgsVerified = argsVerified,
            };
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, LogCategories.Relaunch, "start-failed", ("app", profile.AppId), ("msg", SafeLogger.Redact(ex.Message)));
            return new RelaunchResult { Success = false, UserConsented = true, Unsafe = true, Detail = SafeLogger.Redact(ex.Message) };
        }
    }

    private static string BuildManualCommand(AppProfile profile, int port)
    {
        var args = string.Join(' ', profile.Cdp!.LaunchArgs.Select(a => a.Replace("${port}", port.ToString())));
        return $"\"{profile.DisplayName}\" {args}";
    }

    private static IEnumerable<string> ParseArgs(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine)) return [];
        var rawArgs = SplitWindowsCommandLine(commandLine).Skip(1).ToList();
        var result = new List<string>();
        for (var index = 0; index < rawArgs.Count; index++)
        {
            var argument = rawArgs[index];
            if (!argument.StartsWith("--remote-debugging-", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(argument);
                continue;
            }

            // Chromium accepts both --flag=value and --flag value. Remove the
            // value in the latter form too, otherwise a stale port/address can
            // become an orphan argument during relaunch.
            if (!argument.Contains('=') && index + 1 < rawArgs.Count && !rawArgs[index + 1].StartsWith("--", StringComparison.Ordinal))
                index++;
        }
        return result;
    }

    private static IReadOnlyList<string> SplitWindowsCommandLine(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == IntPtr.Zero || count <= 0) return [];
        try
        {
            var args = new string[count];
            for (var index = 0; index < count; index++)
                args[index] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, index * IntPtr.Size)) ?? string.Empty;
            return args;
        }
        finally
        {
            _ = LocalFree(argv);
        }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    /// <summary>
    /// Returns the PIDs of every process whose executable path matches <paramref name="exe"/>
    /// (the main GUI process plus Electron renderer/GPU children, which all run the
    /// same binary). Matching by exact path means a process that only shares a name
    /// (e.g. a "claude" CLI running from a different folder) is deliberately excluded.
    /// </summary>
    private static List<int> FindProcessIdsByExecutable(string exe, int knownPid)
    {
        var pids = new List<int>();
        try
        {
            var escaped = exe.Replace("\\", "\\\\").Replace("'", "\\'");
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ProcessId FROM Win32_Process WHERE ExecutablePath = '{escaped}'");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (item["ProcessId"] is uint pid) pids.Add(checked((int)pid));
                }
            }
        }
        catch { }
        if (pids.Count == 0)
        {
            // WMI unavailable: fall back to the single known PID so we still try.
            try { using var _ = Process.GetProcessById(knownPid); pids.Add(knownPid); } catch { }
        }
        return pids;
    }

    /// <summary>
    /// Starts the target executable with the given arguments. Tries a direct
    /// (UseShellExecute=false) launch first; if the OS refuses to execute the
    /// binary directly (some installed/packaged apps), falls back to a shell
    /// launch which routes through the OS launcher. Arguments are always passed
    /// via ArgumentList so paths with spaces are quoted correctly.
    /// </summary>
    private Process? StartTarget(string exe, string[] args, bool useShell)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = useShell };
            foreach (var a in args) psi.ArgumentList.Add(a);
            return Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception) when (!useShell)
        {
            _logger.Log(LogLevel.Information, LogCategories.Relaunch, "start-shell-fallback");
            var psi = new ProcessStartInfo(exe) { UseShellExecute = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            return Process.Start(psi);
        }
    }

    private static int? TryGetId(Process? p) { try { return p?.Id; } catch { return null; } }

    private static bool WaitForDebugArgs(string exe, int knownPid, int port, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            foreach (var pid in FindProcessIdsByExecutable(exe, knownPid))
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                    using var results = searcher.Get();
                    foreach (ManagementObject item in results)
                    {
                        using (item)
                        {
                            var commandLine = item["CommandLine"] as string;
                            if (commandLine?.Contains($"--remote-debugging-port={port}", StringComparison.OrdinalIgnoreCase) == true) return true;
                        }
                    }
                }
                catch { }
            }
            Thread.Sleep(150);
        }
        return false;
    }
}
