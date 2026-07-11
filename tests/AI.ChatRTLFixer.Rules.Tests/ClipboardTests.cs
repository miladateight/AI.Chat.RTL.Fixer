using AI.ChatRTLFixer.Core;

namespace AI.ChatRTLFixer.Rules.Tests;

/// <summary>
/// Clipboard behavior: code/path/url/command/config must never receive bidi
/// markers. RTL-readable mode may add markers only around natural-language RTL.
/// </summary>
public class ClipboardTests
{
    private readonly ReferenceEvaluator _eval = new();
    private const char Rlm = '\u200F';
    private const char Lrm = '\u200E';

    [Fact]
    public void OriginalMode_PassesTextThrough()
    {
        var s = _eval.BuildPlainText("سلام دنیا", "Original");
        Assert.Equal("سلام دنیا", s);
    }

    [Fact]
    public void RtlReadable_AddsRlmAroundRtlText()
    {
        var s = _eval.BuildPlainText("سلام", "RtlReadable");
        // RLM (U+200F) should wrap RTL text.
        Assert.StartsWith(Rlm.ToString(), s);
        Assert.EndsWith(Rlm.ToString(), s);
    }

    [Fact]
    public void RtlReadableNoMarkers_KeepsTextClean()
    {
        var s = _eval.BuildPlainText("سلام", "RtlReadableNoMarkers");
        Assert.Equal("سلام", s);
        Assert.False(s.Contains(Rlm), "RLM marker should not be present");
    }

    [Fact]
    public void CodeBlock_PlainText_HasNoBidiMarkers()
    {
        var code = "```csharp\nConsole.WriteLine(\"hi\");\n```";
        var s = _eval.BuildPlainText(code, "RtlReadable");
        // Markers are only added at start/end if the first/last char is RTL.
        // A code block starts with ` and ends with `, so no markers.
        Assert.False(s.Contains(Rlm), "RLM marker should not be present");
        Assert.False(s.Contains(Lrm), "LRM marker should not be present");
    }

    [Fact]
    public void Path_PlainText_HasNoBidiMarkers()
    {
        var s = _eval.BuildPlainText("C:\\Users\\Milad\\Project", "RtlReadable");
        Assert.False(s.Contains(Rlm), "RLM marker should not be present");
    }

    [Fact]
    public void Url_PlainText_HasNoBidiMarkers()
    {
        var s = _eval.BuildPlainText("https://example.com/path", "RtlReadable");
        Assert.False(s.Contains(Rlm), "RLM marker should not be present");
    }

    [Fact]
    public void Html_Rtl_UsesIsolateSpan()
    {
        var html = _eval.BuildHtml("سلام", "RtlReadable", "rtl");
        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("unicode-bidi: isolate", html);
    }

    [Fact]
    public void Html_EscapesSpecialChars()
    {
        var html = _eval.BuildHtml("a < b & c", "RtlReadable", "ltr");
        Assert.Contains("&lt;", html);
        Assert.Contains("&amp;", html);
    }
}
