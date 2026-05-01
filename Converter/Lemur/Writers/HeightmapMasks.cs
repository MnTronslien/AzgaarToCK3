using ImageMagick;

namespace Converter.Lemur.Writers;

/// <summary>
/// Generates the three geometry-driven terrain mask PNGs from the heightmap pixel array.
/// The biome-driven masks (forest, desert, wetlands etc.) are handled separately
/// by TerrainMaskPreparer / TerrainMaskWriter.
///
/// hills_01_mask.png        — derived from per-pixel surface steepness (gradient magnitude)
/// mountain_02_mask.png     — derived from per-pixel surface steepness (higher threshold)
/// mountain_02_c_snow_mask  — derived from pixel height (elevation above snow line)
///
/// All three are 8-bit greyscale, 8192×4096, written to gfx/map/terrain/masks/.
/// White = full texture weight, black = none. CK3 blends masks additively.
/// </summary>
static class HeightmapMasks
{
    // Steepness ramps — fraction of p95 normalised gradient magnitude
    private const float HillsLow      = 0.15f;
    private const float HillsHigh     = 0.55f;
    private const float MountainsLow  = 0.45f;
    private const float MountainsHigh = 0.85f;

    // Snow ramp — normalised height (0–1 over the full 0–255 range)
    private const float SnowLow  = 0.82f;  // ~209 / 255
    private const float SnowHigh = 0.95f;  // ~242 / 255

    public static async Task Write(byte[] pixels, int width, int height, string masksDir)
    {
        Directory.CreateDirectory(masksDir);

        // ── 1. Compute gradient magnitude per pixel ───────────────────────────
        var gradMag = new float[width * height];
        for (int y = 1; y < height - 1; y++)
        for (int x = 1; x < width  - 1; x++)
        {
            int i  = y * width + x;
            float dx = pixels[i + 1]     - pixels[i - 1];
            float dy = pixels[i + width] - pixels[i - width];
            gradMag[i] = MathF.Sqrt(dx * dx + dy * dy);
        }

        // ── 2. Auto-normalise: find p95 gradient over land pixels via histogram
        var hist = new long[1024]; // bin width = 0.1 gradient units, covers 0–102
        int landCount = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] < HeightmapAlgorithm.CK3WaterLevel) continue;
            landCount++;
            hist[Math.Clamp((int)(gradMag[i] * 10f), 0, 1023)]++;
        }

        float normGrad = 1f;
        if (landCount > 0)
        {
            long target = (long)(landCount * 0.95), cumulative = 0;
            for (int b = 0; b < hist.Length; b++)
            {
                cumulative += hist[b];
                if (cumulative >= target) { normGrad = Math.Max(0.1f, b / 10f); break; }
            }
        }

        Logger.Info($"HeightmapMasks: gradient p95={normGrad:F2} (normalisation ceiling)");

        // ── 3. Single pass: fill all three mask arrays ────────────────────────
        var hillsMask     = new byte[width * height];
        var mountainsMask = new byte[width * height];
        var snowMask      = new byte[width * height];

        for (int i = 0; i < pixels.Length; i++)
        {
            byte h = pixels[i];
            if (h < HeightmapAlgorithm.CK3WaterLevel) continue;

            float steepness = Math.Clamp(gradMag[i] / normGrad, 0f, 1f);
            float heightN   = h / 255f;

            hillsMask[i]     = FloatToByte(Smoothstep(steepness, HillsLow,     HillsHigh));
            mountainsMask[i] = FloatToByte(Smoothstep(steepness, MountainsLow, MountainsHigh));
            snowMask[i]      = FloatToByte(Smoothstep(heightN,   SnowLow,      SnowHigh));
        }

        // ── 4. Write PNGs ─────────────────────────────────────────────────────
        var settings = new MagickReadSettings
        {
            Width = width, Height = height,
            ColorSpace = ColorSpace.Gray,
            Format = MagickFormat.Gray,
        };

        await Task.WhenAll(
            WriteMask(hillsMask,     "hills_01_mask.png",             masksDir, settings),
            WriteMask(mountainsMask, "mountain_02_mask.png",          masksDir, settings),
            WriteMask(snowMask,      "mountain_02_c_snow_mask.png",   masksDir, settings));

        Logger.Info("HeightmapMasks: hills_01_mask.png, mountain_02_mask.png, mountain_02_c_snow_mask.png written");
    }

    static async Task WriteMask(byte[] pixels, string fileName, string dir, MagickReadSettings settings)
    {
        using var img = new MagickImage(pixels, settings);
        img.Depth = 8;
        await img.WriteAsync(Path.Combine(dir, fileName), MagickFormat.Png);
    }

    static float Smoothstep(float x, float edge0, float edge1)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    static byte FloatToByte(float v) => (byte)Math.Clamp((int)(v * 255f), 0, 255);
}
