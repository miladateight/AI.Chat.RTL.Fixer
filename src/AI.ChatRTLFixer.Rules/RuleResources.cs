using System.Reflection;
using System.Text.Json;

namespace AI.ChatRTLFixer.Rules;

/// <summary>
/// Loads the shared rule definition files that are embedded in this assembly.
/// The same files are injected into target pages at runtime (via Injectors) and
/// executed under Jint in tests (via <see cref="ReferenceEvaluator"/>), so there
/// is a single source of truth and no C#-vs-JS divergence.
/// </summary>
public static class RuleResources
{
    private const string RulesJs = "AI.ChatRTLFixer.Rules.rules.rtlfixer.rules.js";
    private const string SharedJson = "AI.ChatRTLFixer.Rules.rules.rule-engine.shared.json";

    /// <summary>The canonical JavaScript rule engine source.</summary>
    public static string LoadRulesJs()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(RulesJs)
            ?? throw new InvalidOperationException($"Embedded resource '{RulesJs}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The parsed shared rule definition (ranges, thresholds, protected types).</summary>
    public static JsonElement LoadSharedConfig()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SharedJson)
            ?? throw new InvalidOperationException($"Embedded resource '{SharedJson}' not found.");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.Clone();
    }
}