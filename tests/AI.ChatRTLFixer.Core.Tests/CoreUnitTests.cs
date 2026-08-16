using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Core.Settings;
using AI.ChatRTLFixer.Diagnostics;
using AI.ChatRTLFixer.Profiles;
using AI.ChatRTLFixer.Win32;

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
        Assert.False(settings.EnableBrowserTargets);
    }

    [Fact]
    public void Settings_Normalize_RepairsNullCollectionsAndInvalidRanges()
    {
        var settings = new AppSettings
        {
            SchemaVersion = 1,
            Apps = null!,
            LastKnownAppVersions = null!,
            PortRange = null!,
            DiscoveryTimeoutSeconds = -1,
            ReconciliationIntervalSeconds = 999,
            RelaunchCooldownSeconds = 0,
            UiCulture = null!,
        };

        settings.Normalize();

        Assert.NotNull(settings.Apps);
        Assert.NotNull(settings.LastKnownAppVersions);
        Assert.Equal(49152, settings.PortRange.Min);
        Assert.Equal(65535, settings.PortRange.Max);
        Assert.Equal(2, settings.DiscoveryTimeoutSeconds);
        Assert.Equal(60, settings.ReconciliationIntervalSeconds);
        Assert.Equal(10, settings.RelaunchCooldownSeconds);
        Assert.Equal("en", settings.UiCulture);
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public void LaunchArgumentSanitizer_RemovesEqualsAndSeparateDebugValues()
    {
        var sanitized = LaunchArgumentSanitizer.RemoveRemoteDebuggingArguments(
        [
            "--profile", "work",
            "--remote-debugging-port", "9222",
            "--remote-debugging-address=0.0.0.0",
            "--remote-debugging-pipe",
            "--keep",
        ]);

        Assert.Equal(["--profile", "work", "--keep"], sanitized);
    }

    [Fact]
    public void MacRelaunchArguments_PreserveQuotedValuesAndRemoveOldDebugFlags()
    {
        const string executable = "/Applications/AI Chat.app/Contents/MacOS/AI Chat";
        var arguments = AI.ChatRTLFixer.Mac.RelaunchService.ParseArgs(
            executable + " --profile \"Work Account\" --remote-debugging-port 9222 --safe", executable);

        Assert.Equal(["--profile", "Work Account", "--safe"], arguments);
    }

    [Fact]
    public void MacStartupPlist_EscapesExecutablePathAsXml()
    {
        var plist = AI.ChatRTLFixer.Mac.StartupManager.BuildPlist("/Applications/AI & Chat <Beta>.app/Contents/MacOS/App");

        Assert.Contains("AI &amp; Chat &lt;Beta&gt;.app", plist);
        Assert.DoesNotContain("AI & Chat <Beta>.app", plist);
    }

    [Theory]
    [InlineData("chrome.exe")]
    [InlineData("firefox")]
    [InlineData("C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe")]
    public void BrowserGuard_RecognizesConsumerBrowsers(string value)
    {
        Assert.True(BrowserGuard.IsBrowser(value, null));
    }

    [Fact]
    public void BuiltinProfiles_MatchesTraycerDesktop()
    {
        var registry = new ProfileRegistry();
        Assert.True(registry.TryMatchProcess("Traycer", out var profile));
        Assert.Equal("traycer", profile.AppId);
    }

    [Fact]
    public void AppProfile_SupportsRuntimeInjection_OnlyForUsableElectronProfiles()
    {
        var usable = new AppProfile
        {
            Status = SupportStatus.Experimental,
            UiTechnology = UiTechnology.Electron,
            Cdp = new CdpStrategy(),
        };
        var planned = new AppProfile
        {
            Status = SupportStatus.Planned,
            UiTechnology = UiTechnology.Electron,
            Cdp = new CdpStrategy(),
        };
        var native = new AppProfile
        {
            Status = SupportStatus.Experimental,
            UiTechnology = UiTechnology.Native,
            Cdp = new CdpStrategy(),
        };

        Assert.True(usable.SupportsRuntimeInjection);
        Assert.False(planned.SupportsRuntimeInjection);
        Assert.False(native.SupportsRuntimeInjection);
    }

    [Fact]
    public void ProductVersion_IsCurrentRelease()
    {
        Assert.Equal("1.0.3", Constants.AppVersion);
    }

    [Fact]
    public void ProfileRegistry_MatchesByWindowTitle_WhenTitleIsExactlyTheAppName()
    {
        var profile = new AppProfile
        {
            AppId = "chatgpt-desktop",
            DisplayName = "ChatGPT Desktop",
            ProcessNames = ["ChatGPT"],
            WindowTitlePatterns = ["ChatGPT"],
        };
        var registry = new ProfileRegistry([profile]);

        Assert.True(registry.TryMatchProcess("unknownexe", null, null, null, ["ChatGPT"], null, out var matched, out var reason));
        Assert.Equal("chatgpt-desktop", matched.AppId);
        Assert.Equal("window-title", reason);
    }

    [Theory]
    [InlineData("ChatGPT Screenshot 2024-01-01.png")]
    [InlineData("Vacation Photo - ChatGPT idea.jpg")]
    [InlineData("ChatGPTNotes.txt")]
    [InlineData("ChatGPTHelperUtility")]
    public void ProfileRegistry_DoesNotMatchWindowTitle_ForUnrelatedFileViewer(string title)
    {
        // Regression: opening a document/image whose file name merely contains the
        // app's name (e.g. "ChatGPT Screenshot.png" in Photos) must never be
        // mistaken for the real ChatGPT desktop app and trigger a relaunch.
        var profile = new AppProfile
        {
            AppId = "chatgpt-desktop",
            DisplayName = "ChatGPT Desktop",
            ProcessNames = ["ChatGPT"],
            WindowTitlePatterns = ["ChatGPT"],
        };
        var registry = new ProfileRegistry([profile]);

        Assert.False(registry.TryMatchProcess("PhotosApp", null, null, null, [title], null, out _, out _));
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
