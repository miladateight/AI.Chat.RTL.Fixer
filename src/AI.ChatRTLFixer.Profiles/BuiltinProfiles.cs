using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Profiles;

namespace AI.ChatRTLFixer.Profiles;

/// <summary>
/// Registry of built-in profiles. No profile is marked <see cref="SupportStatus.Stable"/>
/// without a verified <see cref="AppProfile.TestedAppVersion"/> collected from a real
/// installed version (see docs/TESTPLAN.md). Electron apps ship Experimental
/// selectors so the fix works out of the box and can be refined after real testing.
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
        OpenCodeDesktop(),
        OpenClaw(),
        HermesAgent(),
        LmStudio(),
        AnythingLlmDesktop(),
        JanDesktop(),
        CherryStudioDesktop(),
        MstyDesktop(),
        GithubCopilotDesktop(),
    ];

    /// <summary>
    /// Claude Desktop (Anthropic). Electron-based. Selectors are best-effort
    /// based on the public DOM structure; marked Experimental until verified.
    /// </summary>
    public static AppProfile ClaudeDesktop() => new()
    {
        AppId = "claude-desktop",
        DisplayName = "Claude Desktop",
        ProcessNames = ["Claude", "claude", "Claude Desktop"],
        ExecutablePathPatterns = ["**\\Claude\\Claude.exe", "**/Claude/Claude.exe"],
        ProductNamePatterns = ["Claude", "Anthropic"],
        KnownInstallLocations = ["%LOCALAPPDATA%\\Programs\\Claude", "%LOCALAPPDATA%\\Claude"],
        WindowTitlePatterns = ["Claude"],
        UiTechnology = UiTechnology.Electron,
        Status = SupportStatus.Experimental,
        Cdp = new CdpStrategy { TargetTitlePattern = "Claude" },
        Selectors = new Selectors
        {
            ChatContainer = "[data-testid='chat']",
            MessageRoot = "[data-testid='messages']",
            UserMessage = "[data-testid='user-message']",
            AssistantMessage = "[data-testid='assistant-message']",
            Composer = "[data-testid='composer'] textarea, [contenteditable='true']",
            CodeBlock = "pre code",
            InlineCode = "code",
            CopyRoot = "[data-testid='chat']",
            Protected = ["pre", "code", ".code-block", "[data-code]", "kbd"],
            FontScope = "[data-testid='chat'], [data-testid='composer'] textarea, [contenteditable='true']",
        },
        KnownLimitations = ["Selectors not yet verified against a real installed version. Composer may be contenteditable."],
        SafetyNotes = ["Runtime-only via CDP on 127.0.0.1. No permanent modification."],
    };

    /// <summary>
    /// ChatGPT Desktop (OpenAI). Electron-based. Conversation turns share one
    /// test-id; user vs assistant are distinguished by data attribute position,
    /// so both selectors point at the turn wrapper and the script classifies each
    /// inner block individually.
    /// </summary>
    public static AppProfile ChatGptDesktop() => new()
    {
        AppId = "chatgpt-desktop",
        DisplayName = "ChatGPT Desktop",
        ProcessNames = ["ChatGPT", "chatgpt"],
        ExecutablePathPatterns = ["**\\ChatGPT\\ChatGPT.exe", "**/ChatGPT/ChatGPT.exe"],
        WindowTitlePatterns = ["ChatGPT"],
        UiTechnology = UiTechnology.Electron,
        Status = SupportStatus.Experimental,
        Cdp = new CdpStrategy { TargetTitlePattern = "ChatGPT" },
        Selectors = new Selectors
        {
            ChatContainer = "#main, main, #root",
            MessageRoot = "[class*='thread'], main",
            UserMessage = "[data-testid^='conversation-turn-']:nth-child(odd)",
            AssistantMessage = "[data-testid^='conversation-turn-']:nth-child(even)",
            Composer = "textarea#prompt-textarea, #prompt-textarea, textarea, [contenteditable='true']",
            CodeBlock = "pre code",
            InlineCode = "code",
            CopyRoot = "#main, main, #root",
            Protected = ["pre", "code", "kbd"],
            FontScope = "#main, main, #root, textarea, [contenteditable='true']",
        },
        KnownLimitations = ["Selectors not yet verified against a real installed version. Turn parity relies on DOM order."],
        SafetyNotes = ["Runtime-only via CDP on 127.0.0.1. No permanent modification."],
    };

    /// <summary>
    /// Codex Desktop (OpenAI). Likely Electron; UI tech to be confirmed.
    /// </summary>
    public static AppProfile CodexDesktop() => ElectronExperimental(
        "codex-desktop", "Codex Desktop", ["Codex", "codex", "Codex Desktop"], "Codex",
        "Codex process aliases are best-effort and must be verified in the detection report.",
        ["Codex", "OpenAI"], ["%LOCALAPPDATA%\\Programs\\Codex", "%LOCALAPPDATA%\\Codex"]);

    /// <summary>
    /// ZCode desktop client. UI tech to be confirmed.
    /// </summary>
    public static AppProfile ZCode() => ElectronExperimental(
        "zcode", "ZCode", ["ZCode", "zcode"], "ZCode",
        "CDP compatibility and debug-argument handling are unverified; runtime injection remains Experimental.",
        ["ZCode"], ["%LOCALAPPDATA%\\Programs\\ZCode"]);

    /// <summary>
    /// OpenCode Desktop (@opencode-aidesktop). Electron-based; verified to honor
    /// --remote-debugging-port. Uses the generic chat selectors.
    /// </summary>
    public static AppProfile OpenCodeDesktop() => ElectronExperimental(
        "opencode-desktop", "OpenCode", ["OpenCode", "opencode"], "OpenCode",
        "Detection matches the OpenCode desktop GUI only; the 'opencode' CLI/TUI shares the name but is filtered as a non-GUI backend.",
        ["OpenCode"], ["%LOCALAPPDATA%\\Programs\\@opencode-aidesktop", "%LOCALAPPDATA%\\Programs\\OpenCode"]);

    /// <summary>
    /// OpenClaw. UI tech to be confirmed.
    /// </summary>
    public static AppProfile OpenClaw() => ElectronExperimental(
        "openclaw", "OpenClaw", ["OpenClaw"], "OpenClaw");

    /// <summary>
    /// Hermes Agent. UI tech to be confirmed.
    /// </summary>
    public static AppProfile HermesAgent() => ElectronExperimental(
        "hermes-agent", "Hermes Agent", ["HermesAgent"], "Hermes");

    /// <summary>
    /// LM Studio. Electron-based in many builds; UI tech must be confirmed.
    /// </summary>
    public static AppProfile LmStudio() => ElectronExperimental(
        "lm-studio", "LM Studio", ["LM Studio", "LMStudio"], "LM Studio",
        "LM Studio is Electron-based in many builds; UI tech must be confirmed before a profile is finalized.");

    /// <summary>
    /// AnythingLLM Desktop. Electron-based.
    /// </summary>
    public static AppProfile AnythingLlmDesktop() => ElectronExperimental(
        "anythingllm-desktop", "AnythingLLM Desktop", ["AnythingLLM"], "AnythingLLM");

    /// <summary>
    /// Jan (jan.ai) Desktop. Electron-based.
    /// </summary>
    public static AppProfile JanDesktop() => ElectronExperimental(
        "jan-desktop", "Jan", ["Jan", "jan"], "Jan");

    /// <summary>
    /// Cherry Studio. Electron-based.
    /// </summary>
    public static AppProfile CherryStudioDesktop() => ElectronExperimental(
        "cherry-studio", "Cherry Studio", ["CherryStudio"], "Cherry Studio");

    /// <summary>
    /// Msty. Electron-based.
    /// </summary>
    public static AppProfile MstyDesktop() => ElectronExperimental(
        "msty", "Msty", ["Msty"], "Msty");

    /// <summary>
    /// GitHub Copilot Desktop. UI tech to be confirmed.
    /// </summary>
    public static AppProfile GithubCopilotDesktop() => ElectronExperimental(
        "copilot-desktop", "GitHub Copilot", ["Copilot"], "Copilot");

    /// <summary>
    /// Builds an Experimental Electron profile with generic but broadly-applicable
    /// selectors. All such profiles stay Experimental until verified against a
    /// real installed version (see <see cref="SupportStatus"/>).
    /// </summary>
    private static AppProfile ElectronExperimental(
        string id, string display, string[] processNames, string titlePattern, string? note = null,
        string[]? productNames = null, string[]? installLocations = null)
    {
        var limitations = new List<string>
        {
            "Selectors are generic placeholders pending verification against a real installed version.",
            "Detected and injected on a best-effort basis; may require selector refinement.",
        };
        if (note is not null) limitations.Add(note);
        return new AppProfile
        {
            AppId = id,
            DisplayName = display,
            ProcessNames = processNames,
            ProductNamePatterns = productNames ?? [display],
            WindowTitlePatterns = [titlePattern],
            KnownInstallLocations = installLocations ?? [],
            UiTechnology = UiTechnology.Electron,
            Status = SupportStatus.Experimental,
            Cdp = new CdpStrategy { TargetTitlePattern = titlePattern },
            Selectors = GenericSelectors(),
            KnownLimitations = limitations.ToArray(),
            SafetyNotes = ["Runtime-only via CDP on 127.0.0.1. No permanent modification."],
        };
    }

    /// <summary>
    /// Generic selectors that work across many Electron chat UIs: paragraphs,
    /// list items and headings inside the main scroll area. The runtime script
    /// classifies each block individually, so even imprecise container selectors
    /// produce correct per-block direction.
    /// </summary>
    private static Selectors GenericSelectors() => new()
    {
        ChatContainer = "main, [class*='chat'], [class*='conversation'], [class*='messages'], #app",
        MessageRoot = "[class*='messages'], [class*='thread'], main",
        UserMessage = "[class*='user'], [class*='human'], [data-role='user']",
        AssistantMessage = "[class*='assistant'], [class*='bot'], [class*='ai'], [data-role='assistant']",
        Composer = "textarea, [contenteditable='true'], [class*='composer'], [class*='input']",
        CodeBlock = "pre code",
        InlineCode = "code",
        CopyRoot = "main, [class*='chat'], [class*='conversation']",
        Protected = ["pre", "code", "kbd"],
        FontScope = "main, textarea, [contenteditable='true']",
    };
}
