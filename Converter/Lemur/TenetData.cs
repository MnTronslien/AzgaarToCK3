using Converter.Lemur.Entities;
using Converter.Lemur.Fields;
using Converter.Lemur.Provinces;
using static Converter.Lemur.TenetData.FaithType;
using static Converter.Lemur.Provinces.Ck3Terrain;

namespace Converter.Lemur;

/// <summary>
/// Static data for the CK3 faith tenet pool. Collapsed to first principles, mirroring the splatmap's
/// <c>Material(name, EvaluateRule)</c> shape: every tenet is a <see cref="TenetEntry"/> = a
/// <b>name</b> + one <b>eval lambda</b> returning a weight in <c>[0, 1]</c>. All authored taste
/// (type affinity, the lone terrain tenet, the syncretic duds) lives inside the lambda.
///
/// <para>Two things are deliberately NOT in the lambdas:</para>
/// <list type="bullet">
/// <item><b>Mutual exclusivity</b> — a universal rule, applied implicitly by the assigner against the
/// central symmetric <see cref="ConflictGraph"/> (built once from the catalog's <c>can_pick</c>
/// edges). No eval contains a conflict check.</item>
/// <item><b>Theism (Poly/Mono)</b> — dropped entirely: no tenet's <c>can_pick</c> gates on it.</item>
/// </list>
///
/// Source data: <c>CK3_TENETS_CATALOG.md</c> (extracted from <c>30_core_tenets.txt</c>, CK3 1.19).
/// </summary>
public static class TenetData
{
    /// <summary>The one soft magnitude: an unfavoured-type tenet keeps this fraction of its weight.</summary>
    public const float KindPenalty = 0.3f;

    /// <summary>The lone terrain tenet's threshold (cthonic_redoubts; mountains + desert mountains).</summary>
    public const float CthonicThreshold = 0.20f;

    /// <summary>
    /// A faith's type, 1:1 with Azgaar's <c>Faith.Type</c>. <c>[Flags]</c> so a tenet's favoured
    /// <i>set</i> is expressible (<c>Folk | Cult</c>); a faith's own type is a single value.
    /// <b><see cref="Heresy"/> is a first-class peer</b> — equal standing with Folk/Organized/Cult,
    /// no parent/derivation resolution.
    /// </summary>
    [Flags]
    public enum FaithType
    {
        None      = 0,
        Folk      = 1,
        Organized = 2,
        Cult      = 4,
        Heresy    = 8,
        Any       = 15,
    }

    /// <summary>Hand-rolled <c>in</c>-by-ref delegate (mirrors the splatmap's <c>EvaluateRule</c>;
    /// avoids copying the struct). Returns a weight in <c>[0, 1]</c>.</summary>
    public delegate float TenetEval(in FaithContext ctx);

    /// <summary>A tenet: its key + one eval lambda. Nothing else.</summary>
    public record TenetEntry(string Name, TenetEval Eval);

    /// <summary>Literal parse of <c>Faith.Type</c> → <see cref="FaithType"/>. Heresy is its own peer
    /// value; there is no parent resolution.</summary>
    public static FaithType ParseType(Faith faith) => faith.Type switch
    {
        "Folk"      => Folk,
        "Organized" => Organized,
        "Cult"      => Cult,
        "Heresy"    => Heresy,
        _           => None,
    };

    public static readonly TenetEntry[] All =
    [
        // ═══════════════════════════════════════════════════════════════════════
        // Organized / Abrahamic-flavoured
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_aniconism",                      (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_alexandrian_catechism",          (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_armed_pilgrimages",              (in FaithContext c) => c.Favoured(Organized | Cult)),
        new("tenet_communion",                      (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_consolamentum",                  (in FaithContext c) => c.Favoured(Organized | Cult)),
        new("tenet_gnosticism",                     (in FaithContext c) => c.Favoured(Cult)),
        new("tenet_mendicant_preachers",            (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_monasticism",                    (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_pentarchy",                      (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_unrelenting_faith",              (in FaithContext c) => c.Favoured(Organized | Cult)),
        new("tenet_vows_of_poverty",                (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_adaptive",                       (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_legalism",                       (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_literalism",                     (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_religious_legal_pronouncements", (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_struggle_submission",            (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_false_conversion_sanction",      (in FaithContext c) => c.Favoured(Cult | Organized)),
        new("tenet_tax_nonbelievers",               (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_asceticism",                     (in FaithContext c) => c.Favoured(Organized | Cult)),
        new("tenet_communal_possessions",           (in FaithContext c) => c.Favoured(Organized | Folk)),
        new("tenet_pure_land",                      (in FaithContext c) => c.Favoured(Organized | Cult)),
        new("tenet_no_mind",                        (in FaithContext c) => c.Favoured(Cult | Organized)),
        new("tenet_pursuit_of_knowledge",           (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_benevolent_governance",          (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_filial_piety",                   (in FaithContext c) => c.Favoured(Organized | Folk)),
        new("tenet_harmonious_society",             (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_preservation",                   (in FaithContext c) => c.Favoured(Organized | Folk)),

        // ═══════════════════════════════════════════════════════════════════════
        // Pacifism / militancy (exclusivity web is applied by the assigner, not here)
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_pacifism",               (in FaithContext c) => c.Favoured(Organized)),
        new("tenet_dharmic_pacifism",       (in FaithContext c) => c.Favoured(Organized | Cult)),
        new("tenet_warmonger",              (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_human_sacrifice",        (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_gruesome_festivals",     (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_sacrificial_ceremonies", (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_fp3_fedayeen",           (in FaithContext c) => c.Favoured(Cult | Organized)),
        new("tenet_sacred_destruction",     (in FaithContext c) => c.Favoured(Folk | Cult)),

        // ═══════════════════════════════════════════════════════════════════════
        // Folk / pagan / dharmic flavour
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_carnal_exaltation",   (in FaithContext c) => c.Favoured(Cult | Folk)),
        new("tenet_communal_identity",   (in FaithContext c) => c.Favoured(Any)),
        new("tenet_divine_marriage",     (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_rite",                (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_reincarnation",       (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_inner_journey",       (in FaithContext c) => c.Favoured(Cult | Folk)),
        new("tenet_ritual_hospitality",  (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_esotericism",         (in FaithContext c) => c.Favoured(Cult)),
        new("tenet_adorcism",            (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_ancestor_worship",    (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_astrology",           (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_hedonistic",          (in FaithContext c) => c.Favoured(Cult | Folk)),
        new("tenet_mystical_birthright", (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_ritual_celebrations", (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_sacred_childbirth",   (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_bhakti",              (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_household_gods",      (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_exaltation_of_pain",  (in FaithContext c) => c.Favoured(Cult)),
        new("tenet_pursuit_of_power",    (in FaithContext c) => c.Favoured(Cult)),
        new("tenet_ritual_cannibalism",  (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_sacred_shadows",      (in FaithContext c) => c.Favoured(Cult)),
        new("tenet_polyamory",           (in FaithContext c) => c.Favoured(Folk | Cult)),
        new("tenet_extinction_of_dharma",(in FaithContext c) => c.Favoured(Cult)),
        new("tenet_cranial_trophies",    (in FaithContext c) => c.Favoured(Folk | Cult)),

        // ═══════════════════════════════════════════════════════════════════════
        // Formerly terrain-gated (🗺️). Only cthonic_redoubts keeps terrain; the other
        // 7 become plain type-favoured entries (a desert faith may still revere nature).
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_pastoral_isolation",  (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_sanctity_of_nature",  (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_sun_worship",         (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_cthonic_redoubts",    (in FaithContext c) => c.TerrainAtLeast(CthonicThreshold, Mountains, DesertMountains)
                                                  * c.Favoured(Folk | Cult)),
        new("tenet_natural_primitivism", (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_megaliths",           (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_mountain_worship",    (in FaithContext c) => c.Favoured(Folk)),
        new("tenet_takamin",             (in FaithContext c) => c.Favoured(Folk)),

        // ═══════════════════════════════════════════════════════════════════════
        // 💀 Syncretic duds — inert in a full conversion (target vanilla religions). Weight 0
        // always; kept in the pool so the zero self-documents WHY they never appear.
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_sinitic_syncretism",    (in FaithContext c) => 0f),
        new("tenet_eastern_syncretism",    (in FaithContext c) => 0f),
        new("tenet_unreformed_syncretism", (in FaithContext c) => 0f),
        new("tenet_christian_syncretism",  (in FaithContext c) => 0f),
        new("tenet_islamic_syncretism",    (in FaithContext c) => 0f),
        new("tenet_jewish_syncretism",     (in FaithContext c) => 0f),
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
