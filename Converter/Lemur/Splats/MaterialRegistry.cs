namespace Converter.Lemur.Splats;

// The one place all material rules live. Each entry declares a CK3 texture name + a lambda
// that computes the material's weight at a given pixel. Editing this file (and rebuilding) is
// the iteration loop for tuning what gets rendered where.
//
// All rules are pure functions of PixelContext. No I/O, no shared state.
//
// Texture names below MUST appear as keys in Ck3MaterialBytes.ByName (auto-generated from CK3
// materials.settings). If a texture name doesn't resolve, the pack step throws.
public static class MaterialRegistry
{
    public static readonly IReadOnlyList<Material> All = new Material[]
    {
        // ── Biome materials — Azgaar biome → CK3 texture name ──
        // The mapping IS the lambda. Each line reads:
        //   "render <texture> at this pixel, weighted by how much of <Azgaar biome> is here."

        new Material("wetlands_02",        (in PixelContext ctx) => ctx.AzgaarBiomeWeight(AzgaarBiome.Wetland)),  // swamp
        new Material("plains_01",          (in PixelContext ctx) => ctx.AzgaarBiomeWeight(AzgaarBiome.Grassland)),
        new Material("plains_01_dry",      (in PixelContext ctx) => ctx.AzgaarBiomeWeight(AzgaarBiome.Savanna)),
        new Material("desert_01",          (in PixelContext ctx) => ctx.AzgaarBiomeWeight(AzgaarBiome.HotDesert)),
        new Material("desert_02",          (in PixelContext ctx) => ctx.AzgaarBiomeWeight(AzgaarBiome.ColdDesert)),
        new Material("drylands_01_grassy", (in PixelContext ctx) => ctx.AzgaarBiomeWeight(AzgaarBiome.TropicalSeasonalForest)),
        new Material("forest_leaf_01",     (in PixelContext ctx) => ctx.AzgaarBiomeWeight(AzgaarBiome.TemperateDeciduousForest)),
        new Material("forest_jungle_01",   (in PixelContext ctx) => ctx.AzgaarBiomeWeight(AzgaarBiome.TropicalRainforest)),
        new Material("forest_pine_01",     (in PixelContext ctx) => ctx.AzgaarBiomeWeight(AzgaarBiome.TemperateRainforest)),
        new Material("forestfloor",        (in PixelContext ctx) => ctx.AzgaarBiomeWeight(AzgaarBiome.Taiga)),
        new Material("northern_plains_01", (in PixelContext ctx) => ctx.AzgaarBiomeWeight(AzgaarBiome.Tundra)),
        new Material("snow",               (in PixelContext ctx) => ctx.AzgaarBiomeWeight(AzgaarBiome.Glacier)),

        // ── Steepness materials — weight is a curve on Steepness01 ──
        // Note: vanilla's `mountain_02` entry is commented out in materials.settings, so we use
        // `central_mountain` (active index 52) as the generic mountain texture. If the user wants
        // a different one, swap the texture name here.
        new Material("hills_01",        (in PixelContext ctx) => Tent(ctx.Steepness01, 0.20f, 0.40f, 0.75f)),
        new Material("central_mountain",(in PixelContext ctx) => LinearRamp(ctx.Steepness01, 0.65f, 1.00f)),

        // ── Seafloor material — fills the underwater coastal band, blending with beach ──
        // Tent peaks ~25 bytes below the waterline, zero at the waterline (beach owns there) and
        // zero at ~30 bytes underwater (matches CoastalBandUnderwater). Combined with the beach
        // material's underwater taper, this gives beach→mud_wet_01 transition along the surf zone.
        // Multiplier 1.5 so mud dominates the far half of the underwater band where beach has faded.
        new Material("mud_wet_01", (in PixelContext ctx) =>
        {
            float e = ctx.ElevationFromWaterline01;
            if (e >= 0f) return 0f;     // mud is sea-side only
            return 1.5f * Tent(-e, 0f, 25f / 255f, 30f / 255f);
        }),

        // ── Coastline material — peaks AT the waterline, asymmetric falloff ──
        // ElevationFromWaterline01 is signed: 0 at sea level, positive inland, negative underwater.
        // Above water: aggressive linear taper to 0 over ~3 bytes (≈ 0.012 height units) so the
        //   beach strip is thin on the land side.
        // Below water: gentler linear taper to 0 over ~10 bytes (≈ 0.04 height units) so we get a
        //   visible underwater margin (the surf zone). SplatmapBuilder.CoastalBandUnderwater is
        //   sized to match this reach — keep them in sync.
        // Peak weight 1.5 at the waterline beats the ~1.0 biome weight by half, so beach wins the
        // top splat slot at the coast but doesn't completely erase biome blending.
        new Material("beach_02", (in PixelContext ctx) =>
        {
            // Above-water taper: 3 bytes inland to zero — keeps the crisp beachline visible
            // at the exact waterline pixel. Biome materials fade in over a WIDER band via the
            // Delaunay sea-corner mechanic in BiomeWeightField, so beach (sharp peak at coast)
            // and biome (gradual rise inland) overlap to give a smooth coast-to-land blend
            // without elevation acting as a stand-in for distance.
            const float aboveBand = 0.012f;      // ~3 bytes inland — crisp coast strip
            const float belowBand = 30f / 255f;  // ~30 bytes underwater (must match SplatmapBuilder.CoastalBandUnderwater)
            const float peak      = 1.5f;        // weight at the exact waterline
            float e = ctx.ElevationFromWaterline01;
            if (e >= 0f)
            {
                if (e >= aboveBand) return 0f;
                return peak * (1f - e / aboveBand);
            }
            else
            {
                float depth = -e;
                if (depth >= belowBand) return 0f;
                return peak * (1f - depth / belowBand);
            }
        }),
    };

    // Rises from 0 at `low` to 1 at `peak`, falls back to 0 at `high`. Outside [low, high] returns 0.
    public static float Tent(float x, float low, float peak, float high)
    {
        if (x <= low || x >= high) return 0f;
        if (x <= peak) return (x - low) / (peak - low);
        return 1f - (x - peak) / (high - peak);
    }

    // Linear ramp from 0 at `low` to 1 at `high`. Clamped to [0, 1].
    public static float LinearRamp(float x, float low, float high)
    {
        if (x <= low) return 0f;
        if (x >= high) return 1f;
        return (x - low) / (high - low);
    }
}
