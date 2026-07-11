using AI.ChatRTLFixer.Core;

namespace AI.ChatRTLFixer.Rules.Tests;

/// <summary>
/// Technical text must ALWAYS be classified Protected + LTR, regardless of any
/// RTL characters that might appear nearby. This is the core safety guarantee.
/// </summary>
public class ProtectedTextTests
{
    private readonly ReferenceEvaluator _eval = new();

    public static IEnumerable<object[]> ProtectedBlocks => new[]
    {
        new object[] { "code-block-fenced", "```csharp\nConsole.WriteLine(\"hi\");\n```" },
        new object[] { "code-block-fenced-fa-comment", "```js\n// کامنت فارسی\nconsole.log(1)\n```" },
        new object[] { "json", "{\n  \"name\": \"app\",\n  \"version\": \"1.0.0\"\n}" },
        new object[] { "yaml", "name: app\nversion: 1.0.0\nservices:\n  web:\n    image: nginx" },
        new object[] { "xml", "<?xml version=\"1.0\"?>\n<root><item>1</item></root>" },
        new object[] { "toml", "[package]\nname = \"app\"\nversion = \"1.0.0\"" },
        new object[] { "ini", "[core]\nname = app\nversion = 1.0.0" },
        new object[] { "env", "API_KEY=secret\nNODE_ENV=production\nPORT=3000" },
        new object[] { "stack-trace", "Error: boom\n    at foo (app.js:10:5)\n    at bar (app.js:20:3)" },
        new object[] { "diff", "@@ -1,2 +1,2 @@\n-old line\n+new line\n context" },
        new object[] { "log", "2026-07-08 10:00:00 INFO request completed\n2026-07-08 10:01:00 WARN slow query" },
    };

    [Theory]
    [MemberData(nameof(ProtectedBlocks))]
    public void TechnicalBlock_IsProtectedAndLtr(string _1, string text)
    {
        _ = _1;
        var c = _eval.Classify(text);
        Assert.True(c.Protected, $"{_1} should be protected");
        Assert.Equal(BlockDirection.Ltr, c.Direction);
        Assert.Equal("left", c.Align);
    }

    [Fact]
    public void CodeBlock_NeverBecomesRtl_EvenWithPersianComment()
    {
        var c = _eval.Classify("```python\n# این کامنت فارسی است اما بلاک کد است\nprint(1)\n```");
        Assert.True(c.Protected);
        Assert.Equal(BlockDirection.Ltr, c.Direction);
    }

    [Fact]
    public void NodeInsideProtectedSelector_IsAlwaysProtected()
    {
        var c = _eval.ClassifyNode("سلام دنیا", insideProtected: true);
        Assert.True(c.Protected);
        Assert.Equal(BlockDirection.Ltr, c.Direction);
    }

    [Fact]
    public void WindowsPath_Alone_IsNotTechnicalBlock_ButTokenDetected()
    {
        // A lone path is not a whole technical block, but the token is detected.
        var c = _eval.Classify("C:\\Users\\Milad\\Project");
        Assert.Contains("winPath", c.Tokens);
    }

    [Fact]
    public void Url_Alone_TokenDetected()
    {
        var c = _eval.Classify("https://example.com/path");
        Assert.Contains("url", c.Tokens);
    }

    [Fact]
    public void VersionNumber_TokenDetected()
    {
        var c = _eval.Classify("upgraded to v2.4.1 today");
        Assert.Contains("versionNumber", c.Tokens);
    }
}
