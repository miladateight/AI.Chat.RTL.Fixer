using AI.ChatRTLFixer.Clipboard;
using AI.ChatRTLFixer.Core;

namespace AI.ChatRTLFixer.IntegrationTests;

/// <summary>
/// Tests the C# clipboard payload helper. This mirrors the plain-text marker
/// logic used by the JS rule engine and serves as a parity check + fallback
/// path coverage (see ClipboardPayloadBuilder).
/// </summary>
public class ClipboardPayloadBuilderTests
{
    private const char Rlm = '\u200F';

    [Fact]
    public void BuildPlain_Original_PassesThrough()
    {
        var s = ClipboardPayloadBuilder.BuildPlain("سلام دنیا", CopyMode.Original);
        Assert.Equal("سلام دنیا", s);
    }

    [Fact]
    public void BuildPlain_RtlReadable_AddsRlmAroundRtl()
    {
        var s = ClipboardPayloadBuilder.BuildPlain("سلام", CopyMode.RtlReadable);
        Assert.StartsWith(Rlm.ToString(), s);
        Assert.EndsWith(Rlm.ToString(), s);
    }

    [Fact]
    public void BuildPlain_NoMarkers_KeepsTextClean()
    {
        var s = ClipboardPayloadBuilder.BuildPlain("سلام", CopyMode.RtlReadableNoMarkers);
        Assert.Equal("سلام", s);
        Assert.DoesNotContain(Rlm, s);
    }

    [Fact]
    public void BuildHtml_Rtl_UsesIsolateSpan()
    {
        var html = ClipboardPayloadBuilder.BuildHtml("سلام", CopyMode.RtlReadable, rtl: true);
        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("unicode-bidi: isolate", html);
    }

    [Fact]
    public void BuildHtml_EscapesSpecialChars()
    {
        var html = ClipboardPayloadBuilder.BuildHtml("a < b & c", CopyMode.RtlReadable, rtl: false);
        Assert.Contains("&lt;", html);
        Assert.Contains("&amp;", html);
    }
}