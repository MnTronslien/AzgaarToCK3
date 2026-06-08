using static Converter.Lemur.TenetData.Theme;

namespace Converter.Lemur;

/// <summary>
/// Maps an Azgaar religion <c>form</c> string to a baked <see cref="TenetData.Theme"/> set. A faith
/// whose form has themes biases its tenet draw toward tenets sharing any of those themes (see
/// <see cref="FaithTenetAssigner"/>, which folds in <see cref="Converter.Settings.FaithFormThemeBoost"/> on overlap).
///
/// <para>Deity-count labels (Polytheism / Monotheism / Dualism / Syncretism / Pantheism / Non-theism /
/// Deism / Henotheism) are deliberately absent ⇒ <see cref="TenetData.Theme.None"/> — they are a later
/// doctrine signal, not a tenet boost. Any unmapped form returns <see cref="TenetData.Theme.None"/>.</para>
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
