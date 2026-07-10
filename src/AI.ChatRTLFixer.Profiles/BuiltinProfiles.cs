using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Profiles;

namespace AI.ChatRTLFixer.Profiles;

/// <summary>
/// Registry of built-in profiles. v0.1 ships Claude and ChatGPT as
/// Planned/Experimental until tested against real installed versions
/// (see docs/TESTPLAN.md and the "collect from real system" plan step).
/// No profile is marked Stable without a verified <see cref="AppProfile.TestedAppVersion"/>.
/// </summary>
public static class BuiltinProfiles
{
    public static IReadOnlyList<AppProfile> All => _all;

    private static readonly AppProfile[] _all =
    [
        ClaudeDesktop(),
        ChatGptDesktop(),
        CodexDesktop(),
        ZCode(),
        OpenClaw(),
        HermesAgent(),
        LmStudio(),
        AnythingLlmDesktop(),
    ];

    /// <summary>
    /// Claude Desktop. Electron-based. Selectors are PLANNED placeholders until
    /// verified against a real install; status is Planned.
    /// </summary>
    public static AppProfile ClaudeDesktop() => new()
    {
        AppId = "claude-desktop",
        DisplayName = "Claude Desktop",
        ProcessNames = ["Claude"],
        ExecutablePathPatterns = ["**\\Claude\\Claude.exe", "**/Claude/Claude.exe"],
        WindowTitlePatterns = ["Claude"],
        UiTechnology = UiTechnology.Electron,
        Status = SupportStatus.Planned,
        RequiresRelaunch = true,
        Cdp = new CdpStrategy { TargetTitlePattern = "Claude" },
        Selectors = new Selectors
        {
            // PLACEHOLDER selectors — must be collected from a real install before Stable.
            ChatContainer = "[data-testid='chat']",
            MessageRoot = "[data-testid='messages']",
            UserMessage = "[data-testid='user-message']",
            AssistantMessage = "[data-testid='assistant-message']",
            Composer = "[data-testid='composer'] textarea",
            CodeBlock = "pre code",
            InlineCode = "code",
            CopyRoot = "[data-testid='chat']",
            Protected = ["pre", "code", ".code-block", "[data-code]"],
            FontScope = "[data-testid='chat'], [data-testid='composer']",
        },
        KnownLimitations = ["Selectors not yet verified against a real installed version."],
        SafetyNotes = ["Runtime-only via CDP on 127.0.0.1. No permanent modification."],
    };

    public static AppProfile ChatGptDesktop() => new()
    {
        AppId = "chatgpt-desktop",
        DisplayName = "ChatGPT Desktop",
        ProcessNames = ["ChatGPT"],
        ExecutablePathPatterns = ["**\\ChatGPT\\ChatGPT.exe", "**/ChatGPT/ChatGPT.exe"],
        WindowTitlePatterns = ["ChatGPT"],
        UiTechnology = UiTechnology.Electron,
        Status = SupportStatus.Planned,
        RequiresRelaunch = true,
        Cdp = new CdpStrategy { TargetTitlePattern = "ChatGPT" },
        Selectors = new Selectors
        {
            ChatContainer = "#main",
            MessageRoot = "[class*='thread']",
            UserMessage = "[data-testid*='conversation-turn']",
            AssistantMessage = "[data-testid*='conversation-turn']",
            Composer = "textarea#prompt-textarea",
            CodeBlock = "pre code",
            InlineCode = "code",
            CopyRoot = "#main",
            Protected = ["pre", "code"],
            FontScope = "#main, textarea#prompt-textarea",
        },
        KnownLimitations = ["Selectors not yet verified against a real installed version."],
    };

    public static AppProfile CodexDesktop() => Planned("codex-desktop", "Codex Desktop", ["Codex"], UiTechnology.Unknown);

    public static AppProfile ZCode() => Planned("zcode", "ZCode", ["ZCode"], UiTechnology.Unknown);

    public static AppProfile OpenClaw() => Planned("openclaw", "OpenClaw", ["OpenClaw"], UiTechnology.Unknown);

    public static AppProfile HermesAgent() => Planned("hermes-agent", "Hermes Agent", ["HermesAgent"], UiTechnology.Unknown);

    public static AppProfile LmStudio() => Planned("lm-studio", "LM Studio", ["LM Studio"], UiTechnology.Unknown,
        "LM Studio is Electron-based in many builds; UI tech must be confirmed before a profile is written.");

    public static AppProfile AnythingLlmDesktop() => Planned("anythingllm-desktop", "AnythingLLM Desktop", ["AnythingLLM"], UiTechnology.Unknown);

    private static AppProfile Planned(string id, string display, string[] processNames, UiTechnology tech, string? note = null)
    {
        var limitations = new List<string> { "Not implemented yet. Detected only; no injection performed." };
        if (note is not null) limitations.Add(note);
        return new AppProfile
        {
            AppId = id,
            DisplayName = display,
            ProcessNames = processNames,
            UiTechnology = tech,
            Status = SupportStatus.Unsupported,
            RequiresRelaunch = true,
            KnownLimitations = limitations.ToArray(),
            SafetyNotes = ["No safe injection method found yet."],
        };
    }
}