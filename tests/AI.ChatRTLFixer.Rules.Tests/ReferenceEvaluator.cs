using System.Text.Json;
using AI.ChatRTLFixer.Core;
using AI.ChatRTLFixer.Core.Rules;
using AI.ChatRTLFixer.Rules;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace AI.ChatRTLFixer.Rules.Tests;

/// <summary>
/// Executes the canonical <c>rtlfixer.rules.js</c> under Jint so that unit
/// tests run the EXACT same code that is injected into target pages at runtime.
/// This is the anti-divergence guarantee: C# tests and JS runtime share one engine.
/// </summary>
public sealed class ReferenceEvaluator
{
    private readonly Engine _engine;

    public ReferenceEvaluator()
    {
        _engine = new Engine(opts => opts.LimitRecursion(10_000));
        // Provide a minimal CommonJS shim so the IIFE can assign to module.exports.
        _engine.Execute("var module = { exports: {} }; var exports = module.exports;");
        var js = RuleResources.LoadRulesJs();
        _engine.Execute(js);

        // Feed the shared JSON config into the engine.
        var config = RuleResources.LoadSharedConfig();
        var configJson = config.GetRawText();
        _engine.Execute($"module.exports.setConfig({configJson});");
    }

    /// <summary>Classify a text block through the canonical JS engine.</summary>
    public Classification Classify(string text)
    {
        var result = _engine.Call("module.exports.classify", text);
        return ToClassification(result);
    }

    /// <summary>Classify a node, marking whether it is inside a protected selector.</summary>
    public Classification ClassifyNode(string text, bool insideProtected)
    {
        var result = _engine.Call("module.exports.classifyNode", text, insideProtected);
        return ToClassification(result);
    }

    public string BuildPlainText(string text, string mode)
        => (string)_engine.Call("module.exports.buildPlainText", text, mode).ToObject()!;

    public string BuildHtml(string text, string mode, string direction)
        => (string)_engine.Call("module.exports.buildHtml", text, mode, direction).ToObject()!;

    public double RtlRatio(string text)
        => (double)_engine.Call("module.exports.rtlRatio", text).ToObject()!;

    private static Classification ToClassification(JsValue v)
    {
        var obj = v.AsObject();
        var direction = obj.Get("direction").AsString() switch
        {
            "rtl" => BlockDirection.Rtl,
            _ => BlockDirection.Ltr,
        };
        var protect = obj.Get("protected").AsBoolean();
        var align = obj.Get("align").AsString();
        var tokens = obj.Get("tokens").AsArray().Select(t => t.AsString()).ToList();
        var ratio = (double)obj.Get("rtlRatio").AsNumber();
        var len = (int)obj.Get("length").AsNumber();
        return new Classification
        {
            Direction = direction,
            Protected = protect,
            Align = align,
            Tokens = tokens,
            RtlRatio = ratio,
            Length = len,
        };
    }
}