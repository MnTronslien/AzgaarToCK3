namespace Converter.Lemur.Governments;

/// <summary>
/// A CK3 government type. <see cref="Key"/> is the engine identifier emitted into title history
/// (e.g. "feudal_government", "nomad_government"). DLC-gated governments carry a <see cref="DlcFeature"/>
/// flag and a <see cref="Fallback"/>; the writer emits a script-side `has_dlc_feature` conditional
/// so the Jomini engine picks the real key at game-start, or the fallback when the DLC isn't loaded.
///
/// Invariant: <see cref="DlcFeature"/> and <see cref="Fallback"/> are either both null (plain
/// base-game government) or both non-null (DLC-gated). Catalog entries in <see cref="Ck3Governments"/>
/// uphold this.
/// </summary>
public sealed record Ck3Government(string Key, string? DlcFeature = null, Ck3Government? Fallback = null);

/// <summary>
/// Catalog of CK3 governments the converter emits. Only governments reachable from the
/// Azgaar-to-CK3 form mapping are listed — other valid CK3 governments (Mandala, Wanua,
/// Celestial, Meritocratic, Meritocratic Khanate, Ritsuryō, Herder, Adventurer, Holy Order,
/// Mercenary) are intentionally omitted because no Azgaar formName surfaces them.
///
/// Verified against the CK3 wiki and the local CK3 install. Code keys differ from wiki names
/// in two places worth noting: the wiki's "Nomadic" is `nomad_government` (singular), and
/// the wiki's "Sōryō" is `japan_feudal_government`.
/// </summary>
public static class Ck3Governments
{
    public static readonly Ck3Government Feudal    = new("feudal_government");
    public static readonly Ck3Government Clan      = new("clan_government");
    public static readonly Ck3Government Tribal    = new("tribal_government");
    public static readonly Ck3Government Republic  = new("republic_government");
    public static readonly Ck3Government Theocracy = new("theocracy_government");

    // DLC-gated. The engine swaps to Fallback at game-start when DlcFeature is unavailable.
    public static readonly Ck3Government Nomad          = new("nomad_government",            DlcFeature: "khans_of_the_steppe", Fallback: Tribal);
    public static readonly Ck3Government Administrative = new("administrative_government",   DlcFeature: "admin_gov",           Fallback: Feudal);
    public static readonly Ck3Government Soryo          = new("japan_feudal_government",     DlcFeature: "all_under_heaven",    Fallback: Feudal);
}
