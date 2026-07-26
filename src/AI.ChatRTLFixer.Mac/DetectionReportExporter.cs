using System.Text.Json;
using AI.ChatRTLFixer.Core;

namespace AI.ChatRTLFixer.Mac;

/// <summary>Exports detection metadata only. It intentionally contains no DOM, chat or clipboard content.</summary>
public static class DetectionReportExporter
{
    public static async Task<string> ExportAsync(Orchestrator orchestrator, bool includePaths, CancellationToken ct)
    {
        var report = new
        {
            timestampUtc = DateTime.UtcNow,
            includePaths,
            candidates = orchestrator.RuntimeStatuses.Select(status => new
            {
                appId = status.App.AppId,
                processName = status.App.ProcessName,
                pid = status.App.ProcessId,
                executablePath = includePaths ? status.App.ExecutablePath : null,
                windowTitleCount = status.App.WindowTitles.Length,
                commandLine = RedactCommandLine(status.App.CommandLine),
                matchedProfile = status.App.AppId,
                matchReason = status.App.MatchReason,
                debugPort = status.App.DebugPort,
                cdpReachable = status.State is AppRuntimeState.Attached or AppRuntimeState.InjectionSucceeded,
                runtimeState = status.State.ToString(),
                supportStatus = orchestrator.Profiles.FirstOrDefault(p => p.AppId == status.App.AppId)?.Status.ToString() ?? "Unknown",
                nextAction = NextAction(status.State),
                detail = status.Detail,
            }),
        };
        AppPaths.EnsureDirectories();
        var path = Path.Combine(AppPaths.AppDataRoot, $"detection-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
        return path;
    }

    private static string? RedactCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.StartsWith("--remote-debugging-", StringComparison.OrdinalIgnoreCase));
        return string.Join(' ', args);
    }

    private static string NextAction(AppRuntimeState state) => state switch
    {
        AppRuntimeState.CdpUnsupported => "inspect-existing-local-endpoint",
        AppRuntimeState.Unsupported => "no-runtime-injection",
        AppRuntimeState.DisabledByUser => "enable-global-or-app-setting",
        _ => "none",
    };
}
