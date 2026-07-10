namespace AI.ChatRTLFixer.Core.Rules;

/// <summary>
/// Classification of a single text block, produced by the canonical JS rule
/// engine. C# tests run the same JS via Jint and deserialize this shape.
/// </summary>
public sealed class Classification
{
    /// <summary>Chosen direction for the block.</summary>
    public BlockDirection Direction { get; set; } = BlockDirection.Ltr;

    /// <summary>True for technical text that must never be flipped.</summary>
    public bool Protected { get; set; }

    /// <summary>CSS text-align value: "start" (logical) or "left".</summary>
    public string Align { get; set; } = "left";

    /// <summary>Detected protected technical token types within the block.</summary>
    public List<string> Tokens { get; set; } = [];

    /// <summary>Ratio of RTL alpha chars to total alpha chars (0..1).</summary>
    public double RtlRatio { get; set; }

    /// <summary>Length of the classified text (for logging without content).</summary>
    public int Length { get; set; }
}