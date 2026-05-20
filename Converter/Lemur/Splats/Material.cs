namespace Converter.Lemur.Splats;

// A single material — one CK3 texture + a per-pixel weight rule. TextureName is the unique
// identifier (one material per output texture). The rule is a pure function:
// PixelContext → weight (≥ 0). Top-K materials per pixel are selected, normalised, and packed
// into the SplatPixel's four (index, intensity) slots.
public sealed class Material
{
    public string TextureName { get; }
    private readonly EvaluateRule _rule;

    public Material(string textureName, EvaluateRule rule)
    {
        TextureName = textureName;
        _rule = rule;
    }

    public float Evaluate(in PixelContext ctx) => _rule(in ctx);
}

// Custom delegate so PixelContext can be passed by `in` (struct copy avoided per call).
// Func<PixelContext, float> would force a value copy.
public delegate float EvaluateRule(in PixelContext ctx);
