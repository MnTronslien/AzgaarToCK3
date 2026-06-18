namespace Converter.Lemur.Vegetation;

// Shared vegetation math used by BOTH the production writer (Converter) and the TerrainLab debug
// harness, so the preview image and the in-game output can never drift. Holds the canonical tuning
// for the features that must match exactly: the large-scale density noise and the gradual height
// falloff. (Per-rule densities, mesh sets, and colours stay with each caller.)
public static class VegetationCore
{
    // ── Gradual height falloff (heightmap byte units; waterline = MaxWaterByte ≈ 20) ──
    // Full vegetation up to Low; linear taper Low→High; none above High.
    public const int TreelineLow  = 35;
    public const int TreelineHigh = 50;

    // ── Large-scale density noise — breaks the "same clump size/coverage everywhere" uniformity ──
    // The field multiplies per-square density, so high-noise regions get many trees (→ big dense
    // groves once tightened) and low-noise regions get few (→ small sparse stands). One field varies
    // both coverage AND grove size. Wavelength is in map pixels (8192 wide), so ~900 ≈ 9 lobes across.
    public const float NoiseAmp        = 0.75f;   // density factor swings in [1-amp, 1+amp] = [0.25, 1.75]
    public const float NoiseWavelength = 900f;
    public const int   NoiseSeed       = 1234;

    /// <summary>Debug-only amplitude override (set by TerrainLab to preview different strengths). &lt;0 = use the const.</summary>
    public static float NoiseAmpOverride = -1f;

    /// <summary>Density multiplier at a pixel from the large-scale noise field: [1-amp, 1+amp] (clamped ≥0).</summary>
    public static float DensityFactor(float x, float y)
    {
        float amp = NoiseAmpOverride >= 0f ? NoiseAmpOverride : NoiseAmp;
        float f = (1f - amp) + 2f * amp * ValueNoise2D(x, y, NoiseWavelength, NoiseSeed);
        return f < 0f ? 0f : f;   // large amp can drive the low side negative → treat as bare
    }

    /// <summary>Raw noise field value [0,1] at a pixel — for the debug overlay.</summary>
    public static float NoiseRaw(float x, float y) => ValueNoise2D(x, y, NoiseWavelength, NoiseSeed);

    /// <summary>Probability [0..1] a tree at heightmap byte <paramref name="hb"/> survives the gradual treeline.</summary>
    public static float HeightKeepProb(int hb)
    {
        if (hb <= TreelineLow) return 1f;
        if (hb >= TreelineHigh) return 0f;
        return (TreelineHigh - hb) / (float)(TreelineHigh - TreelineLow);
    }

    // ── Deterministic 2D value noise in [0,1] (no System.Random; pure function of lattice + seed) ──
    public static float ValueNoise2D(float x, float y, float wavelength, int seed)
    {
        float fx = x / wavelength, fy = y / wavelength;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float tx = Smooth(fx - x0), ty = Smooth(fy - y0);
        float a = Lerp(Hash(x0, y0, seed),     Hash(x0 + 1, y0, seed),     tx);
        float b = Lerp(Hash(x0, y0 + 1, seed), Hash(x0 + 1, y0 + 1, seed), tx);
        return Lerp(a, b, ty);
    }

    private static float Hash(int xi, int yi, int seed)
    {
        unchecked
        {
            uint h = (uint)(xi * 374761393 + yi * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;   // [0,1)
        }
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
