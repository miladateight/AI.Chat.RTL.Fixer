using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Mac;

namespace AI.ChatRTLFixer.Core.Tests;

/// <summary>
/// The macOS persistent-launch path hands a plist to launchd. A malformed one
/// is not rejected loudly — launchd simply ignores the agent — so the XML shape
/// and its escaping are worth pinning down. These cover the pure string logic
/// and run on any OS; loading the agent itself needs a real Mac.
/// </summary>
public class LaunchAgentLaunchServiceTests
{
    private const string Exe = "/Applications/Claude.app/Contents/MacOS/Claude";

    [Fact]
    public void BuildPlist_PutsTheExecutableFirstThenTheFlags()
    {
        var xml = LaunchAgentLaunchService.BuildPlist(
            "com.aichatrtlfixer.persist.claude", Exe, PersistentLaunchFlags.BuildDebugArguments(51000));

        var args = LaunchAgentLaunchService.ParseProgramArguments(xml);
        Assert.Equal(Exe, args[0]);
        Assert.Contains("--remote-debugging-port=51000", args);
        Assert.Contains("--remote-debugging-address=127.0.0.1", args);
    }

    [Fact]
    public void BuildPlist_RunsAtLogin()
    {
        var xml = LaunchAgentLaunchService.BuildPlist("label", Exe, PersistentLaunchFlags.BuildDebugArguments(51000));
        Assert.Contains("<key>RunAtLoad</key>", xml, StringComparison.Ordinal);
        Assert.Contains("<true/>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlist_EscapesAPathThatWouldOtherwiseCloseATag()
    {
        const string nasty = "/Applications/We<b>ird & \"Quoted\".app/Contents/MacOS/App";
        var xml = LaunchAgentLaunchService.BuildPlist("label", nasty, []);

        // The raw characters must not survive into the document...
        Assert.DoesNotContain("We<b>ird", xml, StringComparison.Ordinal);
        // ...and the value must still round-trip back to exactly the input.
        Assert.Equal(nasty, LaunchAgentLaunchService.ParseProgramArguments(xml)[0]);
    }

    [Fact]
    public void BuildPlist_EscapesTheLabelToo()
    {
        var xml = LaunchAgentLaunchService.BuildPlist("bad<label>&", Exe, []);
        Assert.DoesNotContain("bad<label>", xml, StringComparison.Ordinal);
        Assert.Contains("&lt;label&gt;", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseProgramArguments_OnPlistWithoutTheKey_ReturnsEmpty()
    {
        Assert.Empty(LaunchAgentLaunchService.ParseProgramArguments("<plist><dict></dict></plist>"));
    }

    [Fact]
    public void LabelFor_IsStableAndNamespaced()
    {
        var label = LaunchAgentLaunchService.LabelFor(Exe);
        Assert.StartsWith("com.aichatrtlfixer.persist.", label, StringComparison.Ordinal);
        Assert.Equal(label, LaunchAgentLaunchService.LabelFor(Exe));
    }

    [Fact]
    public void LabelFor_DiffersBetweenApps()
    {
        Assert.NotEqual(
            LaunchAgentLaunchService.LabelFor("/Applications/Claude.app/Contents/MacOS/Claude"),
            LaunchAgentLaunchService.LabelFor("/Applications/ChatGPT.app/Contents/MacOS/ChatGPT"));
    }

    [Fact]
    public void LabelFor_StripsCharactersLaunchdWouldNotAccept()
    {
        var label = LaunchAgentLaunchService.LabelFor("/Applications/My App (beta)!.app/Contents/MacOS/My App (beta)!");
        var suffix = label["com.aichatrtlfixer.persist.".Length..];
        Assert.Matches("^[a-z0-9-]+$", suffix);
    }
}
