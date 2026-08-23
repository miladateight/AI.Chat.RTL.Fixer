using AI.ChatRTLFixer.Core;

namespace AI.ChatRTLFixer.Core.Tests;

/// <summary>
/// The persistent-launch path writes a port into a STATIC Windows shortcut, so
/// two properties matter more than anything else: the port must be identical on
/// every run, and applying the flags twice must not corrupt the shortcut.
/// </summary>
public class PersistentLaunchFlagsTests
{
    private const int Min = 49152;
    private const int Max = 65535;

    [Fact]
    public void DeriveStablePort_IsStableAcrossCalls()
    {
        var first = PersistentLaunchFlags.DeriveStablePort("claude-desktop", Min, Max);
        var second = PersistentLaunchFlags.DeriveStablePort("claude-desktop", Min, Max);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("claude-desktop")]
    [InlineData("chatgpt-desktop")]
    [InlineData("codex-desktop")]
    [InlineData("zcode")]
    public void DeriveStablePort_StaysInsideRange(string appId)
    {
        var port = PersistentLaunchFlags.DeriveStablePort(appId, Min, Max);
        Assert.InRange(port, Min, Max);
    }

    [Fact]
    public void DeriveStablePort_DiffersBetweenApps()
    {
        var a = PersistentLaunchFlags.DeriveStablePort("claude-desktop", Min, Max);
        var b = PersistentLaunchFlags.DeriveStablePort("chatgpt-desktop", Min, Max);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DeriveStablePort_IsCaseInsensitive()
    {
        Assert.Equal(
            PersistentLaunchFlags.DeriveStablePort("ZCode", Min, Max),
            PersistentLaunchFlags.DeriveStablePort("zcode", Min, Max));
    }

    [Fact]
    public void BuildDebugArguments_BindsLoopbackOnly()
    {
        var args = PersistentLaunchFlags.BuildDebugArguments(51000);
        Assert.Contains("--remote-debugging-port=51000", args);
        Assert.Contains("--remote-debugging-address=127.0.0.1", args);
        Assert.DoesNotContain(args, a => a.Contains("0.0.0.0", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyDebugArguments_KeepsOriginalArguments()
    {
        var result = PersistentLaunchFlags.ApplyDebugArguments("--profile-directory=Default --no-sandbox", 51000);
        Assert.StartsWith("--profile-directory=Default --no-sandbox", result, StringComparison.Ordinal);
        Assert.Contains("--remote-debugging-port=51000", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyDebugArguments_IsIdempotent()
    {
        var once = PersistentLaunchFlags.ApplyDebugArguments("--no-sandbox", 51000);
        var twice = PersistentLaunchFlags.ApplyDebugArguments(once, 51000);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void ApplyDebugArguments_ReplacesAnEarlierPort()
    {
        var old = PersistentLaunchFlags.ApplyDebugArguments("--no-sandbox", 51000);
        var updated = PersistentLaunchFlags.ApplyDebugArguments(old, 52000);
        Assert.Contains("--remote-debugging-port=52000", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("51000", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveDebugArguments_RestoresTheOriginalShortcut()
    {
        const string original = "--profile-directory=Default --no-sandbox";
        var withFlags = PersistentLaunchFlags.ApplyDebugArguments(original, 51000);
        Assert.Equal(original, PersistentLaunchFlags.RemoveDebugArguments(withFlags));
    }

    [Fact]
    public void RemoveDebugArguments_OnCleanArguments_ChangesNothing()
    {
        const string original = "--no-sandbox";
        Assert.Equal(original, PersistentLaunchFlags.RemoveDebugArguments(original));
    }

    [Fact]
    public void RemoveDebugArguments_HandlesSpaceSeparatedForm()
    {
        var cleaned = PersistentLaunchFlags.RemoveDebugArguments("--remote-debugging-port 51000 --no-sandbox");
        Assert.Equal("--no-sandbox", cleaned);
    }

    [Fact]
    public void Tokenize_KeepsQuotedPathsIntact()
    {
        var tokens = PersistentLaunchFlags.Tokenize("--user-data-dir=\"C:\\My Apps\\data\" --no-sandbox");
        Assert.Equal(2, tokens.Count);
        Assert.Equal("--user-data-dir=\"C:\\My Apps\\data\"", tokens[0]);
    }

    [Fact]
    public void Tokenize_OnEmptyInput_ReturnsEmpty()
    {
        Assert.Empty(PersistentLaunchFlags.Tokenize(null));
        Assert.Empty(PersistentLaunchFlags.Tokenize("   "));
    }

    [Fact]
    public void HasRemoteDebuggingArguments_DetectsBothForms()
    {
        Assert.True(PersistentLaunchFlags.HasRemoteDebuggingArguments(["--remote-debugging-port=9222"]));
        Assert.True(PersistentLaunchFlags.HasRemoteDebuggingArguments(["--remote-debugging-port", "9222"]));
        Assert.False(PersistentLaunchFlags.HasRemoteDebuggingArguments(["--no-sandbox"]));
    }

    [Fact]
    public void BuildDebugArguments_RejectsAnInvalidPort()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PersistentLaunchFlags.BuildDebugArguments(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PersistentLaunchFlags.BuildDebugArguments(70000));
    }

    [Fact]
    public void DeriveStablePort_RejectsAnEmptyAppId()
    {
        Assert.Throws<ArgumentException>(() => PersistentLaunchFlags.DeriveStablePort(" ", Min, Max));
    }
}
