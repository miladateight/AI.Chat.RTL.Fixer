namespace AI.ChatRTLFixer.Core;

/// <summary>
/// UI technology of a target application. Determines which adapter is used.
/// v0.1 only implements <see cref="Electron"/>; the rest are detected but not
/// injected (Planned/Unsupported).
/// </summary>
public enum UiTechnology
{
    Unknown,
    Electron,
    WebView2,
    Tauri,
    Qt,
    Wpf,
    Native,
}

/// <summary>
/// Support status of an app profile. "Stable" is only ever assigned after the
/// profile has been tested against a real, installed app version.
/// </summary>
public enum SupportStatus
{
    /// <summary>Tested and reliable against a real installed app.</summary>
    Stable,

    /// <summary>Works but may break after app updates; selectors not finalized.</summary>
    Experimental,

    /// <summary>Detected/app known, but no safe injection implemented yet.</summary>
    Planned,

    /// <summary>No safe method found yet.</summary>
    Unsupported,
}

/// <summary>How the CDP debug endpoint should be obtained for an Electron app.</summary>
public enum CdpAttachMode
{
    /// <summary>Attach to an already-running app that already has the debug port open.</summary>
    Attach,

    /// <summary>Relaunch the app (with user consent) with the debug port args appended.</summary>
    Relaunch,

    /// <summary>Show the user the exact command to run manually; do not close the app.</summary>
    ManualReopen,
}

/// <summary>Per-block direction decision produced by the rule engine.</summary>
public enum BlockDirection
{
    /// <summary>Right-to-left, right-aligned (RTL content, not technical).</summary>
    Rtl,

    /// <summary>Left-to-right, left-aligned (English / mostly technical).</summary>
    Ltr,

    /// <summary>Technical text that must never be flipped.</summary>
    Protected,
}

/// <summary>Copy behavior for chat text.</summary>
public enum CopyMode
{
    /// <summary>Pass through whatever the app puts on the clipboard.</summary>
    Original,

    /// <summary>RTL-readable, with invisible bidi markers around natural-language RTL text.</summary>
    RtlReadable,

    /// <summary>RTL-readable but without any invisible Unicode bidi markers.</summary>
    RtlReadableNoMarkers,
}

/// <summary>Font options offered in the tray.</summary>
public enum FontChoice
{
    Vazirmatn,
    NotoSansArabic,
    SegoeUI,
    Tahoma,
    Arial,
    Custom,
}

/// <summary>Logging verbosity.</summary>
public enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical,
}