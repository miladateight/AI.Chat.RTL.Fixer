using System.Net.Http.Headers;
using System.Text.Json;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Diagnostics;

namespace AI.ChatRTLFixer.Mac;

/// <summary>
/// Checks the public GitHub Releases endpoint for a newer application version.
/// Identical to the Windows implementation — pure HTTP, no OS-specific API
/// except opening the release page via the platform's default URL handler.
/// </summary>
public sealed class UpdateChecker
{
    private static readonly HttpClient Client = CreateClient();
    private readonly SafeLogger _logger;

    public UpdateChecker(SafeLogger logger) => _logger = logger;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var response = await Client.GetAsync(Constants.GitHubLatestReleaseApi, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Log(LogLevel.Warning, LogCategories.Update, "check-failed",
                    ("status", ((int)response.StatusCode).ToString()));
                return UpdateCheckResult.Failed("Could not check for updates right now.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var release = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            var root = release.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean() ||
                root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
                return UpdateCheckResult.Failed("The latest published release is not a stable update.");

            if (!root.TryGetProperty("tag_name", out var tagProperty) ||
                !TryParseVersion(tagProperty.GetString(), out var latest) ||
                !root.TryGetProperty("html_url", out var releaseProperty) ||
                !TryGetGitHubReleasePage(releaseProperty.GetString(), out var releasePage))
            {
                _logger.Log(LogLevel.Warning, LogCategories.Update, "invalid-release-response");
                return UpdateCheckResult.Failed("The update service returned an invalid release record.");
            }

            if (!Version.TryParse(Constants.AppVersion, out var current))
                return UpdateCheckResult.Failed("The installed application version is invalid.");

            _logger.Log(LogLevel.Information, LogCategories.Update, "check-complete",
                ("current", current.ToString()), ("latest", latest.ToString()));
            return latest.CompareTo(current) > 0
                ? UpdateCheckResult.Available(latest, releasePage)
                : UpdateCheckResult.Current(current);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return UpdateCheckResult.Failed("The update check was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warning, LogCategories.Update, "check-failed",
                ("msg", SafeLogger.Redact(ex.Message)));
            return UpdateCheckResult.Failed("Could not check for updates right now.");
        }
    }

    public static void OpenReleasePage(Uri releasePage)
    {
        if (!IsOfficialGitHubPage(releasePage)) return;
        // `open` is macOS's equivalent of ShellExecute for a URL.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open", releasePage.AbsoluteUri)
        {
            UseShellExecute = false,
        });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AIChatRTLFixer", Constants.AppVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static bool TryParseVersion(string? tag, out Version version)
    {
        var value = tag?.Trim().TrimStart('v', 'V');
        if (Version.TryParse(value, out var parsed))
        {
            version = parsed;
            return true;
        }

        version = null!;
        return false;
    }

    private static bool TryGetGitHubReleasePage(string? value, out Uri releasePage)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && IsOfficialGitHubPage(parsed))
        {
            releasePage = parsed;
            return true;
        }

        releasePage = null!;
        return false;
    }

    private static bool IsOfficialGitHubPage(Uri page) =>
        page.Scheme == Uri.UriSchemeHttps &&
        string.Equals(page.Host, "github.com", StringComparison.OrdinalIgnoreCase);
}

public sealed record UpdateCheckResult(bool Succeeded, bool IsUpdateAvailable, Version? LatestVersion, Uri? ReleasePage, string Message)
{
    public static UpdateCheckResult Available(Version latestVersion, Uri releasePage) =>
        new(true, true, latestVersion, releasePage, $"Version {latestVersion} is available.");

    public static UpdateCheckResult Current(Version currentVersion) =>
        new(true, false, currentVersion, null, $"You're up to date (version {currentVersion}).");

    public static UpdateCheckResult Failed(string message) => new(false, false, null, null, message);
}
