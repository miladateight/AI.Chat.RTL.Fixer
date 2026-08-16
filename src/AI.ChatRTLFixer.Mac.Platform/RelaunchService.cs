using System.Diagnostics;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Abstractions;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Diagnostics;

namespace AI.ChatRTLFixer.Mac;

/// <summary>
/// Relaunches an Electron app with CDP debug args. NEVER closes or restarts an
/// app without explicit user consent obtained through <c>consentCallback</c>.
/// Mirrors the Windows <c>RelaunchService</c>; the close step uses POSIX
/// signals (SIGTERM then SIGKILL) instead of WM_CLOSE, and process-args
/// verification shells out to <c>ps</c> instead of WMI.
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
        if (!allowBrowserTargets && BrowserGuard.IsBrowser(app.ProcessName, app.ExecutablePath))
        {
            _logger.Log(LogLevel.Warning, LogCategories.Security, "browser-relaunch-blocked", ("name", app.ProcessName));
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
            var manual = BuildManualCommand(profile, port.Value);
            _logger.Log(LogLevel.Warning, LogCategories.Relaunch, "no-exe-path", ("app", profile.AppId));
            return new RelaunchResult { Success = false, UserConsented = true, ManualReopen = true, ManualCommand = manual, Unsafe = true };
        }

        var originalArgs = ParseArgs(app.CommandLine, exe);
        var debugArgs = profile.Cdp.LaunchArgs.Select(a => a.Replace("${port}", port.Value.ToString()));
        var finalArgs = originalArgs.Concat(debugArgs).ToList();

        try
        {
            using var existing = Process.GetProcessById(app.ProcessId);
            try
            {
                // No WM_CLOSE equivalent on macOS from a background process
                // without Accessibility permissions; SIGTERM is the standard
                // graceful-shutdown request and Electron apps handle it.
                PosixKill(app.ProcessId, sigkill: false);
                if (existing.WaitForExit(2000)) { /* exited gracefully */ }
                else if (!existing.HasExited)
                {
                    _logger.Log(LogLevel.Information, LogCategories.Relaunch, "close-escalated-to-kill", ("app", profile.AppId));
                    PosixKill(app.ProcessId, sigkill: true);
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
            // Launching the executable inside Contents/MacOS/ directly (rather
            // than `open -a`) preserves argv exactly, which CDP requires.
            var startInfo = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var arg in finalArgs) startInfo.ArgumentList.Add(arg);
            var newProc = Process.Start(startInfo);
            var argsVerified = newProc is not null && WaitForDebugArgs(newProc.Id, port.Value, TimeSpan.FromMilliseconds(300));
            _logger.Log(LogLevel.Information, LogCategories.Relaunch, argsVerified ? "args-verified" : "args-unverified", ("app", profile.AppId), ("port", port.Value));
            _logger.Log(LogLevel.Information, LogCategories.Relaunch, "relaunched", ("app", profile.AppId), ("port", port.Value));
            return new RelaunchResult
            {
                Success = true,
                UserConsented = true,
                NewProcessId = newProc?.Id,
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

    internal static IReadOnlyList<string> ParseArgs(string? commandLine, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];

        var rest = commandLine.TrimStart();
        if (!string.IsNullOrWhiteSpace(executablePath) &&
            rest.StartsWith(executablePath, StringComparison.Ordinal) &&
            (rest.Length == executablePath.Length || char.IsWhiteSpace(rest[executablePath.Length])))
        {
            rest = rest[executablePath.Length..].TrimStart();
        }
        else
        {
            var allArguments = PosixCommandLine.Split(rest);
            rest = string.Join(' ', allArguments.Skip(1).Select(PosixCommandLine.Quote));
        }

        return LaunchArgumentSanitizer.RemoveRemoteDebuggingArguments(PosixCommandLine.Split(rest));
    }

    private static bool WaitForDebugArgs(int processId, int port, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var commandLine = ProcessListReader.ListProcesses().FirstOrDefault(p => p.ProcessId == processId)?.CommandLine;
            if (commandLine?.Contains($"--remote-debugging-port={port}", StringComparison.OrdinalIgnoreCase) == true &&
                commandLine.Contains("--remote-debugging-address=127.0.0.1", StringComparison.OrdinalIgnoreCase)) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    /// <summary>Sends SIGTERM (15) or SIGKILL (9) via the <c>kill</c> utility.</summary>
    private static void PosixKill(int pid, bool sigkill)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("kill", $"-{(sigkill ? 9 : 15)} {pid}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            proc?.WaitForExit(1000);
        }
        catch { }
    }
}
