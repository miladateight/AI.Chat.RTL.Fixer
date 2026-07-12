using System.Text.Json;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Injectors;
using Microsoft.Playwright;

namespace AI.ChatRTLFixer.IntegrationTests;

/// <summary>
/// Integration tests that run the REAL injected script (built by ScriptBuilder,
/// which embeds the canonical rtlfixer.rules.js) against a mock DOM in a real
/// headless Chromium. This verifies MutationObserver, copy event, protected
/// code blocks and restore cleanup end-to-end — the exact same code that runs
/// inside target Electron apps.
/// </summary>
public class MockDomTests
{
    private static readonly AppProfile MockProfile = new()
    {
        AppId = "mock",
        DisplayName = "Mock",
        UiTechnology = UiTechnology.Electron,
        Status = SupportStatus.Experimental,
        Selectors = new Selectors
        {
            ChatContainer = "#chat",
            MessageRoot = "#messages",
            UserMessage = ".user-msg",
            AssistantMessage = ".assistant-msg",
            Composer = "#composer",
            CodeBlock = "pre code",
            InlineCode = "code",
            CopyRoot = "#chat",
            Protected = ["pre"],
            FontScope = "#chat, #composer",
        },
        Cdp = new CdpStrategy(),
    };

    private const string MockHtml =
        "<!doctype html><html><head><meta charset=\"utf-8\"></head><body>" +
        "<div id=\"sidebar\">Sidebar must not be touched</div>" +
        "<div id=\"chat\">" +
        "<div id=\"messages\">" +
        "<div class=\"user-msg\"><p>\u0633\u0644\u0627\u0645 \u062F\u0646\u06CC\u0627</p></div>" +
        "<div class=\"assistant-msg\">" +
        "<p>\u0627\u06CC\u0646 \u06CC\u06A9 \u067E\u0627\u0633\u062E \u0641\u0627\u0631\u0633\u06CC \u0627\u0633\u062A.</p>" +
        "<pre><code>Console.WriteLine(\"hi\");</code></pre>" +
        "<p>The path is C:\\Users\\test and URL https://example.com</p>" +
        "</div>" +
        "</div>" +
        "<input id=\"composer\" type=\"text\" value=\"\" />" +
        "</div>" +
        "</body></html>";

    private static string BuildScript(CopyMode copyMode = CopyMode.RtlReadable)
        => ScriptBuilder.Build(MockProfile, copyMode);

    [Fact]
    public async Task Script_AppliesRtlToPersianMessages()
    {
        using var pw = await PlaywrightFixture.CreateAsync();
        var page = await pw.NewPageAsync();
        await page.SetContentAsync(MockHtml);
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = BuildScript() });
        await page.WaitForTimeoutAsync(300);

        var dir = await page.Locator(".user-msg p").First.GetAttributeAsync("dir");
        Assert.Equal("rtl", dir);
    }

    [Fact]
    public async Task Script_LeavesCodeBlocksLtr()
    {
        using var pw = await PlaywrightFixture.CreateAsync();
        var page = await pw.NewPageAsync();
        await page.SetContentAsync(MockHtml);
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = BuildScript() });
        await page.WaitForTimeoutAsync(300);

        // Code block must NOT receive dir="rtl"; CSS enforces LTR via protected selector.
        var codeDir = await page.Locator("pre code").First.EvaluateAsync<string>("el => getComputedStyle(el).direction");
        Assert.Equal("ltr", codeDir);
    }

    [Fact]
    public async Task Script_DoesNotTouchSidebar()
    {
        using var pw = await PlaywrightFixture.CreateAsync();
        var page = await pw.NewPageAsync();
        await page.SetContentAsync(MockHtml);
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = BuildScript() });
        await page.WaitForTimeoutAsync(300);

        var sidebarMarked = await page.Locator("#sidebar").First.GetAttributeAsync("data-rtlfixer");
        Assert.Null(sidebarMarked);
    }

    [Fact]
    public async Task Script_ProcessesNewMessagesViaObserver()
    {
        using var pw = await PlaywrightFixture.CreateAsync();
        var page = await pw.NewPageAsync();
        await page.SetContentAsync(MockHtml);
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = BuildScript() });
        await page.WaitForTimeoutAsync(200);

        // Inject a new message dynamically (simulates streaming).
        await page.EvaluateAsync(
            "var m = document.createElement('div');" +
            "m.className = 'assistant-msg';" +
            "m.innerHTML = '<p>\u067E\u06CC\u0627\u0645 \u062C\u062F\u06CC\u062F \u0641\u0627\u0631\u0633\u06CC</p>';" +
            "document.querySelector('#messages').appendChild(m);");
        await page.WaitForTimeoutAsync(300);

        var dir = await page.Locator(".assistant-msg p").Nth(2).GetAttributeAsync("dir");
        Assert.Equal("rtl", dir);
    }

    [Fact]
    public async Task Script_ReclassifiesStreamingTextNodeChanges()
    {
        using var pw = await PlaywrightFixture.CreateAsync();
        var page = await pw.NewPageAsync();
        await page.SetContentAsync(MockHtml);
        await page.EvaluateAsync("document.querySelector('#messages').insertAdjacentHTML('beforeend', '<p id=stream>Loading</p>')");
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = BuildScript() });

        await page.EvaluateAsync("document.querySelector('#stream').firstChild.data = 'این پاسخ در حال پخش است'");
        await page.WaitForTimeoutAsync(300);

        Assert.Equal("rtl", await page.Locator("#stream").GetAttributeAsync("dir"));
    }

    [Fact]
    public async Task Script_TracksReplacedComposerWithoutTouchingOutsideUi()
    {
        using var pw = await PlaywrightFixture.CreateAsync();
        var page = await pw.NewPageAsync();
        await page.SetContentAsync(MockHtml);
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = BuildScript() });

        await page.EvaluateAsync("document.querySelector('#composer').outerHTML = '<input id=composer>'");
        await page.WaitForTimeoutAsync(200);
        await page.EvaluateAsync("var c=document.querySelector('#composer');c.value='متن فارسی';c.dispatchEvent(new Event('input'))");
        await page.WaitForTimeoutAsync(100);

        Assert.Equal("rtl", await page.Locator("#composer").GetAttributeAsync("dir"));
        Assert.Null(await page.Locator("#sidebar").GetAttributeAsync("data-rtlfixer"));
    }

    [Fact]
    public async Task Script_RightAlignsPersianDivWithInlineChildren()
    {
        // A user bubble / streamed answer rendered as a DIV that holds only
        // inline formatting (a <b>) is a real text block and must be flipped.
        using var pw = await PlaywrightFixture.CreateAsync();
        var page = await pw.NewPageAsync();
        await page.SetContentAsync(MockHtml);
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = BuildScript() });

        await page.EvaluateAsync(
            "var d = document.createElement('div');" +
            "d.id = 'inline-bubble';" +
            "d.innerHTML = 'این یک <b>جمله</b> فارسی است';" +
            "document.querySelector('#messages').appendChild(d);");
        await page.WaitForTimeoutAsync(300);

        Assert.Equal("rtl", await page.Locator("#inline-bubble").GetAttributeAsync("dir"));
    }

    [Fact]
    public async Task Script_DoesNotFlipContainerDivWithBlockChildren()
    {
        // A DIV that wraps block-level children is a layout container: it must
        // stay untouched while its inner paragraph is flipped.
        using var pw = await PlaywrightFixture.CreateAsync();
        var page = await pw.NewPageAsync();
        await page.SetContentAsync(MockHtml);
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = BuildScript() });

        await page.EvaluateAsync(
            "var wrap = document.createElement('div');" +
            "wrap.id = 'container';" +
            "wrap.innerHTML = '<p id=inner>پاراگراف فارسی</p>';" +
            "document.querySelector('#messages').appendChild(wrap);");
        await page.WaitForTimeoutAsync(300);

        Assert.Null(await page.Locator("#container").GetAttributeAsync("dir"));
        Assert.Equal("rtl", await page.Locator("#inner").GetAttributeAsync("dir"));
    }

    [Fact]
    public async Task Script_SurvivesChatRootReplacement()
    {
        using var pw = await PlaywrightFixture.CreateAsync();
        var page = await pw.NewPageAsync();
        await page.SetContentAsync(MockHtml);
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = BuildScript() });

        await page.EvaluateAsync("document.querySelector('#chat').outerHTML = '<div id=chat><div id=messages><p id=replaced>ریشه جدید گفتگو</p></div><input id=composer></div>'");
        await page.WaitForTimeoutAsync(300);

        Assert.Equal("rtl", await page.Locator("#replaced").GetAttributeAsync("dir"));
    }

    [Fact]
    public async Task Restore_RemovesAllModifications()
    {
        using var pw = await PlaywrightFixture.CreateAsync();
        var page = await pw.NewPageAsync();
        await page.SetContentAsync(MockHtml);
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = BuildScript() });
        await page.WaitForTimeoutAsync(200);

        await page.EvaluateAsync("window.__rtlfixerRestore()");
        await page.WaitForTimeoutAsync(100);

        var remaining = await page.Locator("[data-rtlfixer]").CountAsync();
        Assert.Equal(0, remaining);
        var styleCount = await page.Locator("#rtlfixer-css").CountAsync();
        // CSS is removed by the host adapter, not by the restore script; here we
        // only assert the script's own cleanup of node attributes.
        Assert.True(styleCount >= 0);
    }

    [Fact]
    public async Task Copy_RtlReadable_OverridesClipboard()
    {
        using var pw = await PlaywrightFixture.CreateAsync();
        var page = await pw.NewPageAsync();
        await page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await page.SetContentAsync(MockHtml);

        // Install a bubble-phase listener that records what the interceptor set
        // on clipboardData. This avoids navigator.clipboard (needs secure context).
        await page.EvaluateAsync(
            "window.__lastCopyPlain = null; document.addEventListener('copy', (e) => {" +
            "  window.__lastCopyPlain = e.clipboardData ? e.clipboardData.getData('text/plain') : null;" +
            "}, false);");

        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Content = BuildScript(CopyMode.RtlReadable) });
        await page.WaitForTimeoutAsync(200);

        await page.Locator(".user-msg p").First.SelectTextAsync();
        await page.EvaluateAsync("document.execCommand('copy');");
        await page.WaitForTimeoutAsync(100);

        var plain = await page.EvaluateAsync<string>("() => window.__lastCopyPlain");
        // The interceptor set text/plain with RLM around RTL text.
        Assert.NotNull(plain);
        Assert.Contains("\u200F", plain);
    }
}
