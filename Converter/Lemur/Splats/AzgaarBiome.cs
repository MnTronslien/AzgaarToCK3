namespace Converter.Lemur.Splats;

// Azgaar's 12 biomes, as serialised in Cell.Biome (int 1..12).
// Stable across Azgaar versions; we hand-maintain this enum, no generation needed.
//
// "Wetland" is what Azgaar calls swamp/marsh terrain — same biome, two everyday names.
public enum AzgaarBiome : byte
{
    None = 0,                          // unmapped / sea / not set
    HotDesert = 1,
    ColdDesert = 2,
    Savanna = 3,
    Grassland = 4,
    TropicalSeasonalForest = 5,
    TemperateDeciduousForest = 6,
    TropicalRainforest = 7,
    TemperateRainforest = 8,
    Taiga = 9,
    Tundra = 10,
    Glacier = 11,
    Wetland = 12,                      // "Wetland" = swamp in everyday terms
}
