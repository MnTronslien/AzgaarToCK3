using Converter.Lemur.Fields;
using Converter.Lemur.Provinces;

namespace Converter.Lemur;

/// <summary>
/// Static data for the CK3 faith tenet pool used by <c>FaithTenetAssigner</c>. Faith twin of
/// <see cref="TraditionData"/>; replaces <c>DoctrineData.AllTenets</c> as the tenet source
/// (<c>DoctrineData.Groups</c> — the structural doctrines — stays where it is).
///
/// <para><b>Inclusive pool.</b> Every CK3 tenet is in the pool (incl. DLC). Treatment decides
/// <i>how</i> a tenet is weighted, never <i>whether</i> it is in. There are only TWO mechanical
/// hard-zeroes (no culture analog):</para>
/// <list type="bullet">
/// <item><b>💀 Dud</b> (<see cref="TenetEntry.Dud"/>) — the six <c>*_syncretism</c> tenets grant an
/// opinion bonus toward adherents of a specific <b>vanilla</b> religion family; a full-conversion
/// output has none → inert for everyone → permanent weight 0.</item>
/// <item><b>🚫 Exclusivity</b> (<see cref="TenetEntry.ExclusiveWith"/>) — vanilla <c>can_pick</c>
/// forbids certain co-selections. Decoded from <c>30_core_tenets.txt</c>; an overlapping graph, not
/// disjoint cliques, so it's a pairwise list applied symmetrically by the assigner.</item>
/// </list>
///
/// <para>Plus two soft/authored axes (the converter's generative taste — vanilla does NOT
/// terrain-bind tenets):</para>
/// <list type="bullet">
/// <item><b>🗺️ Terrain</b> — a <see cref="TerrainGate"/> lambda, hard {0,1}. Authored heuristic.</item>
/// <item><b>🎭 FaithKind</b> — <see cref="TenetEntry.Favoured"/> drives a soft ×penalty in the
/// assigner when the faith's kind (Folk/Organized/Cult + theism) is not favoured.</item>
/// </list>
///
/// Source data: <c>CK3_TENETS_CATALOG.md</c> (extracted from <c>30_core_tenets.txt</c>, CK3 1.19).
/// </summary>
public static class TenetData
{
    /// <summary>
    /// Faith kinds for the soft affinity penalty. The first three mirror Azgaar's <c>Faith.Type</c>
    /// (Folk = unreformed pagan, Organized, Cult); the theism pair is derived from the faith's
    /// assigned doctrines (<c>doctrine_polytheist</c>/<c>doctrine_monotheist</c>).
    /// </summary>
    [Flags]
    public enum FaithKind
    {
        None        = 0,
        Folk        = 1,
        Organized   = 2,
        Cult        = 4,
        Polytheist  = 8,
        Monotheist  = 16,
        Any         = Folk | Organized | Cult | Polytheist | Monotheist, // 31
    }

    public record TenetEntry(
        string Key,
        string Category,                          // grouping / log (mirror TraditionEntry.Category)
        FaithKind Favoured = FaithKind.Any,       // soft kind affinity; Any = no penalty
        TenetWeight? TerrainGate = null,          // null = always terrain-eligible (weight 1)
        string[]? ExclusiveWith = null,           // pairwise mutual exclusivity (from can_pick)
        bool Dud = false,                         // syncretic → permanent weight 0 (inert in our output)
        bool Special = false);                    // held-out (parity with TraditionEntry.Special)

    // ── faith-kind shorthands ────────────────────────────────────────────────
    private const FaithKind Folk = FaithKind.Folk;
    private const FaithKind Org  = FaithKind.Organized;
    private const FaithKind Cult = FaithKind.Cult;
    private const FaithKind Poly = FaithKind.Polytheist;
    private const FaithKind Mono = FaithKind.Monotheist;
    private const FaithKind AnyK = FaithKind.Any;

    // ── terrain gate helpers (same shape as TraditionData) ────────────────────
    private static TenetWeight Gate(float threshold, params Ck3Terrain[] terrains) =>
        (in FaithContext c) =>
        {
            float f = 0f;
            foreach (var t in terrains) f += c.TerrainFractionOf(t);
            return f >= threshold ? 1f : 0f;
        };

    private const float TERR_THRESHOLD = 0.20f;

    // ── mutual-exclusivity clique: the 6 syncretisms + gnosticism (≤1 may be chosen) ──
    // Each member lists the others; the assigner also applies it symmetrically.
    private static readonly string[] SyncretismClique =
    [
        "tenet_sinitic_syncretism", "tenet_eastern_syncretism", "tenet_unreformed_syncretism",
        "tenet_christian_syncretism", "tenet_islamic_syncretism", "tenet_jewish_syncretism",
        "tenet_gnosticism",
    ];

    public static readonly TenetEntry[] All =
    [
        // ═══════════════════════════════════════════════════════════════════════
        // Organized / Abrahamic-flavoured
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_aniconism",                      "organized", Org | Mono),
        new("tenet_alexandrian_catechism",          "organized", Org),
        new("tenet_armed_pilgrimages",              "militant",  Org | Cult, ExclusiveWith: ["tenet_pacifism"]),
        new("tenet_communion",                      "organized", Org, ExclusiveWith: ["tenet_sacred_shadows"]),
        new("tenet_consolamentum",                  "organized", Org | Cult, ExclusiveWith: ["tenet_sacrificial_ceremonies"]),
        new("tenet_gnosticism",                     "esoteric",  Cult, ExclusiveWith: SyncretismClique),
        new("tenet_mendicant_preachers",            "organized", Org, ExclusiveWith: ["tenet_hedonistic"]),
        new("tenet_monasticism",                    "organized", Org, ExclusiveWith: ["tenet_hedonistic"]),
        new("tenet_pentarchy",                      "organized", Org),
        new("tenet_unrelenting_faith",              "militant",  Org | Cult),
        new("tenet_vows_of_poverty",                "organized", Org),
        new("tenet_adaptive",                       "organized", Org | Mono),
        new("tenet_legalism",                       "organized", Org),
        new("tenet_literalism",                     "organized", Org | Mono),
        new("tenet_religious_legal_pronouncements", "organized", Org),
        new("tenet_struggle_submission",            "militant",  Org | Mono),
        new("tenet_false_conversion_sanction",      "esoteric",  Cult | Org),
        new("tenet_tax_nonbelievers",               "organized", Org),
        new("tenet_asceticism",                     "organized", Org | Cult),
        new("tenet_communal_possessions",           "organized", Org | Folk),
        new("tenet_pure_land",                      "organized", Org | Cult),
        new("tenet_no_mind",                        "organized", Cult | Org),
        new("tenet_pursuit_of_knowledge",           "organized", Org),
        new("tenet_benevolent_governance",          "organized", Org),
        new("tenet_filial_piety",                   "organized", Org | Folk),
        new("tenet_harmonious_society",             "organized", Org),
        new("tenet_preservation",                   "organized", Org | Folk, ExclusiveWith: ["tenet_sacred_destruction"]),

        // ═══════════════════════════════════════════════════════════════════════
        // Pacifism / militancy (the big exclusivity web)
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_pacifism",            "pacific",  Org,
            ExclusiveWith: ["tenet_human_sacrifice", "tenet_armed_pilgrimages", "tenet_gruesome_festivals",
                            "tenet_warmonger", "tenet_sacrificial_ceremonies", "tenet_fp3_fedayeen",
                            "tenet_sacred_destruction"]),
        new("tenet_dharmic_pacifism",    "pacific",  Org | Cult,
            ExclusiveWith: ["tenet_human_sacrifice", "tenet_gruesome_festivals", "tenet_sacrificial_ceremonies",
                            "tenet_warmonger", "tenet_fp3_fedayeen", "tenet_sacred_destruction"]),
        new("tenet_warmonger",           "militant", Folk | Cult,
            ExclusiveWith: ["tenet_pacifism", "tenet_dharmic_pacifism"]),
        new("tenet_human_sacrifice",     "militant", Folk | Cult | Poly,
            ExclusiveWith: ["tenet_pacifism", "tenet_dharmic_pacifism", "tenet_gruesome_festivals", "tenet_sacrificial_ceremonies"]),
        new("tenet_gruesome_festivals",  "militant", Folk | Cult | Poly,
            ExclusiveWith: ["tenet_pacifism", "tenet_dharmic_pacifism", "tenet_human_sacrifice", "tenet_sacrificial_ceremonies"]),
        new("tenet_sacrificial_ceremonies", "militant", Folk | Cult | Poly,
            ExclusiveWith: ["tenet_pacifism", "tenet_dharmic_pacifism", "tenet_gruesome_festivals", "tenet_human_sacrifice", "tenet_consolamentum"]),
        new("tenet_fp3_fedayeen",        "militant", Cult | Org,
            ExclusiveWith: ["tenet_pacifism", "tenet_dharmic_pacifism"]),
        new("tenet_sacred_destruction",  "militant", Folk | Cult,
            ExclusiveWith: ["tenet_pacifism", "tenet_dharmic_pacifism", "tenet_preservation"]),

        // ═══════════════════════════════════════════════════════════════════════
        // Folk / pagan / dharmic flavour
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_carnal_exaltation",   "ritual",   Cult | Folk),
        new("tenet_communal_identity",   "social",   AnyK),
        new("tenet_divine_marriage",     "ritual",   Folk | Cult),
        new("tenet_rite",                "folk",     Folk),
        new("tenet_reincarnation",       "dharmic",  Folk | Cult),
        new("tenet_inner_journey",       "dharmic",  Cult | Folk),
        new("tenet_ritual_hospitality",  "social",   Folk),
        new("tenet_esotericism",         "esoteric", Cult),
        new("tenet_adorcism",            "esoteric", Folk | Cult),
        new("tenet_ancestor_worship",    "folk",     Folk | Poly),
        new("tenet_astrology",           "esoteric", Folk | Cult | Poly),
        new("tenet_hedonistic",          "ritual",   Cult | Folk, ExclusiveWith: ["tenet_monasticism", "tenet_mendicant_preachers"]),
        new("tenet_mystical_birthright", "esoteric", Folk | Cult),
        new("tenet_ritual_celebrations", "ritual",   Folk),
        new("tenet_sacred_childbirth",   "ritual",   Folk),
        new("tenet_bhakti",              "dharmic",  Folk | Cult | Poly),
        new("tenet_household_gods",      "folk",     Folk | Poly),
        new("tenet_exaltation_of_pain",  "esoteric", Cult),
        new("tenet_pursuit_of_power",    "esoteric", Cult),
        new("tenet_ritual_cannibalism",  "ritual",   Folk | Cult | Poly),
        new("tenet_sacred_shadows",      "esoteric", Cult, ExclusiveWith: ["tenet_communion"]),
        new("tenet_polyamory",           "social",   Folk | Cult),
        new("tenet_extinction_of_dharma", "dharmic",  Cult),
        new("tenet_cranial_trophies",    "militant", Folk | Cult | Poly),

        // ═══════════════════════════════════════════════════════════════════════
        // Terrain-gated (🗺️ authored heuristic — vanilla has no terrain gate on tenets)
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_pastoral_isolation",  "folk", Folk, Gate(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.DesertMountains, Ck3Terrain.Hills, Ck3Terrain.Steppe)),
        new("tenet_sanctity_of_nature",  "folk", Folk | Poly, Gate(TERR_THRESHOLD, Ck3Terrain.Forest, Ck3Terrain.Taiga, Ck3Terrain.Jungle, Ck3Terrain.Wetlands)),
        new("tenet_sun_worship",         "folk", Folk | Poly, Gate(TERR_THRESHOLD, Ck3Terrain.Desert, Ck3Terrain.Drylands, Ck3Terrain.DesertMountains)),
        new("tenet_cthonic_redoubts",    "folk", Folk | Cult, Gate(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.DesertMountains)),
        new("tenet_natural_primitivism", "folk", Folk | Poly, Gate(TERR_THRESHOLD, Ck3Terrain.Forest, Ck3Terrain.Taiga, Ck3Terrain.Jungle)),
        new("tenet_megaliths",           "folk", Folk | Poly, Gate(TERR_THRESHOLD, Ck3Terrain.Hills)),
        new("tenet_mountain_worship",    "folk", Folk | Poly, Gate(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.DesertMountains)),
        new("tenet_takamin",             "folk", Folk | Poly, Gate(TERR_THRESHOLD, Ck3Terrain.Taiga)),

        // ═══════════════════════════════════════════════════════════════════════
        // 💀 Syncretic duds — weight 0 always; mutually exclusive (clique). Kept in the
        // pool (inclusive) so the dud-zero self-documents WHY they never appear.
        // ═══════════════════════════════════════════════════════════════════════
        new("tenet_sinitic_syncretism",    "syncretic", AnyK, ExclusiveWith: SyncretismClique, Dud: true),
        new("tenet_eastern_syncretism",    "syncretic", AnyK, ExclusiveWith: SyncretismClique, Dud: true),
        new("tenet_unreformed_syncretism", "syncretic", AnyK, ExclusiveWith: SyncretismClique, Dud: true),
        new("tenet_christian_syncretism",  "syncretic", AnyK, ExclusiveWith: SyncretismClique, Dud: true),
        new("tenet_islamic_syncretism",    "syncretic", AnyK, ExclusiveWith: SyncretismClique, Dud: true),
        new("tenet_jewish_syncretism",     "syncretic", AnyK, ExclusiveWith: SyncretismClique, Dud: true),
    ];
}
