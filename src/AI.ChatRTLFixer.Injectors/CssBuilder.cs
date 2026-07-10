using System.Text;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Profiles;

namespace AI.ChatRTLFixer.Injectors;

/// <summary>
/// Builds the CSS injected into the chat surface. All rules are scoped to the
/// profile's selectors so the sidebar/menu/title/settings are never affected.
/// </summary>
public static class CssBuilder
{
    /// <summary>
    /// Builds the direction/alignment CSS. The runtime script applies per-node
    /// dir/align attributes for dynamic content; this static CSS covers the
    /// defaults and the hard-protected elements (code/paths never flipped).
    /// </summary>
    public static string Build(Selectors selectors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/* AI Chat RTL Fixer — chat surface only. Scoped CSS. */");

        // Default the chat container to LTR; the script overrides per-block.
        if (!string.IsNullOrEmpty(selectors.ChatContainer))
        {
            sb.AppendLine($"{selectors.ChatContainer} {{ direction: ltr; }}");
        }

        // Composer: set to LTR by default; the script flips to RTL while the
        // user types RTL text.
        if (!string.IsNullOrEmpty(selectors.Composer))
        {
            sb.AppendLine($"{selectors.Composer} {{ direction: ltr; text-align: left; }}");
        }

        // Hard-protected elements: always LTR, never flipped, never RTL font.
        var protectedSelectors = new List<string>();
        if (!string.IsNullOrEmpty(selectors.CodeBlock)) protectedSelectors.Add(selectors.CodeBlock);
        if (!string.IsNullOrEmpty(selectors.InlineCode)) protectedSelectors.Add(selectors.InlineCode);
        protectedSelectors.AddRange(selectors.Protected.Where(p => !string.IsNullOrEmpty(p)));

        if (protectedSelectors.Count > 0)
        {
            var joined = string.Join(", ", protectedSelectors);
            sb.AppendLine($"{joined} {{");
            sb.AppendLine("  direction: ltr !important;");
            sb.AppendLine("  text-align: left !important;");
            sb.AppendLine("  unicode-bidi: isolate;");
            sb.AppendLine("}");
        }

        return sb.ToString();
    }
}