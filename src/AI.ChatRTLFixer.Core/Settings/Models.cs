using System.Text.Json.Serialization;

namespace AI.ChatRTLFixer.Core.Settings;

/// <summary>
/// Persisted user settings. Stored as JSON at
/// %AppData%\AIChatRTLFixer\settings.json. Never contains chat text.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Schema version for forward migrations.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public const int CurrentSchemaVersion = 5;

    /// <summary>Global kill switch. When false, no app is touched.</summary>
    public bool GlobalEnabled { get; set; } = true;

    /// <summary>Per-app enable map, keyed by <see cref="AppProfile.AppId"/>.</summary>
    public Dictionary<string, AppToggleState> Apps { get; set; } = new();

    public FontChoice SelectedFont { get; set; } = FontChoice.Vazirmatn;

    /// <summary>Optional path to a custom .ttf when <see cref="SelectedFont"/> is Custom.</summary>
    public string? CustomFontPath { get; set; }

    public CopyMode CopyMode { get; set; } = CopyMode.RtlReadable;

    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Checks the project's public GitHub Releases endpoint when the tray app
    /// starts. The request contains no account, device, chat or usage data.
    /// </summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>
    /// Opt-in developer mode. Only when true may short (truncated) text samples
    /// appear in logs. Default false. The UI must show a clear warning when
    /// enabling this.
    /// </summary>
    public bool DeveloperMode { get; set; }

    /// <summary>Developer diagnostics include paths and ignored candidates in exported reports.</summary>
    public bool DeveloperDiagnosticsEnabled { get; set; }

    /// <summary>
    /// Master switch for remembered relaunch consent. When true (default),
    /// an app the user has already explicitly consented to relaunch once
    /// (see <see cref="AppToggleState.RelaunchConsentGranted"/>) is relaunched
    /// again automatically on future detections, without re-prompting. This
    /// never affects the FIRST relaunch of any app: that always requires an
    /// explicit click through the tray "Relaunch with RTL Fix" menu and its
    /// confirmation dialog, regardless of this setting. Turning this off makes
    /// every relaunch, for every app, require a fresh click every time.
    /// </summary>
    public bool AutoRelaunchAfterConsent { get; set; } = true;

    public int RelaunchCooldownSeconds { get; set; } = 30;
    public int DiscoveryTimeoutSeconds { get; set; } = 20;
    public int InitialScanDelayMs { get; set; } = 0;
    public int ReconciliationIntervalSeconds { get; set; } = 15;
    public bool ShowUnsupportedApps { get; set; } = true;

    public PortRange PortRange { get; set; } = new();

    /// <summary>Last known installed version per app id (informational only).</summary>
    public Dictionary<string, string> LastKnownAppVersions { get; set; } = new();

    public LogLevel LoggingLevel { get; set; } = LogLevel.Information;

    /// <summary>UI culture code (e.g. "en"). v0.1 ships English UI.</summary>
    public string UiCulture { get; set; } = "en";
}

public sealed class AppToggleState
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// True once the user has explicitly clicked "yes" on the relaunch
    /// confirmation dialog for this app at least once. Only ever set by that
    /// dialog's own Yes button — never inferred or defaulted to true.
    /// </summary>
    public bool RelaunchConsentGranted { get; set; }
}

/// <summary>Inclusive port range for the random free CDP port picker.</summary>
public sealed class PortRange
{
    public int Min { get; set; } = 49152;
    public int Max { get; set; } = 65535;
}
