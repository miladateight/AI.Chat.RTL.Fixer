using System.Reflection;

namespace AI.ChatRTLFixer.Core;

/// <summary>
/// Project-wide constants. Keep all magic strings/ids here so profiles,
/// injectors and the tray never drift apart.
/// </summary>
public static class Constants
{
    /// <summary>Product display name.</summary>
    public const string ProductName = "AI RTL Fixer";

    /// <summary>
    /// Product version, read from this assembly at runtime rather than repeated
    /// as a literal. It feeds the About dialog, the launch log line and the
    /// update check, so a stale value here makes a freshly installed build
    /// report the PREVIOUS version and offer itself as an update forever.
    /// The assembly stamp comes from &lt;Version&gt; in Directory.Build.props,
    /// which is the one place the version is declared.
    /// </summary>
    public static string AppVersion { get; } = ReadAssemblyVersion();

    private static string ReadAssemblyVersion()
    {
        var informational = typeof(Constants).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip any "+<commit>" build metadata so the value stays parseable
            // by Version.TryParse in the update checker.
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return typeof(Constants).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>Folder name under %AppData% used for settings and logs.</summary>
    public const string AppDataFolder = "AIChatRTLFixer";

    /// <summary>Settings file name.</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>Log file name.</summary>
    public const string LogFileName = "rtlfixer.log";

    /// <summary>GitHub link placeholder (filled when the repo is published).</summary>
    public const string GitHubLink = "https://github.com/miladateight/AI.RTL.Fixer";

    /// <summary>No-telemetry statement shown in the UI and README.</summary>
    public const string NoTelemetryStatement =
        "No telemetry or analytics. Optional update checks contact only GitHub; " +
        "target-app communication stays on local loopback.";

    /// <summary>Official GitHub Releases API endpoint used only for update checks.</summary>
    public const string GitHubLatestReleaseApi =
        "https://api.github.com/repos/miladateight/AI.RTL.Fixer/releases/latest";

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
