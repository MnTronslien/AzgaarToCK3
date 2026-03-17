namespace Converter.Lemur;

/// <summary>
/// Static data for non-DLC CK3 culture tradition keys.
/// Source: game/common/culture/traditions/ (CK3 v1.12.5)
/// </summary>
public static class TraditionData
{
    public record TraditionEntry(string Key, string Category);

    public static readonly TraditionEntry[] All =
    [
        new("tradition_hird",                       "warfare"),
        new("tradition_formation_fighting",          "warfare"),
        new("tradition_stand_and_fight",             "warfare"),  // was: tradition_shield_wall (doesn't exist)
        new("tradition_horse_lords",                 "warfare"),
        new("tradition_devoted_horsemanship",        "warfare"),  // was: tradition_mounted_warriors (doesn't exist)
        new("tradition_seafaring",                   "warfare"),  // was: tradition_longship_builders (doesn't exist)
        new("tradition_stalwart_defenders",          "warfare"),
        new("tradition_jungle_warriors",             "warfare"),
        new("tradition_desert_ribat",                "warfare"),  // was: tradition_desert_ribats (typo)
        new("tradition_warrior_culture",             "warfare"),
        new("tradition_hit_and_run",                 "warfare"),
        new("tradition_practiced_pirates",           "warfare"),  // was: tradition_fp1_varangians (doesn't exist)
        new("tradition_pastoralists",                "economy"),
        new("tradition_agrarian",                    "economy"),
        new("tradition_maritime_mercantilism",       "economy"),  // was: tradition_maritime_mercantile (wrong name)
        new("tradition_maritime_way_of_life",        "economy"),  // was: tradition_seafarers (wrong name)
        new("tradition_city_keepers",                "economy"),
        new("tradition_diasporic",                   "economy"),
        new("tradition_hard_working",                "economy"),  // was: tradition_industrious (doesn't exist)
        new("tradition_pacifism",                    "civic"),    // was: tradition_pacifist (wrong name)
        new("tradition_philosopher_culture",         "civic"),    // was: tradition_philosophers (wrong name)
        new("tradition_legalistic",                  "civic"),    // was: tradition_legal_codification (doesn't exist)
        new("tradition_equal_inheritance",           "civic"),    // was: tradition_independent_vassals (doesn't exist)
        new("tradition_chanson_de_geste",            "civic"),
        new("tradition_court_eunuchs",               "civic"),
        new("tradition_hereditary_hierarchy",        "civic"),
        new("tradition_poetry",                      "arts"),
        new("tradition_music_theory",                "arts"),     // was: tradition_music (wrong name)
        new("tradition_storytellers",                "arts"),
        new("tradition_runestones",                  "arts"),     // was: tradition_saga_writing (doesn't exist)
        new("tradition_culinary_art",                "arts"),
        new("tradition_brewery",                     "arts"),
        new("tradition_hunters",                     "societal"), // was: tradition_hunting (wrong name)
        new("tradition_welcoming",                   "societal"),
        new("tradition_forest_folk",                 "societal"),
        new("tradition_monastic_communities",        "societal"), // was: tradition_monasteries (wrong name)
        new("tradition_by_the_sword",                "societal"),
        new("tradition_mystical_ancestors",          "societal"), // was: tradition_sorcerers (doesn't exist)
        new("tradition_wedding_ceremonies",          "societal"), // was: tradition_rite_of_passage (doesn't exist)
        new("tradition_isolationist",                "societal"),
        new("tradition_ancient_miners",              "societal"),
    ];
}
