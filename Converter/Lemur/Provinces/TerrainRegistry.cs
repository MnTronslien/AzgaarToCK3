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
    // ── Steepness thresholds — chosen to track the splatmap's visual hill/mountain ramps so
    //    a barony that the game *paints* as mountainous is also *gameplay-tagged* as such.
    //    See Lemur/Splats/MaterialRegistry.cs: hills_01 Tent peaks at 0.40, central_mountain
    //    LinearRamp starts at 0.65. Tune in lockstep with those if either side moves.
    public const float HILL_ROUGHNESS_THRESHOLD     = 0.30f;
    public const float MOUNTAIN_ROUGHNESS_THRESHOLD = 0.65f;

    // ── Population thresholds — placeholders. Real values need in-game tuning against a real
    //    map. PopDensity is burg.Population / cellCount; raw Population is the unscaled burg
    //    field from Azgaar (typically 0..~100 for a large city).
    public const float FLOODPLAIN_POPDENSITY_MIN = 5.0f;
    public const float FARMLAND_POPDENSITY_MIN   = 8.0f;

    // ── Score weights — unbounded non-negative. "Hard wins" use ≥ 5 so biome-defining rules
    //    can't be beaten by the soft fallback. Soft fallbacks sit at ~0.1 so they win only
    //    when nothing else fires.
    private const float HARD_WIN          = 5.0f;
    private const float STRONG_MATCH      = 3.0f;
    private const float MODERATE_MATCH    = 2.0f;
    private const float WEAK_MATCH        = 1.0f;
    private const float PLAINS_FALLBACK   = 0.1f;

    public static readonly IReadOnlyList<TerrainCandidate> All = new TerrainCandidate[]
    {
        // ── Biome-defined hard wins (geometry doesn't override these) ────────────

        new(Ck3Terrain.Wetlands, (in BaronyContext ctx) =>
            ctx.DominantBiome == AzgaarBiome.Wetland ? HARD_WIN : 0f),

        new(Ck3Terrain.Jungle, (in BaronyContext ctx) =>
            ctx.DominantBiome == AzgaarBiome.TropicalRainforest ? HARD_WIN : 0f),

        // ── Steepness-driven (geometry beats biome) ──────────────────────────────
        // DesertMountains placed BEFORE Mountains so when both fire in a desert
        // barony, DesertMountains' higher score wins.

        new(Ck3Terrain.DesertMountains, (in BaronyContext ctx) =>
        {
            if (ctx.Roughness < MOUNTAIN_ROUGHNESS_THRESHOLD) return 0f;
            bool isHotArid = ctx.DominantBiome == AzgaarBiome.HotDesert
                          || ctx.DominantBiome == AzgaarBiome.ColdDesert;
            return isHotArid ? HARD_WIN : 0f;
        }),

        new(Ck3Terrain.Mountains, (in BaronyContext ctx) =>
        {
            if (ctx.Roughness < MOUNTAIN_ROUGHNESS_THRESHOLD) return 0f;
            // Wetland / Jungle already won as hard biome lock-ins; Mountains takes
            // everything else that's vertical enough (incl. Glacier — snowy peaks
            // read as mountains, the cleanest CK3 analogue for high-altitude ice).
            return STRONG_MATCH;
        }),

        // ── Cold-biome forest catch-all (Taiga absorbs Tundra; CK3 has no "tundra") ──

        new(Ck3Terrain.Taiga, (in BaronyContext ctx) =>
        {
            var b = ctx.DominantBiome;
            if (b == AzgaarBiome.Taiga || b == AzgaarBiome.Tundra) return HARD_WIN;
            // Glacier on flat ground — no CK3 "glacier" terrain exists. Taiga is the
            // least-wrong fallback (cold + low-supply). Flip to a different terrain
            // here if test maps show large flat Glacier zones reading poorly.
            if (b == AzgaarBiome.Glacier && ctx.Roughness < MOUNTAIN_ROUGHNESS_THRESHOLD) return STRONG_MATCH;
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

        new(Ck3Terrain.Oasis, (in BaronyContext ctx) =>
        {
            // Oasis is the river-adjacent fertile patch within the hot desert biome.
            // Specifically scored above plain Desert so it wins when both fire.
            return ctx.DominantBiome == AzgaarBiome.HotDesert && ctx.RiverAdjacent
                ? HARD_WIN
                : 0f;
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
            // Hills fires on moderately rough non-forest, non-wetland, non-jungle land.
            // Forests in CK3 don't have a "hilly forest" terrain — biome wins there.
            if (ctx.Roughness < HILL_ROUGHNESS_THRESHOLD) return 0f;
            if (ctx.Roughness >= MOUNTAIN_ROUGHNESS_THRESHOLD) return 0f;  // Mountains owns this band
            var b = ctx.DominantBiome;
            // Restrict to "open" biomes — Grassland and similar — so Hills doesn't steal
            // from Forest/Desert/Taiga which read better as their biome at moderate slopes.
            if (b == AzgaarBiome.Grassland) return STRONG_MATCH;
            return 0f;
        }),

        new(Ck3Terrain.Plains, (in BaronyContext ctx) =>
            // Always-true soft fallback. Wins only when nothing else fires.
            PLAINS_FALLBACK),

        // ── Placeholder for completeness — no Azgaar signal cleanly maps to terraced_hills.
        //    Kept here so adding a real rule later is a one-line edit, not a new file.
        new(Ck3Terrain.TerracedHills, (in BaronyContext _) => 0f),
    };
}
