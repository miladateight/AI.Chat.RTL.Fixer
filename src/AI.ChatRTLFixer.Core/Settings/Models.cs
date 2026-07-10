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

    public const int CurrentSchemaVersion = 1;

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
    /// Opt-in developer mode. Only when true may short (truncated) text samples
    /// appear in logs. Default false. The UI must show a clear warning when
    /// enabling this.
    /// </summary>
    public bool DeveloperMode { get; set; }

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
}

/// <summary>Inclusive port range for the random free CDP port picker.</summary>
public sealed class PortRange
{
    public int Min { get; set; } = 49152;
    public int Max { get; set; } = 65535;
}