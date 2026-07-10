using System.Diagnostics;
using System.Management;
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
        var originalArgs = ParseArgs(app.CommandLine, exe);
        var debugArgs = profile.Cdp.LaunchArgs.Select(a => a.Replace("${port}", port.Value.ToString()));
        var finalArgs = string.Join(' ', originalArgs.Concat(debugArgs));

        try
        {
            // Close the existing process gracefully.
            using var existing = Process.GetProcessById(app.ProcessId);
            try
            {
                if (!existing.CloseMainWindow()) throw new InvalidOperationException("main-window-not-found");
                if (!existing.WaitForExit(5000)) throw new TimeoutException("timeout");
            }
            catch (Exception ex)
            {
                var reason = ex is TimeoutException ? "timeout" : ex.Message == "main-window-not-found" ? "main-window-not-found" : "unknown";
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
            var argsVerified = newProc is not null && WaitForDebugArgs(newProc.Id, port.Value, TimeSpan.FromSeconds(2));
            _logger.Log(LogLevel.Information, LogCategories.Relaunch, argsVerified ? "args-verified" : "args-ignored", ("app", profile.AppId), ("port", port.Value));
            _logger.Log(LogLevel.Information, LogCategories.Relaunch, "relaunched", ("app", profile.AppId), ("port", port.Value));
            // NOTE: the orchestrator verifies CDP comes up on 127.0.0.1 with a BOUNDED
            // number of retries. If it does not (e.g. Electron single-instance rejected
            // the second instance), the orchestrator reports Experimental/Unsupported.
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
        var tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !t.StartsWith("--remote-debugging", StringComparison.OrdinalIgnoreCase));
        return tokens;
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
