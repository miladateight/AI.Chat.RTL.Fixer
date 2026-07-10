namespace AI.ChatRTLFixer.Core.Profiles;

/// <summary>
/// Describes one target application and how to fix its chat surface.
/// A profile is only the data/strategy; the runtime logic lives in adapters
/// and the shared rule engine. <see cref="Status"/> is only ever
/// <see cref="SupportStatus.Stable"/> after the profile has been tested
/// against a real installed app version.
/// </summary>
public sealed class AppProfile
{
    /// <summary>Stable identifier, e.g. "claude-desktop".</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>User-facing name, e.g. "Claude Desktop".</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Process names to match (without path, case-insensitive).</summary>
    public string[] ProcessNames { get; set; } = [];

    /// <summary>Executable path glob patterns, e.g. "**\\Claude\\Claude.exe".</summary>
    public string[] ExecutablePathPatterns { get; set; } = [];

    /// <summary>Window title substrings/regexes, useful when ambiguous.</summary>
    public string[] WindowTitlePatterns { get; set; } = [];

    public UiTechnology UiTechnology { get; set; } = UiTechnology.Unknown;

    public SupportStatus Status { get; set; } = SupportStatus.Planned;

    /// <summary>True if enabling the fix requires relaunching the app with debug args.</summary>
    public bool RequiresRelaunch { get; set; } = true;

    /// <summary>CDP strategy for Electron apps. Null for non-Electron.</summary>
    public CdpStrategy? Cdp { get; set; }

    /// <summary>DOM selectors scoped to the chat surface only.</summary>
    public Selectors Selectors { get; set; } = new();

    public string[] KnownLimitations { get; set; } = [];

    /// <summary>App version the profile was last verified against. Null until tested.</summary>
    public string? TestedAppVersion { get; set; }

    public DateTime? LastVerifiedDate { get; set; }

    public string[] SafetyNotes { get; set; } = [];

    public RestoreStrategy Restore { get; set; } = new();
}

/// <summary>DOM selectors. All must be scoped to the chat surface; never select sidebar/menu/etc.</summary>
public sealed class Selectors
{
    /// <summary>The outermost chat scroll container. Scans are limited to this node.</summary>
    public string ChatContainer { get; set; } = string.Empty;

    /// <summary>Wrapper around all messages inside the chat container.</summary>
    public string MessageRoot { get; set; } = string.Empty;

    /// <summary>User message bubble/wrapper.</summary>
    public string UserMessage { get; set; } = string.Empty;

    /// <summary>Assistant message bubble/wrapper.</summary>
    public string AssistantMessage { get; set; } = string.Empty;

    /// <summary>The composer/input/prompt box.</summary>
    public string Composer { get; set; } = string.Empty;

    /// <summary>Fenced code blocks, e.g. "pre code".</summary>
    public string CodeBlock { get; set; } = string.Empty;

    /// <summary>Inline code, e.g. "code".</summary>
    public string InlineCode { get; set; } = string.Empty;

    /// <summary>Scope inside which copy events are intercepted.</summary>
    public string CopyRoot { get; set; } = string.Empty;

    /// <summary>Elements that must never be touched (direction-wise), in addition to code blocks.</summary>
    public string[] Protected { get; set; } = [];

    /// <summary>Scope that receives the RTL font family. Usually chat messages + composer.</summary>
    public string FontScope { get; set; } = string.Empty;
}

/// <summary>CDP endpoint strategy. Always loopback; port is random per session.</summary>
public sealed class CdpStrategy
{
    /// <summary>Always 127.0.0.1. Stored so profiles are explicit.</summary>
    public string BindAddress { get; set; } = Constants.LoopbackAddress;

    /// <summary>How to pick a port. v0.1 always uses random-free.</summary>
    public CdpPortMode PortMode { get; set; } = CdpPortMode.RandomFree;

    /// <summary>Args appended when relaunching. ${port} is substituted at runtime.</summary>
    public string[] LaunchArgs { get; set; } =
    [
        "--remote-debugging-port=${port}",
        "--remote-debugging-address=127.0.0.1",
    ];

    /// <summary>Preferred attach mode.</summary>
    public CdpAttachMode AttachMode { get; set; } = CdpAttachMode.Relaunch;

    /// <summary>How long to wait for the debug endpoint to come up after relaunch.</summary>
    public int AttachTimeoutMs { get; set; } = 8000;

    /// <summary>Discovery URL template; ${port} is substituted.</summary>
    public string DiscoverUrl { get; set; } = "http://127.0.0.1:${port}/json";

    /// <summary>Optional page URL/title pattern to identify the chat page among targets.</summary>
    public string TargetTitlePattern { get; set; } = string.Empty;
}

public enum CdpPortMode
{
    /// <summary>Pick a random free port in the configured range on 127.0.0.1.</summary>
    RandomFree,
}

/// <summary>How to clean up when disabled / on exit.</summary>
public sealed class RestoreStrategy
{
    /// <summary>True to attempt a soft page reload as a last resort (with user consent).</summary>
    public bool AllowSoftReloadFallback { get; set; } = true;
}