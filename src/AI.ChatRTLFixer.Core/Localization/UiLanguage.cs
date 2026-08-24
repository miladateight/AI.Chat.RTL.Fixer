namespace AI.ChatRTLFixer.Core.Localization;

/// <summary>
/// A language the interface can be shown in.
/// </summary>
/// <param name="Code">BCP-47 code, and the name of the embedded strings file.</param>
/// <param name="NativeName">Written the way its own speakers write it, which is
/// what belongs in a language picker — somebody who cannot read the current
/// interface language still has to recognise their own.</param>
/// <param name="EnglishName">Used in logs and in the settings file.</param>
/// <param name="IsRtl">Whether the interface itself should be mirrored.</param>
public sealed record UiLanguage(string Code, string NativeName, string EnglishName, bool IsRtl);

/// <summary>
/// The languages shipped with the app. This tool exists for people who read
/// right-to-left, so every major RTL script its rule engine already detects is
/// offered, with Persian as the default.
/// </summary>
public static class UiLanguages
{
    /// <summary>Used when nothing has been chosen and when a stored code is unknown.</summary>
    public const string DefaultCode = "fa";

    public static IReadOnlyList<UiLanguage> All { get; } =
    [
        new("fa", "فارسی",    "Persian", IsRtl: true),
        new("ar", "العربية",  "Arabic",  IsRtl: true),
        new("he", "עברית",    "Hebrew",  IsRtl: true),
        new("ur", "اردو",     "Urdu",    IsRtl: true),
        new("en", "English",  "English", IsRtl: false),
    ];

    public static UiLanguage Default => Get(DefaultCode);

    /// <summary>Resolves a code to a shipped language, falling back to the default.</summary>
    public static UiLanguage Get(string? code)
        => All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
           ?? All.First(l => l.Code == DefaultCode);

    public static bool IsSupported(string? code)
        => All.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
}
