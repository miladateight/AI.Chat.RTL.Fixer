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

/// <summary>Lifecycle state of one detected target process.  States are intentionally
/// per process instance, not per profile, because Electron applications can be
/// restarted while another instance is still being reconciled.</summary>
public enum AppRuntimeState
{
    Unknown,
    Detected,
    RunningNoDebugPort,

    /// <summary>Detected without a debug port; a relaunch is needed. Surfaced in the tray for one-click consent.</summary>
    RelaunchRequired,

    /// <summary>Waiting on the user's explicit yes/no in the relaunch confirmation dialog.</summary>
    RelaunchPromptShown,

    /// <summary>The user consented; the old process is being closed and a new one started with debug args.</summary>
    Relaunching,

    /// <summary>Relaunch completed; waiting for the new process to publish its CDP endpoint.</summary>
    WaitingForCdp,
    CdpDiscovered,
    Attached,
    InjectionSucceeded,
    InjectionFailed,
    CdpUnsupported,

    /// <summary>Relaunched with debug args, but the new process never opened the port (args were dropped/ignored).</summary>
    DebugArgsIgnored,
    Exited,
    DisabledByUser,
    Unsupported,
}
