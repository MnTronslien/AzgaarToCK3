using Converter.Lemur.Entities;
using Converter.Lemur.Fields;
using Converter.Lemur.Provinces;
using static Converter.Lemur.TenetData.ReligionType;
using static Converter.Lemur.TenetData.Theme;
using static Converter.Lemur.Provinces.Ck3Terrain;

namespace Converter.Lemur;

/// <summary>
/// CK3 faith tenet pool. Each tenet is a <see cref="TenetEntry"/>: a name + one eval lambda returning
/// a weight in <c>[0, 1]</c>. Mutual exclusivity is not in the lambdas — the assigner applies the
/// central <see cref="ConflictGraph"/>. Source: <c>CK3_TENETS_CATALOG.md</c> (CK3 1.19).
/// </summary>
public static class TenetData
{
    /// <summary>Soft magnitude: an unfavoured-type tenet keeps this fraction of its weight.</summary>
    public const float TypeMismatchPenalty = 0.3f;

    /// <summary>The lone terrain tenet's threshold (cthonic_redoubts; mountains + desert mountains).</summary>
    public const float CthonicThreshold = 0.20f;

    /// <summary>A faith's type, 1:1 with Azgaar's <c>Faith.Type</c>. <c>[Flags]</c> so a tenet's
    /// favoured <i>set</i> is expressible (<c>Folk | Cult</c>); Heresy is a peer, not derived.</summary>
    [Flags]
    public enum ReligionType
    {
        None      = 0,
        Folk      = 1,
        Organized = 2,
        Cult      = 4,
        Heresy    = 8,
        Any       = 15,
    }

    /// <summary>A tenet's thematic tags (from <c>tenet_distribution.csv</c>). <c>[Flags]</c> so a tag
    /// <i>set</i> is expressible; a faith's form maps to a tag set too and the assigner boosts on
    /// overlap. Duds / blank → <see cref="None"/>.</summary>
    [Flags]
    public enum Theme
    {
        None          = 0,
        Nature        = 1,
        Ancestral     = 2,
        Communal      = 4,
        Martial       = 8,
        Sacrificial   = 16,
        Occult        = 32,
        Ascetic       = 64,
        Hedonistic    = 128,
        Scholarly     = 256,
        Dharmic       = 512,
        Institutional = 1024,
        Pacific       = 2048,
    }

    /// <summary>Hand-rolled <c>in</c>-by-ref delegate (mirrors the splatmap's <c>EvaluateRule</c>;
    /// avoids copying the struct). Returns a weight in <c>[0, 1]</c>.</summary>
    public delegate float TenetEval(in FaithContext ctx);

    /// <summary>A tenet: its key + baked theme tags + one eval lambda. Nothing else.</summary>
    public record TenetEntry(string Name, Theme Themes, TenetEval Eval);

    /// <summary>Literal parse of <c>Faith.Type</c> → <see cref="ReligionType"/>.</summary>
    public static ReligionType ParseType(Faith faith) => faith.Type switch
    {
        "Folk"      => Folk,
        "Organized" => Organized,
        "Cult"      => Cult,
        "Heresy"    => Heresy,
        _           => ReligionType.None,
    };

    public static readonly TenetEntry[] All =
    [
        // ═══════════════════════════════════════════════════════════════════════
        // Organized / Abrahamic-flavoured
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_aniconism",                      Institutional,                 (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_alexandrian_catechism",          Scholarly,                     (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_armed_pilgrimages",              Martial | Institutional,       (in FaithContext c) => c.Favoured(Organized | Cult)),
        new("tenet_communion",                      Institutional,                 (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_consolamentum",                  Ascetic,                       (in FaithContext c) => c.Favoured(Organized | Cult)),
        new("tenet_gnosticism",                     Occult | Scholarly,            (in FaithContext c) => c.Favoured(Cult)),
        new("tenet_mendicant_preachers",            Institutional | Ascetic,       (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_monasticism",                    Ascetic | Institutional,       (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_pentarchy",                      Institutional,                 (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_unrelenting_faith",              Martial,                       (in FaithContext c) => c.Favoured(Organized | Cult)),
        new("tenet_vows_of_poverty",                Ascetic,                       (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_adaptive",                       Institutional,                 (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_legalism",                       Institutional | Scholarly,     (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_literalism",                     Scholarly | Institutional,     (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_religious_legal_pronouncements", Institutional,                 (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_struggle_submission",            Martial,                       (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_false_conversion_sanction",      Occult,                        (in FaithContext c) => c.Favoured(Cult | Organized)),
        new("tenet_tax_nonbelievers",               Institutional,                 (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_asceticism",                     Ascetic | Dharmic,             (in FaithContext c) => c.Favoured(Organized | Cult)),
        new("tenet_communal_possessions",           Communal,                      (in FaithContext c) => c.Favoured(Organized | Folk)),
        new("tenet_pure_land",                      Dharmic | Ascetic,             (in FaithContext c) => c.Favoured(Organized | Cult)),
        new("tenet_no_mind",                        Dharmic | Ascetic,             (in FaithContext c) => c.Favoured(Cult | Organized)),
        new("tenet_pursuit_of_knowledge",           Scholarly,                     (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_benevolent_governance",          Institutional | Communal,      (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_filial_piety",                   Ancestral | Communal,          (in FaithContext c) => c.Favoured(Organized | Folk)),
        new("tenet_harmonious_society",             Communal | Institutional,      (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_preservation",                   Institutional | Communal,      (in FaithContext c) => c.Favoured(Organized | Folk)),

        // ═══════════════════════════════════════════════════════════════════════
        // Pacifism / militancy (exclusivity web is applied by the assigner, not here)
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_pacifism",               Pacific,            (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_dharmic_pacifism",       Pacific | Dharmic,  (in FaithContext c) => c.Favoured(Organized | Cult)),
        new("tenet_warmonger",              Martial,            (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_human_sacrifice",        Sacrificial,        (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_gruesome_festivals",     Sacrificial,        (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_sacrificial_ceremonies", Sacrificial,        (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_fp3_fedayeen",           Martial | Occult,   (in FaithContext c) => c.Favoured(Cult | Organized)),
        new("tenet_sacred_destruction",     Martial,            (in FaithContext c) => c.Favoured(Folk | Cult)),

        // ═══════════════════════════════════════════════════════════════════════
        // Folk / pagan / dharmic flavour
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_carnal_exaltation",   Hedonistic,             (in FaithContext c) => c.Favoured(Cult | Folk)),
        new("tenet_communal_identity",   Communal,               (in FaithContext c) => c.Favoured(Any)),
        new("tenet_divine_marriage",     Ancestral,              (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_rite",                Institutional,          (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_reincarnation",       Dharmic,                (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_inner_journey",       Dharmic | Ascetic,      (in FaithContext c) => c.Favoured(Cult | Folk)),
        new("tenet_ritual_hospitality",  Communal,               (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_esotericism",         Occult | Scholarly,     (in FaithContext c) => c.Favoured(Cult)),
        new("tenet_adorcism",            Occult | Nature,        (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_ancestor_worship",    Ancestral,              (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_astrology",           Occult | Scholarly,     (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_hedonistic",          Hedonistic,             (in FaithContext c) => c.Favoured(Cult | Folk)),
        new("tenet_mystical_birthright", Occult,                 (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_ritual_celebrations", Communal,               (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_sacred_childbirth",   Communal,               (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_bhakti",              Dharmic,                (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_household_gods",      Ancestral | Communal,   (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_exaltation_of_pain",  Sacrificial | Occult,   (in FaithContext c) => c.Favoured(Cult)),
        new("tenet_pursuit_of_power",    Martial | Occult,       (in FaithContext c) => c.Favoured(Cult)),
        new("tenet_ritual_cannibalism",  Sacrificial,            (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_sacred_shadows",      Occult,                 (in FaithContext c) => c.Favoured(Cult)),
        new("tenet_polyamory",           Hedonistic,             (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_extinction_of_dharma",Martial | Dharmic,      (in FaithContext c) => c.Favoured(Cult)),
        new("tenet_cranial_trophies",    Sacrificial | Martial,  (in FaithContext c) => c.Favoured(Folk | Cult)),

        // ═══════════════════════════════════════════════════════════════════════
        // Formerly terrain-gated (🗺️). Only cthonic_redoubts keeps terrain; the other
        // 7 become plain type-favoured entries (a desert faith may still revere nature).
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_pastoral_isolation",  Nature | Communal,  (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_sanctity_of_nature",  Nature,             (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_sun_worship",         Nature,             (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_cthonic_redoubts",    Nature,             (in FaithContext c) => c.TerrainAtLeast(CthonicThreshold, Mountains, DesertMountains)
                                                  * c.Favoured(Folk | Cult)),
        new("tenet_natural_primitivism", Nature | Ascetic,   (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_megaliths",           Nature,             (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_mountain_worship",    Nature,             (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_takamin",             Nature,             (in FaithContext c) => c.Favoured(Folk)),

        // ═══════════════════════════════════════════════════════════════════════
        // 💀 Syncretic duds — inert in a full conversion (target vanilla religions). Weight 0
        // always; kept in the pool so the zero self-documents WHY they never appear. Blank themes
        // in the CSV ⇒ Theme.None (no boost can ever fire on a dud).
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_sinitic_syncretism",    Theme.None, (in FaithContext c) => 0f),
        new("tenet_eastern_syncretism",    Theme.None, (in FaithContext c) => 0f),
        new("tenet_unreformed_syncretism", Theme.None, (in FaithContext c) => 0f),
        new("tenet_christian_syncretism",  Theme.None, (in FaithContext c) => 0f),
        new("tenet_islamic_syncretism",    Theme.None, (in FaithContext c) => 0f),
        new("tenet_jewish_syncretism",     Theme.None, (in FaithContext c) => 0f),
    ];

    // ── Central symmetric conflict graph ───────────────────────────────────────
    // Built once from the catalog's can_pick edges (the syncretism clique + the pacifism/militancy
    // web etc.) and symmetrised so A⊥B ⟺ B⊥A regardless of fill order. The assigner reads this;
    // no eval does. One table to diff against 30_core_tenets.txt.

    // Directed edges as authored in the catalog; ConflictGraph symmetrises them.
    private static readonly (string, string[])[] ConflictEdges =
    [
        ("tenet_armed_pilgrimages",      ["tenet_pacifism"]),
        ("tenet_communion",             ["tenet_sacred_shadows"]),
        ("tenet_consolamentum",         ["tenet_sacrificial_ceremonies"]),
        ("tenet_mendicant_preachers",   ["tenet_hedonistic"]),
        ("tenet_monasticism",           ["tenet_hedonistic"]),
        ("tenet_preservation",          ["tenet_sacred_destruction"]),
        ("tenet_pacifism",              ["tenet_human_sacrifice", "tenet_armed_pilgrimages", "tenet_gruesome_festivals",
                                         "tenet_warmonger", "tenet_sacrificial_ceremonies", "tenet_fp3_fedayeen",
                                         "tenet_sacred_destruction"]),
        ("tenet_dharmic_pacifism",      ["tenet_human_sacrifice", "tenet_gruesome_festivals", "tenet_sacrificial_ceremonies",
                                         "tenet_warmonger", "tenet_fp3_fedayeen", "tenet_sacred_destruction"]),
        ("tenet_human_sacrifice",       ["tenet_gruesome_festivals", "tenet_sacrificial_ceremonies"]),
        ("tenet_gruesome_festivals",    ["tenet_sacrificial_ceremonies"]),
    ];

    // The {6 syncretisms + gnosticism} clique: at most one may be chosen.
    private static readonly string[] SyncretismClique =
    [
        "tenet_sinitic_syncretism", "tenet_eastern_syncretism", "tenet_unreformed_syncretism",
        "tenet_christian_syncretism", "tenet_islamic_syncretism", "tenet_jewish_syncretism",
        "tenet_gnosticism",
    ];

    /// <summary>Symmetric conflict graph: <c>ConflictGraph[a].Contains(b) ⟺ ConflictGraph[b].Contains(a)</c>.
    /// Every tenet has an entry (empty set if it conflicts with nothing).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ConflictGraph = BuildConflictGraph();

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> BuildConflictGraph()
    {
        var g = new Dictionary<string, HashSet<string>>();
        foreach (var e in All) g[e.Name] = [];

        void Link(string a, string b)
        {
            if (a == b) return;
            g[a].Add(b);
            g[b].Add(a);
        }

        foreach (var (a, partners) in ConflictEdges)
            foreach (var b in partners)
                Link(a, b);

        // Clique: every member conflicts with every other member.
        for (int i = 0; i < SyncretismClique.Length; i++)
            for (int j = i + 1; j < SyncretismClique.Length; j++)
                Link(SyncretismClique[i], SyncretismClique[j]);

        return g.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value);
    }
}
