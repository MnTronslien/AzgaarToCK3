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
        new Material("farmland_01",        (in PixelContext ctx) => ctx.AzgaarBiomeWeight(AzgaarBiome.TropicalSeasonalForest)),
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
