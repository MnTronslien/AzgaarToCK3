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
        new("tradition_hird",                      "warfare"),
        new("tradition_formation_fighting",         "warfare"),
        new("tradition_shield_wall",                "warfare"),
        new("tradition_horse_lords",                "warfare"),
        new("tradition_mounted_warriors",           "warfare"),
        new("tradition_longship_builders",          "warfare"),
        new("tradition_stalwart_defenders",         "warfare"),
        new("tradition_jungle_warriors",            "warfare"),
        new("tradition_desert_ribats",              "warfare"),
        new("tradition_warrior_culture",            "warfare"),
        new("tradition_hit_and_run",                "warfare"),
        new("tradition_pastoralists",               "economy"),
        new("tradition_agrarian",                   "economy"),
        new("tradition_maritime_mercantile",        "economy"),
        new("tradition_seafarers",                  "economy"),
        new("tradition_city_keepers",               "economy"),
        new("tradition_diasporic",                  "economy"),
        new("tradition_industrious",                "economy"),
        new("tradition_pacifist",                   "civic"),
        new("tradition_philosophers",               "civic"),
        new("tradition_legal_codification",         "civic"),
        new("tradition_independent_vassals",        "civic"),
        new("tradition_chanson_de_geste",           "civic"),
        new("tradition_court_eunuchs",              "civic"),
        new("tradition_hereditary_hierarchy",       "civic"),
        new("tradition_poetry",                     "arts"),
        new("tradition_music",                      "arts"),
        new("tradition_storytellers",               "arts"),
        new("tradition_saga_writing",               "arts"),
        new("tradition_culinary_art",               "arts"),
        new("tradition_brewery",                    "arts"),
        new("tradition_hunting",                    "societal"),
        new("tradition_welcoming",                  "societal"),
        new("tradition_forest_folk",                "societal"),
        new("tradition_monasteries",                "societal"),
        new("tradition_by_the_sword",               "societal"),
        new("tradition_sorcerers",                  "societal"),
        new("tradition_rite_of_passage",            "societal"),
        new("tradition_isolationist",               "societal"),
        new("tradition_ancient_miners",             "societal"),
        new("tradition_fp1_varangians",             "warfare"),
    ];
}
