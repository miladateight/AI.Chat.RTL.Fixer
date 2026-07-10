using System.Text;
using AI.ChatRTLFixer.Core;

namespace AI.ChatRTLFixer.Clipboard;

/// <summary>
/// Builds clipboard payloads (text/plain and text/html) for copied chat text
/// according to the selected <see cref="CopyMode"/>. The actual JS that runs in
/// the target page uses the canonical rule engine; this C# helper mirrors the
/// plain-text marker logic for any fallback paths and for tests.
/// </summary>
public static class ClipboardPayloadBuilder
{
    private const char Rlm = '\u200F';
    private const char Lrm = '\u200E';

    /// <summary>
    /// Returns the plain-text representation for the given mode. Code/path/url
    /// /command/config are never touched by markers: callers must split those
    /// out before calling this. Here we only add markers around natural-language
    /// RTL text, and only when the mode requests them.
    /// </summary>
    public static string BuildPlain(string text, CopyMode mode)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return mode switch
        {
            CopyMode.Original => text,
            CopyMode.RtlReadableNoMarkers => text,
            CopyMode.RtlReadable => AddRtlMarkers(text),
            _ => text,
        };
    }

    /// <summary>
    /// Returns a safe HTML representation. text/html may carry dir, bdi and
    /// unicode-bidi: isolate spans, but never around technical tokens.
    /// </summary>
    public static string BuildHtml(string text, CopyMode mode, bool rtl)
    {
        var dir = rtl ? "rtl" : "ltr";
        var escaped = HtmlEscape(text);
        if (mode == CopyMode.Original)
        {
            return $"<span>{escaped}</span>";
        }
        return $"<span dir=\"{dir}\" style=\"unicode-bidi: isolate\">{escaped}</span>";
    }

    private static string AddRtlMarkers(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text);
        if (IsRtlChar(sb[0])) sb.Insert(0, Rlm);
        if (IsRtlChar(sb[^1])) sb.Append(Rlm);
        return sb.ToString();
    }

    private static bool IsRtlChar(char ch)
    {
        var c = (int)ch;
        return (c >= 0x0590 && c <= 0x05FF) || (c >= 0xFB1D && c <= 0xFB4F) ||
               (c >= 0x0600 && c <= 0x06FF) || (c >= 0x0750 && c <= 0x077F) ||
               (c >= 0x08A0 && c <= 0x08FF) || (c >= 0xFB50 && c <= 0xFDFF) ||
               (c >= 0xFE70 && c <= 0xFEFF);
    }

    private static string HtmlEscape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}