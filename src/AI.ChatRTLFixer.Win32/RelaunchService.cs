using System.Diagnostics;
using System.Management;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Diagnostics;

namespace AI.ChatRTLFixer.Win32;

/// <summary>
/// Relaunches an Electron app with CDP debug args. NEVER closes or restarts an
/// app without explicit user consent obtained through <c>consentCallback</c>.
/// Handles Electron's single-instance lock: if relaunching with debug args does
/// not enable CDP (because a second instance is rejected), the caller falls
/// back to manual reopen guidance. No infinite retry.
/// </summary>
public sealed class RelaunchService : IRelaunchService
{
    private readonly SafeLogger _logger;
    private readonly IPortPicker _portPicker;
    private readonly int _portMin;
    private readonly int _portMax;

    public RelaunchService(SafeLogger logger, IPortPicker portPicker, int portMin, int portMax)
    {
        _logger = logger;
        _portPicker = portPicker;
        _portMin = portMin;
        _portMax = portMax;
    }

    public async Task<RelaunchResult> RelaunchWithRtlFixAsync(
        DetectedApp app,
        AppProfile profile,
        bool allowBrowserTargets,
        Func<RelaunchWarning, Task<bool>> consentCallback,
        CancellationToken ct)
    {
        // Defence in depth: ProcessWatcher excludes browsers by default, but
        // relaunch independently enforces the same explicit opt-in.
        if (!allowBrowserTargets && BrowserGuard.IsBrowser(app.ProcessName, app.ExecutablePath))
        {
            _logger.Log(LogLevel.Warning, LogCategories.Security, "browser-relaunch-blocked",
                ("name", app.ProcessName));
            return new RelaunchResult { Success = false, UserConsented = false, Detail = "browser-relaunch-blocked", Unsafe = true };
        }

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

        var port = _portPicker.PickFreePort(_portMin, _portMax);
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
        var originalArgs = ParseArgs(app.CommandLine, exe);
        var debugArgs = profile.Cdp.LaunchArgs.Select(a => a.Replace("${port}", port.Value.ToString()));
        var finalArgs = string.Join(' ', originalArgs.Concat(debugArgs));

        // NEVER close an app we are not sure we can start again. Closing first
        // and discovering the executable is unreachable leaves the user staring
        // at an app that vanished and will not come back — the single worst
        // thing this tool can do to someone mid-conversation.
        if (!File.Exists(exe))
        {
            _logger.Log(LogLevel.Warning, LogCategories.Relaunch, "exe-missing", ("app", profile.AppId));
            return new RelaunchResult
            {
                Success = false,
                UserConsented = true,
                ManualReopen = true,
                ManualCommand = BuildManualCommand(profile, port.Value),
                Unsafe = true,
                Detail = "executable-not-found",
            };
        }

        // A second live main process of the same app holds Electron's
        // single-instance lock. Killing this one and starting a replacement then
        // hands the launch straight back to the survivor, which has no debug
        // port: the app appears not to reopen and the fix never engages. Refuse
        // rather than close something for no gain.
        var siblings = CountOtherMainProcesses(app.ProcessId, exe);
        if (siblings > 0)
        {
            _logger.Log(LogLevel.Warning, LogCategories.Relaunch, "other-instances-running",
                ("app", profile.AppId), ("count", siblings));
            return new RelaunchResult
            {
                Success = false,
                UserConsented = true,
                ManualReopen = true,
                ManualCommand = BuildManualCommand(profile, port.Value),
                Unsafe = true,
                Detail = "other-windows-open:" + siblings,
            };
        }

        try
        {
            // Close the existing process. Try graceful first (WM_CLOSE): some apps
            // exit outright, but many "minimize to tray" apps intercept this and
            // just hide their window while the process keeps running in the
            // background — WaitForExit would then never return on its own. The
            // user already explicitly consented to THIS app being closed and
            // reopened (the warning dialog above), so if it is still alive after
            // a short grace period we escalate to actually terminating it —
            // otherwise the relaunch the user asked for would silently never
            // happen and the fixer would sit stuck at "waiting" forever.
            using var existing = Process.GetProcessById(app.ProcessId);
            try
            {
                var closedMainWindow = existing.CloseMainWindow();
                if (existing.WaitForExit(2000)) { /* exited gracefully */ }
                else if (!existing.HasExited)
                {
                    _logger.Log(LogLevel.Information, LogCategories.Relaunch, "close-escalated-to-kill",
                        ("app", profile.AppId), ("hadMainWindow", closedMainWindow));
                    existing.Kill(entireProcessTree: true);
                    if (!existing.WaitForExit(3000)) throw new TimeoutException("timeout");
                }
            }
            catch (Exception ex)
            {
                var reason = ex is TimeoutException ? "timeout" : "unknown";
                _logger.Log(LogLevel.Warning, LogCategories.Relaunch, "close-failed", ("app", profile.AppId), ("reason", reason));
                return new RelaunchResult { Success = false, UserConsented = true, Detail = "close-failed:" + reason, Unsafe = true };
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.Relaunch, "close-failed", ("app", profile.AppId), ("msg", SafeLogger.Redact(ex.Message)));
        }

        try
        {
            var startInfo = new ProcessStartInfo(exe, finalArgs) { UseShellExecute = false };
            var newProc = Process.Start(startInfo);

            // The app is closed at this point, so "did it actually come back?"
            // is the only question that matters. Reporting success merely
            // because Process.Start returned is what let a launch that died
            // immediately — an Electron instance handing off to a single-instance
            // lock, or a packaged app refusing to run — look like it had worked,
            // leaving the user with a closed app and a cheerful message.
            var alive = newProc is not null && SurvivedStartup(newProc, TimeSpan.FromSeconds(6));
            if (!alive)
            {
                // The app we closed is not running. Nothing about the fix matters
                // now compared with giving the user their app back, so activate
                // the package the way the Start menu would. That start carries no
                // debugging endpoint, which is the correct trade: open without the
                // fix beats closed with it.
                var recovered = false;
                if (!PackagedAppLauncher.IsAnyInstanceRunning(exe))
                    recovered = PackagedAppLauncher.TryActivate(exe);

                _logger.Log(LogLevel.Warning, LogCategories.Relaunch, "relaunch-did-not-stay-up",
                    ("app", profile.AppId), ("port", port.Value), ("recovered", recovered));

                return new RelaunchResult
                {
                    Success = false,
                    UserConsented = true,
                    ManualReopen = true,
                    ManualCommand = BuildManualCommand(profile, port.Value),
                    Unsafe = true,
                    Detail = recovered ? "reopened-without-fix" : "did-not-stay-open",
                };
            }

            var argsVerified = WaitForDebugArgs(newProc!.Id, port.Value, TimeSpan.FromMilliseconds(500));
            _logger.Log(LogLevel.Information, LogCategories.Relaunch, argsVerified ? "args-verified" : "args-unverified", ("app", profile.AppId), ("port", port.Value));
            _logger.Log(LogLevel.Information, LogCategories.Relaunch, "relaunched", ("app", profile.AppId), ("port", port.Value), ("pid", newProc.Id));
            // NOTE: the orchestrator still verifies CDP comes up on 127.0.0.1 with
            // a BOUNDED number of retries; this only guarantees the app is back.
            return new RelaunchResult
            {
                Success = true,
                UserConsented = true,
                NewProcessId = newProc.Id,
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

    /// <summary>
    /// True when the freshly started process is still alive after
    /// <paramref name="grace"/>. An Electron instance that hands off to an
    /// existing single-instance lock exits within a fraction of a second, as
    /// does a packaged app that refuses to run outside its package.
    /// </summary>
    private static bool SurvivedStartup(Process process, TimeSpan grace)
    {
        try
        {
            // WaitForExit returning true means it exited inside the window,
            // which is exactly the failure we are looking for.
            return !process.WaitForExit((int)grace.TotalMilliseconds);
        }
        catch
        {
            // Cannot observe it (already reaped, access denied): fall back to a
            // direct look rather than assuming either outcome.
            try { return !process.HasExited; } catch { return false; }
        }
    }

    /// <summary>
    /// Counts other MAIN processes of the same executable — Electron helpers
    /// (<c>--type=</c>) excluded. Each main process is a separate window the
    /// user has open, and any one of them holds the single-instance lock.
    /// </summary>
    /// <remarks>
    /// Public so it can be exercised directly: the alternative way to check this
    /// gate is to run a real relaunch, and a bug here closes somebody's chat app
    /// without being able to bring it back. Read-only — it only queries WMI.
    /// </remarks>
    public static int CountOtherMainProcesses(int selfPid, string exe)
    {
        var count = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    if (item["ProcessId"] is not uint pid || (int)pid == selfPid) continue;
                    if (item["ExecutablePath"] is not string path) continue;
                    if (!string.Equals(path, exe, StringComparison.OrdinalIgnoreCase)) continue;
                    var commandLine = item["CommandLine"] as string;
                    // Only other MAIN processes matter; helpers die with their parent.
                    if (commandLine?.Contains("--type=", StringComparison.OrdinalIgnoreCase) != false) continue;
                    count++;
                }
            }
        }
        catch
        {
            // Cannot tell — treat as none rather than blocking a relaunch that
            // would otherwise be fine. The post-launch verification below still
            // catches a launch that does not come up.
        }
        return count;
    }

    private static string BuildManualCommand(AppProfile profile, int port)
    {
        var args = string.Join(' ', profile.Cdp!.LaunchArgs.Select(a => a.Replace("${port}", port.ToString())));
        return $"\"{profile.DisplayName}\" {args}";
    }

    private static IEnumerable<string> ParseArgs(string? commandLine, string exe)
    {
        if (string.IsNullOrEmpty(commandLine)) return [];
        // Strip the executable (quoted or not) from the front.
        var rest = commandLine;
        if (rest.StartsWith('"'))
        {
            var close = rest.IndexOf('"', 1);
            if (close > 0) rest = rest[(close + 1)..];
        }
        else
        {
            var space = rest.IndexOf(' ');
            if (space > 0) rest = rest[space..]; else rest = "";
        }
        // Remove any existing debug args to avoid duplicates.
        var tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return LaunchArgumentSanitizer.RemoveRemoteDebuggingArguments(tokens);
    }

    private static bool WaitForDebugArgs(int processId, int port, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
                using var results = searcher.Get();
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        var commandLine = item["CommandLine"] as string;
                        if (commandLine?.Contains($"--remote-debugging-port={port}", StringComparison.OrdinalIgnoreCase) == true &&
                            commandLine.Contains("--remote-debugging-address=127.0.0.1", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }
            catch { }
            Thread.Sleep(100);
        }
        return false;
    }
}
