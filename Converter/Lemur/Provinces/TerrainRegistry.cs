using Converter.Lemur.Splats;

namespace Converter.Lemur.Provinces;

// The one place all CK3-terrain-selection rules live. Each entry declares one Ck3Terrain
// value + a lambda that scores how well it matches a barony's context. BaronyTerrainAssigner
// evaluates all entries against each barony and picks the top-1 (argmax) as Barony.Ck3Terrain.
//
// All rules are pure functions of BaronyContext. No I/O, no shared state.
//
// Score scale: 0..1 is the natural range. A score of 1.0 means a "very confident match"
// (under the current biome-fraction model that happens only when 100 % of cells belong to a
// rule's biome set). Values above 1.0 are reserved for the steepness rules (Hills, Mountains,
// DesertMountains) where geometry should overrule biome in genuinely obvious cases — see
// each lambda for its `max` and the crossover that implies.
//
// Order in the list breaks ties. With biome scores capped at 1.0, ties are rare in practice;
// for the steepness overshoot rules ordering still matters (DesertMountains before Mountains
// so the arid biome gate wins in deserts when both reach the same score).
public static class TerrainRegistry
{
    // Population thresholds — placeholders pending in-game tuning. PopDensity is
    // burg.Population / cellCount; raw Population is the unscaled burg field from
    // Azgaar (typically 0..~100 for a large city).
    public const float FLOODPLAIN_POPDENSITY_MIN = 5.0f;
    public const float FARMLAND_POPDENSITY_MIN   = 8.0f;

    // Helper for band-based scoring: fraction of cells whose roughness falls in [low, high),
    // multiplied by `max`. 0 cells in the band → 0 score; all cells in band → `max` score.
    // Linear in-between. Used by Hills / Mountains / DesertMountains.
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
        // ── Biome rules — score = sum of relevant BiomeFractions, capped naturally at 1.0 ──
        // Each Azgaar biome is mapped to exactly one CK3 terrain to avoid double-counting.
        // Multi-biome targets (Forest, Taiga, Drylands) sum the contributing fractions.

        new(Ck3Terrain.Wetlands, (in BaronyContext ctx) =>
            ctx.BiomeFractionOf(AzgaarBiome.Wetland)),

        new(Ck3Terrain.Jungle, (in BaronyContext ctx) =>
            ctx.BiomeFractionOf(AzgaarBiome.TropicalRainforest)),

        new(Ck3Terrain.Forest, (in BaronyContext ctx) =>
            // Temperate Deciduous = leaves drop seasonally (pre-clearance Europe, eastern US,
            // East Asia). Temperate Rainforest = high-precipitation cool evergreen (Pacific
            // Northwest, southern Chile, NZ). Both unambiguously CK3 "Forest."
            ctx.BiomeFractionOf(AzgaarBiome.TemperateDeciduousForest)
          + ctx.BiomeFractionOf(AzgaarBiome.TemperateRainforest)),

        new(Ck3Terrain.Taiga, (in BaronyContext ctx) =>
            // Taiga absorbs Tundra (CK3 has no "tundra" terrain) and flat Glacier (no glacier
            // terrain either — Mountains rule overshoots whenever the Glacier zone is steep).
            ctx.BiomeFractionOf(AzgaarBiome.Taiga)
          + ctx.BiomeFractionOf(AzgaarBiome.Tundra)
          + ctx.BiomeFractionOf(AzgaarBiome.Glacier)),

        new(Ck3Terrain.Desert, (in BaronyContext ctx) =>
            ctx.BiomeFractionOf(AzgaarBiome.HotDesert)),

        new(Ck3Terrain.Steppe, (in BaronyContext ctx) =>
            // ColdDesert covers both genuine cold deserts (Gobi) and cold steppes
            // (Kazakhstan). We bias the entire biome toward Steppe — cavalry country,
            // horse-archer cultural match. Flip to Ck3Terrain.Desert here if a map
            // needs the Gobi-style reading instead.
            ctx.BiomeFractionOf(AzgaarBiome.ColdDesert)),

        new(Ck3Terrain.Drylands, (in BaronyContext ctx) =>
            // Savanna = warm-dry sparse vegetation; TropicalSeasonalForest = monsoon
            // dry-deciduous scrub. Both read closer to drylands than to forest or desert.
            ctx.BiomeFractionOf(AzgaarBiome.Savanna)
          + ctx.BiomeFractionOf(AzgaarBiome.TropicalSeasonalForest)),

        new(Ck3Terrain.Plains, (in BaronyContext ctx) =>
            // Plains is unified with the other biome rules — scored on Grassland fraction
            // rather than a hardcoded fallback. Pure Grassland → 1.0, same scale as Forest /
            // Desert / etc. Non-Grassland baronies score 0 here; the corresponding biome
            // rule (or steepness overshoot) wins instead.
            ctx.BiomeFractionOf(AzgaarBiome.Grassland)),

        // ── Conditional gates on top of Grassland ─────────────────────────────────
        // Both must beat the Grassland → Plains 1.0 score, so flat values above 1.0.
        // Floodplains > Farmlands because the river-adjacency + population pair is a
        // stronger geographic signal than population alone.

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

        // ── Steepness rules — band × max, with deliberate overshoot above 1.0 ────
        // Mountains/DesertMountains use IDENTICAL band+max so they tie when both fire;
        // registry order places DesertMountains first so its biome gate wins in arid.

        new(Ck3Terrain.DesertMountains, (in BaronyContext ctx) =>
        {
            if (ctx.DominantBiome != AzgaarBiome.HotDesert
             && ctx.DominantBiome != AzgaarBiome.ColdDesert) return 0f;
            const float bandLow = 0.65f, bandHigh = float.PositiveInfinity, max = 2.0f;
            return BandFraction(ctx.CellRoughnesses, bandLow, bandHigh, max);
        }),

        new(Ck3Terrain.Mountains, (in BaronyContext ctx) =>
        {
            // max = 2.0. Crossover vs a pure biome (score 1.0) is 50 % mountain-grade
            // cells — a barony with half mountain cells reads as Mountains regardless
            // of biome cover. Strong claim; deliberately so. To reserve Mountains for
            // even more dominant relief, drop max toward 1.5 (67 % crossover) or 1.25
            // (80 %). To make Mountains fire more readily, push toward 3.0 (33 %).
            const float bandLow = 0.65f, bandHigh = float.PositiveInfinity, max = 2.0f;
            return BandFraction(ctx.CellRoughnesses, bandLow, bandHigh, max);
        }),

        new(Ck3Terrain.Hills, (in BaronyContext ctx) =>
        {
            // bandLow = 0.10 — Azgaar's p95-normalised cell roughness averages ~0.14 on
            // real maps, so cells in 0.10–0.30 are the typical "subtle rolling" relief.
            //
            // max = 1.5. Crossover vs a pure biome (score 1.0) is 67 % hill-grade cells
            // — a barony that is two-thirds hill-grade flips to Hills regardless of
            // biome. Less aggressive than Mountains (50 % crossover) because rolling
            // forest still reads as forest, but rolling grassland reads as hills.
            //
            // No DesertHills counterpart — CK3 has no such terrain. Hilly hot desert
            // just stays Desert (Desert at 1.0 beats Hills at 1.5 × 0.67 = 1.0 by
            // registry tiebreak), unless steepness pushes far enough for DesertMountains.
            const float bandLow = 0.10f, bandHigh = 0.65f, max = 1.5f;
            return BandFraction(ctx.CellRoughnesses, bandLow, bandHigh, max);
        }),

        // ── Parked rules ─────────────────────────────────────────────────────────
        new(Ck3Terrain.Oasis, (in BaronyContext _) =>
        {
            // Parked at 0 pending a better signal. The "HotDesert + river-adjacent"
            // rule over-fires once minor rivers count for adjacency: on a river-dense
            // map every HotDesert cell is within one neighbour-hop of a river, so
            // Oasis swallows the entire Desert category. Likely better signals:
            // population presence (burgs cluster at oases), restrict to cells ON a
            // river rather than neighbouring one, or freshwater-feature adjacency only.
            return 0f;
        }),

        new(Ck3Terrain.TerracedHills, (in BaronyContext _) =>
            // No Azgaar signal cleanly maps to terraced_hills. Kept here so adding a
            // real rule later is a one-line edit, not a new file.
            0f),
    };
}
