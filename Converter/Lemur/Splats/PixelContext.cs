namespace Converter.Lemur.Splats;

// Per-pixel facts that Material rules read. Built fresh per pixel from the three pre-computed
// fields (biomeTriples, steepness, heightmap bytes). Small struct (≈ 24 bytes) — passed by `in` to
// rules to avoid the per-call copy.
public readonly struct PixelContext
{
    public readonly int X;
    public readonly int Y;
    public readonly BiomeWeightTriple Biomes;
    public readonly float Steepness01;             // 0..1, p95-normalised gradient; 0 on sea or if no heightmap
    public readonly float Height01;                // 0..1, heightmap byte / 255; 0 if no heightmap
    public readonly float WaterLevel01;            // 0..1, MaxWaterByte / 255. Height01 > WaterLevel01 ⇔ pixel is land.
    public readonly bool IsLand;

    public PixelContext(int x, int y, BiomeWeightTriple biomes,
        float steepness01, float height01, float waterLevel01, bool isLand)
    {
        X = x; Y = y; Biomes = biomes;
        Steepness01 = steepness01; Height01 = height01;
        WaterLevel01 = waterLevel01; IsLand = isLand;
    }

    // Sugar for Material rules — read biome weight by Azgaar enum value.
    // Gated on IsLand so biome textures never leak onto sea-side pixels even when the relaxed
    // coastal-band gate in SplatmapBuilder lets sub-water pixels through to the material loop.
    //
    // Coast-fade is handled by the data, not by a curve here: BiomeWeightField now includes
    // sea cells in the Delaunay triangulation with biome = 0 (None). At a coast pixel, the
    // sea-cell corner of the triangle takes part of the barycentric weight, so land biome
    // weights naturally drop from 1.0 (inland) toward 0 (waterline) in cell-space — a much
    // better "distance to coast" signal than height-from-waterline.
    public float AzgaarBiomeWeight(AzgaarBiome b) => IsLand ? Biomes.WeightOf(b) : 0f;

    // Signed elevation relative to the waterline. 0 exactly at sea level, positive on land,
    // negative underwater. Use this for rules that need to behave differently above vs below the
    // waterline (beaches, tidal flats, kelp).
    public float ElevationFromWaterline01 => Height01 - WaterLevel01;
}

// Holds the three Azgaar biomes at this pixel's Delaunay triangle corners and their barycentric
// weights. Sum of weights = 1.0 for any pixel inside a land-cell triangle (or 0.0 for pixels
// outside the convex hull of land-cell centroids).
public readonly struct BiomeWeightTriple
{
    public readonly byte B0, B1, B2;          // AzgaarBiome value (1..12) or 0 = unused slot
    public readonly float W0, W1, W2;         // weights ≥ 0; sum to 1.0 for in-triangle pixels

    public BiomeWeightTriple(byte b0, float w0, byte b1, float w1, byte b2, float w2)
    {
        B0 = b0; W0 = w0; B1 = b1; W1 = w1; B2 = b2; W2 = w2;
    }

    public static BiomeWeightTriple Empty => default;

    public float WeightOf(AzgaarBiome b)
    {
        byte v = (byte)b;
        if (B0 == v) return W0;
        if (B1 == v) return W1;
        if (B2 == v) return W2;
        return 0f;
    }
}
