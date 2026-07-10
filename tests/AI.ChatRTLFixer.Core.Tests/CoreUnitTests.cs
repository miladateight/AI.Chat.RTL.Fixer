using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Profiles;
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
    public void PortPicker_ReturnsFreePort_InRange()
    {
        var picker = new PortPicker();
        var port = picker.PickFreePort(49152, 65535);
        Assert.NotNull(port);
        Assert.InRange(port!.Value, 49152, 65535);
    }

    [Fact]
    public void PortPicker_InvalidRange_ReturnsNull()
    {
        var picker = new PortPicker();
        Assert.Null(picker.PickFreePort(70000, 80000));
        Assert.Null(picker.PickFreePort(100, 50));
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