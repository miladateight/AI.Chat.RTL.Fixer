namespace AI.ChatRTLFixer.Core;

/// <summary>
/// Project-wide constants. Keep all magic strings/ids here so profiles,
/// injectors and the tray never drift apart.
/// </summary>
public static class Constants
{
    /// <summary>Product display name.</summary>
    public const string ProductName = "AI Chat RTL Fixer";

    /// <summary>Product version. Keep in sync with Directory.Build.props and the installer.</summary>
    public const string AppVersion = "0.4.0";

    /// <summary>Folder name under %AppData% used for settings and logs.</summary>
    public const string AppDataFolder = "AIChatRTLFixer";

    /// <summary>Settings file name.</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>Log file name.</summary>
    public const string LogFileName = "rtlfixer.log";

    /// <summary>GitHub link placeholder (filled when the repo is published).</summary>
    public const string GitHubLink = "https://github.com/miladateight/AI.Chat.RTL.Fixer";

    /// <summary>No-telemetry statement shown in the UI and README.</summary>
    public const string NoTelemetryStatement =
        "No telemetry. No analytics. No external network calls. Only local loopback " +
        "communication with debug-enabled target apps.";

    /// <summary>HTML id of the injected style element that carries direction CSS.</summary>
    public const string CssStyleId = "rtlfixer-css";

    /// <summary>HTML id of the injected style element that carries the @font-face.</summary>
    public const string FontStyleId = "rtlfixer-font";

    /// <summary>HTML id of the injected script element (informational; scripts run, they are not kept as a node).</summary>
    public const string ScriptMarkerId = "rtlfixer-script";

    /// <summary>Data attribute stamped on every node we touch, for restore.</summary>
    public const string AppliedDataAttr = "data-rtlfixer";

    /// <summary>Window flag set once the font has been injected into a page.</summary>
    public const string FontInjectedFlag = "__rtlfixerFontInjected";

    /// <summary>Window flag set once the runtime script has been installed into a page.</summary>
    public const string ScriptInstalledFlag = "__rtlfixerInstalled";

    /// <summary>Default CDP bind address. Always loopback.</summary>
    public const string LoopbackAddress = "127.0.0.1";
}
