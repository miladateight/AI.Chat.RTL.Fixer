using System.Reflection;
using System.Text;
using AI.ChatRTLFixer.Core;

namespace AI.ChatRTLFixer.Fonts;

/// <summary>
/// Bundled font handling. The Vazirmatn-Regular.ttf is embedded as a resource
/// and is injected once into a target page as a base64 @font-face. License: OFL.
/// </summary>
public static class FontPack
{
    private const string VazirmatnResource = "AI.ChatRTLFixer.Fonts.fonts.Vazirmatn-Regular.ttf";

    /// <summary>Returns the embedded Vazirmatn bytes, or null if missing.</summary>
    public static byte[]? LoadVazirmatn()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(VazirmatnResource);
        if (stream is null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Base64-encodes the bundled font for inline @font-face injection.</summary>
    public static string? LoadVazirmatnBase64()
    {
        var bytes = LoadVazirmatn();
        return bytes is null ? null : Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Builds the CSS font-family value for a <see cref="FontChoice"/>, with the
    /// fallback chain. Only applied to the chat surface (scoped by selectors).
    /// </summary>
    public static string FontFamilyCss(FontChoice choice, string? customPath = null)
    {
        return choice switch
        {
            FontChoice.Vazirmatn => "Vazirmatn, \"Noto Sans Arabic\", \"Segoe UI\", Tahoma, Arial",
            FontChoice.NotoSansArabic => "\"Noto Sans Arabic\", Vazirmatn, \"Segoe UI\", Tahoma, Arial",
            FontChoice.SegoeUI => "\"Segoe UI\", Tahoma, Arial",
            FontChoice.Tahoma => "Tahoma, Arial",
            FontChoice.Arial => "Arial",
            FontChoice.Custom => $"\"{EscapeFontName(customPath)}\", Vazirmatn, \"Segoe UI\", Tahoma, Arial",
            _ => "Vazirmatn, \"Segoe UI\", Tahoma, Arial",
        };
    }

    /// <summary>
    /// Builds a complete @font-face + font-family style block. The @font-face is
    /// only emitted when the bundled font is available and the choice uses it.
    /// </summary>
    public static string BuildFontStyle(string fontFamilyCss, string fontScopeSelector, string? base64)
    {
        var sb = new StringBuilder();
        // Only Vazirmatn is bundled; emit @font-face only when the family is Vazirmatn
        // and we have the data. If the font is already installed on the system, the
        // browser will use it from the @font-face data or the local install.
        if (fontFamilyCss.StartsWith("Vazirmatn", StringComparison.Ordinal) && !string.IsNullOrEmpty(base64))
        {
            sb.AppendLine("@font-face {");
            sb.AppendLine("  font-family: \"Vazirmatn\";");
            sb.AppendLine("  font-style: normal;");
            sb.AppendLine("  font-weight: 100 900;");
            sb.AppendLine("  font-display: swap;");
            sb.AppendLine($"  src: url(data:font/ttf;base64,{base64}) format('truetype');");
            sb.AppendLine("}");
        }
        // Apply the font only to the chat-surface scope (messages + composer).
        if (!string.IsNullOrEmpty(fontScopeSelector))
        {
            sb.AppendLine($"{fontScopeSelector} {{");
            sb.AppendLine($"  font-family: {fontFamilyCss};");
            sb.AppendLine("}");
        }
        return sb.ToString();
    }

    private static string EscapeFontName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "CustomFont";
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Replace("\"", string.Empty);
    }
}
