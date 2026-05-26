using Converter.Lemur.Splats;

namespace Converter.Lemur.Provinces;

// Score scale: 0..1 natural range for biome rules (pure biome → 1.0). Steepness rules
// (Hills, Mountains, DesertMountains) may overshoot above 1.0 so dominant relief wins
// over biome. Order in the list breaks ties.
public static class TerrainRegistry
{
    public const float FLOODPLAIN_POPDENSITY_MIN = 5.0f;
    public const float FARMLAND_POPDENSITY_MIN   = 8.0f;

    private static float BandFraction(IReadOnlyList<float> roughnesses, float low, float high, float max)
    {
        int n = roughnesses.Count;
        if (n == 0) return 0f;
        int hits = 0;
        for (int i = 0; i < n; i++)
            if (roughnesses[i] >= low && roughnesses[i] < high) hits++;
        return (hits / (float)n) * max;
    }

    public static readonly IReadOnlyList<TerrainCandidate> All = new TerrainCandidate[]
    {
        new(Ck3Terrain.Wetlands, (in BaronyContext ctx) =>
            ctx.BiomeFractionOf(AzgaarBiome.Wetland)),

        new(Ck3Terrain.Jungle, (in BaronyContext ctx) =>
            ctx.BiomeFractionOf(AzgaarBiome.TropicalRainforest)),

        new(Ck3Terrain.Forest, (in BaronyContext ctx) =>
            ctx.BiomeFractionOf(AzgaarBiome.TemperateDeciduousForest)
          + ctx.BiomeFractionOf(AzgaarBiome.TemperateRainforest)),

        // Taiga absorbs Tundra and Glacier — no CK3 terrain for either. Mountains overshoots
        // when a Glacier zone has enough mountain-grade cells.
        new(Ck3Terrain.Taiga, (in BaronyContext ctx) =>
            ctx.BiomeFractionOf(AzgaarBiome.Taiga)
          + ctx.BiomeFractionOf(AzgaarBiome.Tundra)
          + ctx.BiomeFractionOf(AzgaarBiome.Glacier)),

        new(Ck3Terrain.Desert, (in BaronyContext ctx) =>
            ctx.BiomeFractionOf(AzgaarBiome.HotDesert)),

        // ColdDesert covers both Gobi-style desert and Kazakh-style cold steppe; we bias
        // toward Steppe. Flip the terrain here to Ck3Terrain.Desert if a map needs the other.
        new(Ck3Terrain.Steppe, (in BaronyContext ctx) =>
            ctx.BiomeFractionOf(AzgaarBiome.ColdDesert)),

        new(Ck3Terrain.Drylands, (in BaronyContext ctx) =>
            ctx.BiomeFractionOf(AzgaarBiome.Savanna)
          + ctx.BiomeFractionOf(AzgaarBiome.TropicalSeasonalForest)),

        new(Ck3Terrain.Plains, (in BaronyContext ctx) =>
            ctx.BiomeFractionOf(AzgaarBiome.Grassland)),

        // Conditional gates on top of Grassland. Flat scores chosen above 1.0 to beat the
        // pure-Grassland Plains score; Floodplains above Farmlands because the river+pop
        // pair is a stronger signal than population alone.
        new(Ck3Terrain.Floodplains, (in BaronyContext ctx) =>
        {
            if (ctx.DominantBiome != AzgaarBiome.Grassland) return 0f;
            if (!ctx.RiverAdjacent) return 0f;
            if (ctx.PopDensity < FLOODPLAIN_POPDENSITY_MIN) return 0f;
            return 1.5f;
        }),

        new(Ck3Terrain.Farmlands, (in BaronyContext ctx) =>
        {
            if (ctx.DominantBiome != AzgaarBiome.Grassland) return 0f;
            if (ctx.PopDensity < FARMLAND_POPDENSITY_MIN) return 0f;
            return 1.2f;
        }),

        // DesertMountains uses identical band+max as Mountains; biome gate makes it win
        // over Mountains in arid biomes via registry order on score tie.
        new(Ck3Terrain.DesertMountains, (in BaronyContext ctx) =>
        {
            if (ctx.DominantBiome != AzgaarBiome.HotDesert
             && ctx.DominantBiome != AzgaarBiome.ColdDesert) return 0f;
            const float bandLow = 0.65f, bandHigh = float.PositiveInfinity, max = 2.0f;
            return BandFraction(ctx.CellRoughnesses, bandLow, bandHigh, max);
        }),

        // max=2.0 → crossover vs pure biome (1.0) at 50% mountain cells.
        new(Ck3Terrain.Mountains, (in BaronyContext ctx) =>
        {
            const float bandLow = 0.65f, bandHigh = float.PositiveInfinity, max = 2.0f;
            return BandFraction(ctx.CellRoughnesses, bandLow, bandHigh, max);
        }),

        // bandLow=0.10 catches subtle rolling relief (typical cell roughness sits 0.10–0.30
        // given p95-normalised mean ~0.14). max=1.5 → crossover vs pure biome (1.0) at 67%
        // hill cells. No DesertHills counterpart — CK3 has no such terrain.
        new(Ck3Terrain.Hills, (in BaronyContext ctx) =>
        {
            const float bandLow = 0.10f, bandHigh = 0.65f, max = 1.5f;
            return BandFraction(ctx.CellRoughnesses, bandLow, bandHigh, max);
        }),

        // Parked: the HotDesert + river-adjacent signal over-fires once minor rivers count
        // for adjacency. Real oases are tiny isolated wet spots, not "every desert near a river."
        new(Ck3Terrain.Oasis, (in BaronyContext _) => 0f),

        // No Azgaar signal cleanly maps. Kept so adding a rule later is a one-line edit.
        new(Ck3Terrain.TerracedHills, (in BaronyContext _) => 0f),
    };
}
