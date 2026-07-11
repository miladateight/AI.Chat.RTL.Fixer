using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Diagnostics;
using AI.ChatRTLFixer.Profiles;

namespace AI.ChatRTLFixer.Core.Tests;

public class CoreUnitTests
{
    [Fact]
    public void SafeLogger_Redact_RemovesAllContent()
    {
        var redacted = SafeLogger.Redact("سلام دنیا this is private chat content");
        Assert.DoesNotContain("سلام", redacted);
        Assert.Contains("len=", redacted);
        Assert.Contains("rtlRatio=", redacted);
    }

    [Fact]
    public void SafeLogger_Redact_EmptyString()
    {
        var redacted = SafeLogger.Redact("");
        Assert.Equal("len=0", redacted);
    }

    [Fact]
    public void ProfileRegistry_MatchesKnownProcess()
    {
        var reg = new ProfileRegistry();
        Assert.True(reg.TryMatchProcess("Claude", out var p));
        Assert.Equal("claude-desktop", p.AppId);
    }

    [Fact]
    public void ProfileRegistry_UnknownProcess_DoesNotMatch()
    {
        var reg = new ProfileRegistry();
        Assert.False(reg.TryMatchProcess("notepad", out _));
    }

    [Fact]
    public void ProfileRegistry_MatchesByExecutablePath_WhenProcessNameDiffers()
    {
        var profile = new AppProfile
        {
            AppId = "path-profile",
            DisplayName = "Path Profile",
            ProcessNames = ["ExpectedName"],
            ExecutablePathPatterns = ["Claude\\Claude.exe"],
        };
        var registry = new ProfileRegistry([profile]);

        Assert.True(registry.TryMatchProcess("unexpected", "C:\\Users\\x\\Claude\\Claude.exe", null, null, null, null, out var matched, out var reason));
        Assert.Equal("path-profile", matched.AppId);
        Assert.Equal("executable-path", reason);
    }

    [Fact]
    public void ProfileRegistry_MatchesByProductName_WhenMarketingNameDiffersFromExe()
    {
        var profile = new AppProfile
        {
            AppId = "codex",
            DisplayName = "Codex",
            ProcessNames = ["ExpectedName"],
            ProductNamePatterns = ["OpenAI Codex"],
        };
        var registry = new ProfileRegistry([profile]);

        Assert.True(registry.TryMatchProcess("desktop", null, "OpenAI Codex Desktop", null, null, null, out var matched, out var reason));
        Assert.Equal("codex", matched.AppId);
        Assert.Equal("version-info", reason);
    }

    [Fact]
    public void Settings_Defaults_EnableBoundedRuntimeControls()
    {
        var settings = new AI.ChatRTLFixer.Core.Settings.AppSettings();
        Assert.Equal(15, settings.ReconciliationIntervalSeconds);
        Assert.InRange(settings.ReconciliationIntervalSeconds, 5, 60);
        Assert.True(settings.DiscoveryTimeoutSeconds >= 2);
    }

    [Fact]
    public void BuiltinProfiles_NoStableWithoutVerifiedVersion()
    {
        // v0.1 rule: a profile is only Stable after a real verified version.
        // None of the built-in profiles ship Stable in v0.1 (per the plan).
        foreach (var p in BuiltinProfiles.All)
        {
            if (p.Status == SupportStatus.Stable)
            {
                Assert.False(string.IsNullOrEmpty(p.TestedAppVersion),
                    $"{p.AppId} is Stable but has no TestedAppVersion");
            }
        }
    }
}
