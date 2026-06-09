using static Converter.Lemur.TenetData.Theme;

namespace Converter.Lemur;

/// <summary>
/// Maps an Azgaar religion <c>form</c> to a <see cref="TenetData.Theme"/> set that biases the faith's
/// tenet draw (see <see cref="FaithTenetAssigner"/>). Unmapped forms (incl. deity-count labels like
/// Monotheism) → <see cref="TenetData.Theme.None"/>. Conversion rules: docs/CONVERSION_RULES.md#faith-tenets.
/// </summary>
public static class FormThemes
{
    private static readonly IReadOnlyDictionary<string, TenetData.Theme> Map =
        new Dictionary<string, TenetData.Theme>
        {
            ["Nature Worship"]   = Nature,
            ["Animism"]          = Nature,
            ["Shamanism"]        = Nature | Occult,
            ["Totemism"]         = Nature | Ancestral,
            ["Ancestor Worship"] = Ancestral,
            ["Philosophical"]    = Scholarly,
            ["Ethical"]          = Scholarly | Communal,
            ["Cult"]             = Occult,
            ["Dark Cult"]        = Occult | Sacrificial | Hedonistic | Martial,
            ["Sect"]             = Occult,
        };

    public static TenetData.Theme Of(string? form) =>
        (form != null && Map.TryGetValue(form, out var t)) ? t : TenetData.Theme.None;
}
