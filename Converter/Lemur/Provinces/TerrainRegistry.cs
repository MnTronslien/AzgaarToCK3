using Converter.Lemur.Splats;

namespace Converter.Lemur.Provinces;

// The one place all CK3-terrain-selection rules live. Each entry declares one Ck3Terrain
// value + a lambda that scores how well it matches a barony's context. BaronyTerrainAssigner
// evaluates all entries against each barony and picks the top-1 (argmax) as Barony.Ck3Terrain.
//
// All rules are pure functions of BaronyContext. No I/O, no shared state.
//
// Score scale: non-negative floats (matches MaterialRegistry). Higher = better match. Order
// in the list breaks ties — hard-win rules (Wetlands, Jungle) come early so they outrank the
// soft `plains` fallback on equal scores. Tuning by changing the numeric constants below.
//
// Open design questions captured as named constants so they are easy to flip during in-game
// tuning: see ColdDesert handling (Steppe vs Desert), TropicalSeasonalForest (Drylands vs
// Jungle), Glacier (Mountains vs Taiga).
public static class TerrainRegistry
{
    // ── Population thresholds — placeholders. Real values need in-game tuning against a real
    //    map. PopDensity is burg.Population / cellCount; raw Population is the unscaled burg
    //    field from Azgaar (typically 0..~100 for a large city).
    public const float FLOODPLAIN_POPDENSITY_MIN = 5.0f;
    public const float FARMLAND_POPDENSITY_MIN   = 8.0f;

    // ── Score weights — unbounded non-negative. "Hard wins" use ≥ 5 so biome-defining rules
    //    can't be beaten by the soft fallback. Soft fallbacks sit at ~0.1 so they win only
    //    when nothing else fires. Band-based steepness rules (Hills, Mountains, DesertMountains)
    //    use their own `max` constants declared next to each lambda — see those for tuning.
    private const float HARD_WIN          = 5.0f;
    private const float STRONG_MATCH      = 3.0f;
    private const float MODERATE_MATCH    = 2.0f;
    private const float WEAK_MATCH        = 1.0f;
    private const float PLAINS_FALLBACK   = 0.1f;

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
        // ── Biome-defined hard wins (geometry doesn't override these) ────────────

        new(Ck3Terrain.Wetlands, (in BaronyContext ctx) =>
            ctx.DominantBiome == AzgaarBiome.Wetland ? HARD_WIN : 0f),

        new(Ck3Terrain.Jungle, (in BaronyContext ctx) =>
            ctx.DominantBiome == AzgaarBiome.TropicalRainforest ? HARD_WIN : 0f),

        // ── Steepness-driven (geometry beats most biomes) ─────────────────────
        // Band-based: fraction of cells in [low, high) × max. So 100 % mountain-grade cells
        // gives the full `max`; 0 % gives 0; linear in between. Same formula for Mountains
        // and DesertMountains; the only difference is DesertMountains' biome gate. Mountains
        // and DesertMountains use IDENTICAL band+max, so they tie when both fire; registry
        // order places DesertMountains first so it wins in arid biomes.

        new(Ck3Terrain.DesertMountains, (in BaronyContext ctx) =>
        {
            if (ctx.DominantBiome != AzgaarBiome.HotDesert
             && ctx.DominantBiome != AzgaarBiome.ColdDesert) return 0f;
            const float bandLow = 0.65f, bandHigh = float.PositiveInfinity, max = 4.5f;
            return BandFraction(ctx.CellRoughnesses, bandLow, bandHigh, max);
        }),

        new(Ck3Terrain.Mountains, (in BaronyContext ctx) =>
        {
            // max = 4.5 sits above STRONG_MATCH (3.0) — beats Forest/Drylands/Taiga at high
            // mountain-cell fractions — and below HARD_WIN (5.0) — Wetlands and Jungle still
            // win their biome lock. To make Mountains overrule Jungle (Himalayan foothills),
            // bump max above 5.0 and add an equivalent bump to Wetlands so mountain-swamp
            // doesn't appear.
            const float bandLow = 0.65f, bandHigh = float.PositiveInfinity, max = 4.5f;
            return BandFraction(ctx.CellRoughnesses, bandLow, bandHigh, max);
        }),

        // ── Cold-biome forest catch-all (Taiga absorbs Tundra; CK3 has no "tundra") ──

        new(Ck3Terrain.Taiga, (in BaronyContext ctx) =>
        {
            var b = ctx.DominantBiome;
            if (b == AzgaarBiome.Taiga || b == AzgaarBiome.Tundra) return HARD_WIN;
            // Glacier — no CK3 "glacier" terrain exists. Taiga is the least-wrong fallback
            // (cold + low-supply). The Mountains rule will out-score this whenever the
            // Glacier zone has enough mountain-grade cells to be properly mountainous.
            if (b == AzgaarBiome.Glacier) return STRONG_MATCH;
            return 0f;
        }),

        // ── Temperate forest ─────────────────────────────────────────────────────

        new(Ck3Terrain.Forest, (in BaronyContext ctx) =>
        {
            var b = ctx.DominantBiome;
            if (b == AzgaarBiome.TemperateDeciduousForest) return HARD_WIN;
            if (b == AzgaarBiome.TemperateRainforest)       return HARD_WIN;
            return 0f;
        }),

        // ── Hot-arid trio (Oasis > Desert; both > nothing) ──────────────────────

        new(Ck3Terrain.Oasis, (in BaronyContext _) =>
        {
            // Parked at 0 pending a better signal. The "HotDesert + river-adjacent" rule
            // over-fires once minor rivers are counted for adjacency: on a river-dense map,
            // every HotDesert cell is within one neighbour-hop of a river, so Oasis swallows
            // the entire Desert category. Real oases are tiny isolated wet spots inside large
            // arid expanses, not "every desert near a river." Likely better signals to try
            // next: require population presence (burgs cluster at oases), restrict to cells
            // ON a river rather than neighbouring one, or detect freshwater-feature adjacency
            // specifically rather than any river.
            return 0f;
        }),

        new(Ck3Terrain.Desert, (in BaronyContext ctx) =>
            ctx.DominantBiome == AzgaarBiome.HotDesert ? STRONG_MATCH : 0f),

        // ── Cold-arid / semi-arid ───────────────────────────────────────────────
        // ColdDesert biome covers both genuine cold deserts (Gobi) and cold steppes
        // (Kazakhstan). We bias toward Steppe — cavalry country, horse-archer match.
        // To flip a particular map's ColdDesert zones to Desert instead, change this
        // rule's terrain to Ck3Terrain.Desert.

        new(Ck3Terrain.Steppe, (in BaronyContext ctx) =>
            ctx.DominantBiome == AzgaarBiome.ColdDesert ? STRONG_MATCH : 0f),

        new(Ck3Terrain.Drylands, (in BaronyContext ctx) =>
        {
            var b = ctx.DominantBiome;
            // Savanna = warm-dry sparse vegetation; TropicalSeasonalForest = monsoon
            // dry-deciduous scrub. Both read closer to drylands than to forest or desert.
            if (b == AzgaarBiome.Savanna)                  return STRONG_MATCH;
            if (b == AzgaarBiome.TropicalSeasonalForest)   return STRONG_MATCH;
            return 0f;
        }),

        // ── Grassland branch — needs secondary signals to lift out of `plains` ──

        new(Ck3Terrain.Floodplains, (in BaronyContext ctx) =>
        {
            if (ctx.DominantBiome != AzgaarBiome.Grassland) return 0f;
            if (!ctx.RiverAdjacent) return 0f;
            if (ctx.PopDensity < FLOODPLAIN_POPDENSITY_MIN) return 0f;
            return STRONG_MATCH;
        }),

        new(Ck3Terrain.Farmlands, (in BaronyContext ctx) =>
        {
            if (ctx.DominantBiome != AzgaarBiome.Grassland) return 0f;
            if (ctx.PopDensity < FARMLAND_POPDENSITY_MIN) return 0f;
            return MODERATE_MATCH;
        }),

        new(Ck3Terrain.Hills, (in BaronyContext ctx) =>
        {
            // max = 1.2 — sits just above PLAINS_FALLBACK and WEAK_MATCH (1.0), below every
            // biome rule (STRONG_MATCH 3.0 and HARD_WIN 5.0). So Hills wins over plain Plains
            // (hilly grassland → Hills) but loses to all biome rules (hilly forest → Forest;
            // hilly desert → Desert). Vanilla CK3 has no "hilly forest" or "hilly desert"
            // terrain — biome wins those cases, which matches this scoring.
            //
            // No biome filter (intentional). The score discipline alone produces the right
            // outcomes: only Plains baronies have nothing else firing strongly enough to
            // beat 1.2, so Hills only takes from Plains. There is no analogous DesertHills
            // entry because CK3 has no such terrain — hilly hot desert just stays Desert.
            const float bandLow = 0.30f, bandHigh = 0.65f, max = 1.2f;
            return BandFraction(ctx.CellRoughnesses, bandLow, bandHigh, max);
        }),

        new(Ck3Terrain.Plains, (in BaronyContext ctx) =>
            // Always-true soft fallback. Wins only when nothing else fires.
            PLAINS_FALLBACK),

        // ── Placeholder for completeness — no Azgaar signal cleanly maps to terraced_hills.
        //    Kept here so adding a real rule later is a one-line edit, not a new file.
        new(Ck3Terrain.TerracedHills, (in BaronyContext _) => 0f),
    };
}
