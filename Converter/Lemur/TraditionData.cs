using Converter.Lemur.Fields;
using Converter.Lemur.Provinces;

namespace Converter.Lemur;

/// <summary>
/// Static data for the CK3 culture tradition pool used by <c>CultureTraditionAssigner</c>.
///
/// <para><b>Inclusive pool.</b> It's a generated fantasy map — anything goes. Essentially every
/// real CK3 tradition is in the pool (incl. DLC and heritage/region-flavored ones). Treatment
/// decides <i>how</i> a tradition is weighted, never <i>whether</i> it is in:</para>
/// <list type="bullet">
/// <item><b>🗺️ terrain/coastal</b> — a <see cref="TerrainGate"/> lambda, hard {0,1}. Mechanically
/// terrain-tied, so a mismatch is excluded entirely.</item>
/// <item><b>🎭 flavor</b> — no terrain gate; baseline weight 1, only ethos-nudged. Available to
/// any culture. The heritage/culture/region-flavored traditions live here.</item>
/// <item><b>🔬 special</b> (<see cref="TraditionEntry.Special"/>) — the <c>is_shown=always no</c>
/// placeholders, held OUT pending a one-time functionality check.</item>
/// </list>
///
/// <para><b>Conventions:</b> terrain gate ∈ {0,1} (MVP cap 1); <see cref="TraditionEntry.FavouredEthos"/>
/// drives a soft ×penalty in the assigner (catalog "Compatible ethos" column — the ethoses that
/// avoid vanilla's <c>tradition_incompatible_ethos_penalty</c>); flavor traditions have no
/// <see cref="TraditionEntry.TerrainGate"/>.</para>
///
/// Source data: <c>CK3_TRADITIONS_CATALOG.md</c> (extracted from CK3 game files, incl. FP1–3,
/// EP1–3, tgp/mpo/ce1). MVP ignores DLC ownership — DLC traditions are pooled by their type.
/// </summary>
public static class TraditionData
{
    /// <summary>
    /// CK3's 7 ethos pillars as a flag set, matching <c>common/culture/pillars/00_ethos.txt</c>.
    /// </summary>
    [Flags]
    public enum Ethos
    {
        None         = 0,
        Bellicose    = 1,
        Stoic        = 2,
        Bureaucratic = 4,
        Spiritual    = 8,
        Courtly      = 16,
        Egalitarian  = 32,
        Communal     = 64,
        Any          = Bellicose | Stoic | Bureaucratic | Spiritual | Courtly | Egalitarian | Communal, // 127
    }

    public record TraditionEntry(
        string Key,
        string Category,
        Ethos FavouredEthos = Ethos.Any,       // catalog "Compatible ethos"; Any = no penalty
        TraditionWeight? TerrainGate = null,    // null = always terrain-eligible (weight 1)
        bool Special = false);                  // is_shown=always no placeholder — held out

    // ── ethos shorthands ────────────────────────────────────────────────────
    private const Ethos Bel = Ethos.Bellicose;
    private const Ethos Sto = Ethos.Stoic;
    private const Ethos Bur = Ethos.Bureaucratic;
    private const Ethos Spi = Ethos.Spiritual;
    private const Ethos Cou = Ethos.Courtly;
    private const Ethos Ega = Ethos.Egalitarian;
    private const Ethos Com = Ethos.Communal;
    private const Ethos AnyE = Ethos.Any;
    private const Ethos NotBel = AnyE & ~Bel; // "not bellicose" → all 6 others favoured

    // ── terrain gate thresholds ──────────────────────────────────────────────
    // Illustrative thresholds (tune later). A culture passes a gate if a meaningful slice of its
    // land is the gated terrain — not "majority", since terrain is mixed and the gate is the only
    // hard filter. Coastal needs a higher bar (vanilla uses ≥50% coastal for the strong ones).
    private const float TERR_THRESHOLD = 0.20f;
    private const float COAST_THRESHOLD = 0.20f;

    public static readonly TraditionEntry[] All =
    [
        // ═══════════════════════════════════════════════════════════════════════
        // Base game — Combat (00_combat_traditions.txt)
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_forest_fighters",        "warfare", Bel | Sto | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Forest, Ck3Terrain.Taiga)),
        new("tradition_mountaineers",           "warfare", Bel | Sto | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.DesertMountains)),
        new("tradition_warriors_of_the_dry",    "warfare", Bel | Sto | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Drylands, Ck3Terrain.Desert)),
        new("tradition_highland_warriors",      "warfare", Bel | Sto | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Hills)),
        new("tradition_jungle_warriors",        "warfare", Bel | Sto | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Jungle)),
        new("tradition_winter_warriors",        "warfare", Bel | Sto | Com),
        new("tradition_only_the_strong",        "warfare", Bel | Sto),
        new("tradition_quarrelsome",            "warfare", Bel | Spi),
        new("tradition_warriors_by_merit",      "warfare", Bel | Ega | Com),
        new("tradition_warrior_monks",          "warfare", Bel | Spi),
        new("tradition_talent_acquisition",     "warfare", Bel | Ega | Cou),
        new("tradition_strength_in_numbers",    "warfare", Bel | Spi),
        new("tradition_frugal_armorsmiths",     "warfare", Bel | Sto | Com),
        new("tradition_swords_for_hire",        "warfare", Bel | Cou | Com),
        new("tradition_reverence_for_veterans", "warfare", Bel | Sto | Ega),
        new("tradition_stalwart_defenders",     "warfare", Bel | Sto | Cou),
        new("tradition_battlefield_looters",    "warfare", Bel | Bur),
        new("tradition_hit_and_run",            "warfare", Bel | Ega | Spi),
        new("tradition_stand_and_fight",        "warfare", Bel | Spi | Sto),
        new("tradition_adaptive_skirmishing",   "warfare", Bel | Spi | Sto),
        new("tradition_formation_fighting",     "warfare", Bel | Cou | Com),
        new("tradition_horse_breeder",          "warfare", Bel | Sto | Com),
        new("tradition_longbow_competitions",   "warfare", Sto | Bur),
        new("tradition_malleable_invaders",     "warfare", Bel | Ega | Bur),

        // ═══════════════════════════════════════════════════════════════════════
        // Base game — Men-at-arms / regional MaA (00_maa_traditions.txt)
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_desert_ribat",               "warfare", Bel | Spi | Sto, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Drylands, Ck3Terrain.Desert)),
        new("tradition_horn_mountain_skirmishing",  "warfare", Spi | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.DesertMountains)),
        new("tradition_mubarizuns",                 "warfare", Bel | Sto | Com),
        new("tradition_garuda_warriors",            "warfare", Bel | Sto),
        new("tradition_mobile_guards",              "warfare", Com),
        new("tradition_bush_hunting",               "warfare", Com | Ega),
        new("tradition_hussar",                     "warfare", Sto),
        new("tradition_khadga_puja",                "warfare", Bel | Sto),
        new("tradition_hird",                       "warfare", Bel | Spi),
        new("tradition_futuwaa",                    "warfare", Bel | Cou | Com),
        new("tradition_land_of_the_bow",            "warfare", Bel | Spi),
        new("tradition_druzhina",                   "warfare", Bel | Sto),
        new("tradition_burman_royal_army",          "warfare", Spi),
        new("tradition_chanson_de_geste",           "warfare", Bel | Sto | Cou),
        new("tradition_strong_kinship",             "warfare", Bel | Sto | Bur),
        new("tradition_mountain_herding",           "warfare", Bel | Sto | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.DesertMountains)),
        new("tradition_forest_wardens",             "warfare", Bel | Sto | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Forest, Ck3Terrain.Taiga)),
        new("tradition_upland_skirmishing",         "warfare", Com | Ega, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Hills)),
        new("tradition_amharic_highlanders",        "warfare", Spi, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Hills)),
        new("tradition_polders",                    "warfare", Com | Bur | Sto, (in CultureContext c) => c.CoastalAtLeast(COAST_THRESHOLD)),
        new("tradition_caucasian_wolves",           "warfare", Bel | Sto | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.DesertMountains)),

        // ═══════════════════════════════════════════════════════════════════════
        // Base game — Realm (00_realm_traditions.txt)
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_agrarian",                "economy", NotBel, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Farmlands, Ck3Terrain.Floodplains)),
        new("tradition_pastoralists",            "economy", Bel | Sto | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Plains, Ck3Terrain.Steppe)),
        new("tradition_hill_dwellers",           "economy", NotBel, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Hills)),
        new("tradition_forest_folk",             "economy", NotBel, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Forest, Ck3Terrain.Taiga)),
        new("tradition_mountain_homes",          "economy", NotBel, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.DesertMountains)),
        new("tradition_dryland_dwellers",        "economy", NotBel, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Drylands, Ck3Terrain.Desert)),
        new("tradition_jungle_dwellers",         "economy", NotBel, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Jungle)),
        new("tradition_wetlanders",              "economy", NotBel, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Wetlands)),
        new("tradition_maritime_mercantilism",   "economy", Ega | Sto | Bur, (in CultureContext c) => c.CoastalAtLeast(COAST_THRESHOLD)),
        new("tradition_esteemed_hospitality",    "economy", Cou | Com | Spi),
        new("tradition_gardening",               "economy", Cou | Com | Spi),
        new("tradition_astute_diplomats",        "economy", NotBel),
        new("tradition_metal_craftsmanship",     "economy", Com | Bur | Sto),
        new("tradition_wedding_ceremonies",      "economy", Cou | Com | Spi),
        new("tradition_ruling_caste",            "economy", Spi | Cou),
        new("tradition_brewery",                 "economy", Spi),
        new("tradition_collective_lands",        "economy", Com | Ega | Sto),
        new("tradition_fervent_temple_builders", "economy", Com | Spi | Sto),
        new("tradition_monastic_communities",    "economy", Spi | Com),
        new("tradition_castle_keepers",          "economy", Bel | Sto | Bur),
        new("tradition_city_keepers",            "economy", Bur | Cou | Ega),
        new("tradition_legalistic",              "economy", Com | Ega | Cou),
        new("tradition_hereditary_hierarchy",    "economy", Cou | Spi),
        new("tradition_tribe_unity",             "economy", Com | Spi | Sto),
        new("tradition_court_eunuchs",           "economy", Cou | Com | Spi),
        new("tradition_family_entrepreneurship", "economy", Com | Cou),
        new("tradition_isolationist",            "economy", Com | Spi | Sto),
        new("tradition_parochialism",            "economy", Cou | Com | Spi),
        new("tradition_republican_legacy",       "economy", Cou | Com | Spi),
        new("tradition_roman_legacy",            "economy", Bel | Ega | Cou),
        new("tradition_female_only_inheritance", "economy", Ega),
        new("tradition_equal_inheritance",       "economy", Ega),
        new("tradition_culture_blending",        "economy", Com | Ega),
        new("tradition_staunch_traditionalists", "economy", Com | Spi | Sto),
        new("tradition_hidden_cities",           "economy", Bur, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Jungle)),
        new("tradition_ancient_miners",          "economy", AnyE, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.Hills, Ck3Terrain.DesertMountains), Special: true),

        // ═══════════════════════════════════════════════════════════════════════
        // Base game — Regional (00_regional_traditions.txt)
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_mountaineer_ruralism",    "economy", Sto, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.DesertMountains)),
        new("tradition_things",                  "civic", Bur | Bel),
        new("tradition_the_witenagemot",         "civic", Bur | Sto),
        new("tradition_horse_lords",             "warfare", Bel | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Steppe, Ck3Terrain.Plains)),
        new("tradition_steppe_tolerance",        "civic", Bel | Ega | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Steppe, Ck3Terrain.Plains)),
        new("tradition_saharan_nomads",          "economy", Spi | Sto, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Desert, Ck3Terrain.Drylands)),
        new("tradition_himalayan_settlers",      "economy", Spi | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.DesertMountains)),
        new("tradition_desert_nomads",           "economy", Spi | Sto, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Desert, Ck3Terrain.Drylands)),
        new("tradition_lords_of_the_elephant",   "warfare", Bel | Cou | Sto, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Jungle, Ck3Terrain.Drylands)),
        new("tradition_visigothic_codes",        "civic", Ega),
        new("tradition_african_tolerance",       "civic", Ega | Com),
        new("tradition_byzantine_succession",    "civic", Cou | Com),
        new("tradition_nubian_warrior_queens",   "civic", Ega),
        new("tradition_nubian_warrior_kings",    "civic", Ega),
        new("tradition_caravaneers",             "economy", Ega, Special: true),

        // ═══════════════════════════════════════════════════════════════════════
        // Base game — Ritual (00_ritual_traditions.txt)
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_sacred_mountains",        "ritual", Spi, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.DesertMountains)),
        new("tradition_sacred_groves",           "ritual", Spi, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Forest, Ck3Terrain.Taiga, Ck3Terrain.Jungle)),
        new("tradition_culinary_art",            "ritual", Cou | Com | Spi, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Farmlands, Ck3Terrain.Floodplains)),
        new("tradition_festivities",             "ritual", Cou | Com | Sto),
        new("tradition_faith_bound",             "ritual", Spi),
        new("tradition_medicinal_plants",        "ritual", Bur | Sto),
        new("tradition_language_scholars",       "ritual", Spi | Bur | Ega),
        new("tradition_sorcerous_metallurgy",    "ritual", Spi | Com),
        new("tradition_sacred_hunts",            "ritual", Bel | Spi | Sto),
        new("tradition_by_the_sword",            "ritual", Spi),
        new("tradition_religious_patronage",     "ritual", Cou | Com | Spi),
        new("tradition_religion_blending",       "ritual", Cou | Com | Spi),
        new("tradition_monogamous",              "ritual", AnyE),
        new("tradition_polygamous",              "ritual", AnyE),
        new("tradition_concubines",              "ritual", AnyE),
        new("tradition_merciful_blindings",      "ritual", Spi | Cou),
        new("tradition_runestones",              "ritual", Bel | Bur),
        new("tradition_mystical_ancestors",      "ritual", Spi),

        // ═══════════════════════════════════════════════════════════════════════
        // Base game — Societal (00_societal_traditions.txt)
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_seafaring",               "societal", Bel | Bur | Spi, (in CultureContext c) => c.CoastalAtLeast(COAST_THRESHOLD)),
        new("tradition_fishermen",               "societal", NotBel, (in CultureContext c) => c.CoastalAtLeast(COAST_THRESHOLD)),
        new("tradition_practiced_pirates",       "societal", Bel, (in CultureContext c) => c.CoastalAtLeast(COAST_THRESHOLD)),
        new("tradition_xenophilic",              "societal", Com | Ega),
        new("tradition_hard_working",            "societal", Bel | Sto | Com),
        new("tradition_loyal_soldiers",          "societal", Bel | Sto | Com),
        new("tradition_pacifism",                "societal", Ega | Spi),
        new("tradition_spartan",                 "societal", Sto | Com),
        new("tradition_hunters",                 "societal", Bel | Spi | Sto),
        new("tradition_mendicant_mystics",       "societal", Com | Spi),
        new("tradition_warrior_culture",         "societal", Bel | Spi),
        new("tradition_philosopher_culture",     "societal", Cou | Com | Spi),
        new("tradition_welcoming",               "societal", Com | Ega | Sto),
        new("tradition_eye_for_an_eye",          "societal", Com | Bel),
        new("tradition_zealous_people",          "societal", Spi | Com),
        new("tradition_forbearing",              "societal", Spi | Sto),
        new("tradition_equitable",               "societal", Ega | Sto),
        new("tradition_charitable",              "societal", Com | Spi),
        new("tradition_modest",                  "societal", Sto | Spi),
        new("tradition_life_is_just_a_joke",     "societal", Sto | Com),
        new("tradition_noble_adoption",          "societal", Cou | Com | Spi),
        new("tradition_storytellers",            "arts", Cou | Com | Sto),
        new("tradition_music_theory",            "arts", Cou | Com | Spi),
        new("tradition_poetry",                  "arts", Cou | Com | Spi),
        new("tradition_artisans",                "arts", Cou | Com | Spi),
        new("tradition_vegetarianism",           "societal", Com | Spi | Sto),
        new("tradition_chivalry",                "societal", Bel | Ega | Cou),
        new("tradition_martial_admiration",      "societal", Bel | Sto),
        new("tradition_diasporic",               "economy", AnyE, Special: true),

        // ═══════════════════════════════════════════════════════════════════════
        // DLC — FP1 Northern Lords (heritage gate dropped → flavor; coastal kept)
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_fp1_performative_honour", "warfare", Bel),
        new("tradition_fp1_northern_stories",    "arts", Bel | Bur),
        new("tradition_fp1_trials_by_combat",    "warfare", Bur | Sto | Cou),
        new("tradition_fp1_the_right_to_prove",  "civic", Bel | Com | Ega),
        new("tradition_fp1_coastal_warriors",    "warfare", Bel, (in CultureContext c) => c.CoastalAtLeast(COAST_THRESHOLD)),

        // ═══════════════════════════════════════════════════════════════════════
        // DLC — FP2 Fate of Iberia
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_fp2_state_ransoming",      "economy", Bel | Com),
        new("tradition_fp2_strategy_gamers",      "ritual", Bel | Cou),
        new("tradition_fp2_ritualised_friendship", "societal", AnyE),
        new("tradition_fp2_malleable_subjects",   "economy", Ega | Cou),

        // ═══════════════════════════════════════════════════════════════════════
        // DLC — FP3 Legacy of Persia
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_fp3_irrigation_experts",  "economy", Cou | Com | Ega, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Drylands, Ck3Terrain.Desert, Ck3Terrain.DesertMountains)),
        new("tradition_fp3_pragmatic_creed",     "warfare", Bel | Com | Ega | Sto, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.DesertMountains)),
        new("tradition_fp3_frontier_warriors",   "warfare", Bel | Sto, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Steppe, Ck3Terrain.Drylands)),
        new("tradition_fp3_beacon_of_learning",  "societal", Cou | Com | Spi),
        new("tradition_fp3_enlightened_magnates", "societal", Cou | Com | Spi),
        new("tradition_fp3_jirga",               "warfare", Com | Sto),
        new("tradition_fp3_fierce_independence",  "warfare", Bel | Com | Sto),

        // ═══════════════════════════════════════════════════════════════════════
        // DLC — EP2 Tours & Tournaments
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_ep2_avid_falconers",      "societal", Cou | Sto),

        // ═══════════════════════════════════════════════════════════════════════
        // DLC — EP3 Roads to Power (culture/gov gate dropped → flavor)
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_ep3_indomitable_azatani",      "warfare", Bel | Sto),
        new("tradition_ep3_audacious_cadets",         "warfare", Bel | Cou),
        new("tradition_ep3_imperial_tagmata",         "warfare", Bel | Bur),
        new("tradition_ep3_roman_ceremonies",         "ritual", Cou | Bur),
        new("tradition_ep3_palace_politics",          "civic", Cou | Bur),
        new("tradition_ep3_cultivated_sophistication", "arts", Cou | Com | Spi),

        // ═══════════════════════════════════════════════════════════════════════
        // DLC — tgp pack (East/SE-Asia; heritage gate dropped, terrain kept where present)
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_tgp_rice_cultivators",    "economy", Com | Bur | Sto, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.TerracedHills, Ck3Terrain.Hills, Ck3Terrain.Farmlands)),
        new("tradition_intensive_farming",       "economy", NotBel, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Farmlands, Ck3Terrain.Floodplains)),
        new("tradition_tgp_mountain_island",     "warfare", Bel | Sto, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Mountains, Ck3Terrain.DesertMountains)),
        new("tradition_maritime_way_of_life",    "economy", NotBel, (in CultureContext c) => c.CoastalAtLeast(COAST_THRESHOLD)),

        // ═══════════════════════════════════════════════════════════════════════
        // DLC — mpo pack (nomad/steppe; heritage/gov gate dropped → flavor; taiga kept)
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_mpo_iron_cavalry",            "warfare", Bel | Sto),
        new("tradition_mpo_wolves_of_the_deep_steppe", "warfare", Bel | Com),
        new("tradition_devoted_horsemanship",        "warfare", Bel | Sto | Com),
        new("tradition_mpo_northern_tribes",         "economy", Sto | Com, (in CultureContext c) => c.TerrainAtLeast(TERR_THRESHOLD, Ck3Terrain.Taiga)),

        // ═══════════════════════════════════════════════════════════════════════
        // DLC — ce1
        // ═══════════════════════════════════════════════════════════════════════
        new("tradition_ce1_ritual_washing",      "ritual", Cou | Com | Spi),
    ];
}
