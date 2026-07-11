using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Profiles;
using AI.ChatRTLFixer.Fonts;
using AI.ChatRTLFixer.Injectors;

namespace AI.ChatRTLFixer.IntegrationTests;

/// <summary>
/// Verifies the injection payload is built correctly WITHOUT requiring a real
/// browser. These are fast unit-style tests that live next to the integration
/// project (which already references ScriptBuilder/FontPack) so the font and
/// setConfig fixes can be asserted even when Playwright Chromium is unavailable.
/// </summary>
public class InjectionPayloadTests
{
    private static readonly AppProfile Profile = new()
    {
        AppId = "test",
        DisplayName = "Test",
        UiTechnology = UiTechnology.Electron,
        Status = SupportStatus.Experimental,
        Selectors = new Selectors
        {
            ChatContainer = "#chat",
            Composer = "#composer",
            FontScope = "#chat, #composer",
            CodeBlock = "pre code",
            InlineCode = "code",
            Protected = ["pre"],
        },
        Cdp = new CdpStrategy(),
    };

    [Fact]
    public void FontPack_BuildFontStyle_AppliesFamilyToScope()
    {
        var family = FontPack.FontFamilyCss(FontChoice.Vazirmatn);
        var css = FontPack.BuildFontStyle(family, Profile.Selectors.FontScope, base64: "AAAA");

        // The scoped font-family rule must be present, otherwise the @font-face
        // is registered but never applied to any element (the original bug).
        Assert.Contains("#chat, #composer {", css);
        Assert.Contains("font-family: Vazirmatn", css);
        // @font-face should be emitted for Vazirmatn when base64 is provided.
        Assert.Contains("@font-face {", css);
        Assert.Contains("base64,AAAA", css);
    }

    [Fact]
    public void FontPack_BuildFontStyle_OmitsFontFaceWhenNoBase64()
    {
        var family = FontPack.FontFamilyCss(FontChoice.Vazirmatn);
        var css = FontPack.BuildFontStyle(family, Profile.Selectors.FontScope, base64: null);

        Assert.DoesNotContain("@font-face", css);
        // Scoped rule still applied.
        Assert.Contains("#chat, #composer {", css);
    }

    [Fact]
    public void FontPack_BuildFontStyle_NoScope_OmitsScopedRule()
    {
        var family = FontPack.FontFamilyCss(FontChoice.Vazirmatn);
        var css = FontPack.BuildFontStyle(family, fontScopeSelector: "", base64: "AAAA");

        // @font-face present but no scoped application rule.
        Assert.Contains("@font-face {", css);
        Assert.DoesNotContain("font-family: Vazirmatn", css);
    }

    [Fact]
    public void ScriptBuilder_EmitsSetConfig_FromSharedJson()
    {
        var script = ScriptBuilder.Build(Profile, CopyMode.RtlReadable);

        // The runtime bootstrap must feed the shared JSON config into the engine
        // so thresholds/ranges come from rule-engine.shared.json, not the
        // hard-coded fallback inside the JS file (single source of truth).
        Assert.Contains("Rules.setConfig(", script);
        // The shared config includes the rtlRatio threshold (0.30).
        Assert.Contains("rtlRatio", script);
    }

    [Fact]
    public void ScriptBuilder_EmbedsSelectorsAndCopyMode()
    {
        var script = ScriptBuilder.Build(Profile, CopyMode.RtlReadableNoMarkers);

        Assert.Contains("#chat", script);
        Assert.Contains("#composer", script);
        Assert.Contains("RtlReadableNoMarkers", script);
    }
}
