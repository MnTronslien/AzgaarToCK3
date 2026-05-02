using ImageMagick;

namespace Converter.Lemur.Writers;

/// <summary>
/// Generates the three geometry-driven terrain mask PNGs from the heightmap pixel array.
/// The biome-driven masks (forest, desert, wetlands etc.) are handled separately
/// by TerrainMaskPreparer / TerrainMaskWriter.
///
/// hills_01_mask.png        — derived from surface steepness (surface normal dot up)
/// mountain_02_mask.png     — derived from surface steepness (higher threshold)
/// mountain_02_c_snow_mask  — derived from pixel height (elevation above snow line)
///
/// All three are 8-bit greyscale, 8192×4096, written to gfx/map/terrain/masks/.
/// White = full texture weight, black = none. CK3 blends masks additively.
///
/// Algorithm:
///   The incoming pixel array is already Gaussian-blurred by HeightmapAlgorithm,
///   so facet edges are pre-smoothed. We compute the surface normal directly:
///     N = normalize(-Gx, -Gy, 1)   (central difference, kernel half-width kd)
///     steepness = 1 - N.z = 1 - 1/sqrt(Gx²+Gy²+1)
///   steepness is 0 for flat terrain and approaches 1 for a vertical cliff.
///   p95 over land pixels auto-calibrates the ramps per map.
/// </summary>
static class HeightmapMasks
{
    // Hills: tent function — ramps up from HillsFloor, peaks full-white at HillsPeak,
    // then fades back to black by HillsCeiling. Keeps hills out of the mountain zone.
    private const float HillsFloor   = 0.20f;
    private const float HillsPeak    = 0.40f;
    private const float HillsCeiling = 0.75f;

    // Mountains: plain ramp from MountainsFloor to 1.0 (no ceiling — steepest = whitest).
    // Overlap with hills: 0.65–0.75, both near-zero at the edges of that band.
    private const float MountainsFloor = 0.65f;

    // Snow ramp — normalised height (0–1 over the full 0–255 range)
    private const float SnowLow  = 0.82f;  // ~209 / 255
    private const float SnowHigh = 0.95f;  // ~242 / 255

    // Gradient kernel half-width. The heightmap is already Gaussian-blurred (radius 3),
    // so kd=4 safely lands within smoothed terrain rather than on triangle facet edges.
    private const int kd = 4;

    // heightmapF: float heightmap (pre-quantization) for smooth normal computation.
    // pixels: byte heightmap used only for ocean masking (land/sea boundary).
    public static async Task Write(float[] heightmapF, byte[] pixels, int width, int height, string masksDir)
    {
        Directory.CreateDirectory(masksDir);
        byte wl = HeightmapAlgorithm.CK3WaterLevel;

        // ── 1. Surface steepness = 1 − N.z ───────────────────────────────────
        // Use float heights for smooth gradients (byte quantization causes terracing).
        // Divide by 2*kd so gx/gy are gradient per pixel.
        // Zero out component if either sample crosses ocean boundary.
        var steep = new float[width * height];
        for (int y = kd; y < height - kd; y++)
        for (int x = kd; x < width  - kd; x++)
        {
            int i = y * width + x;
            if (pixels[i] < wl) continue;
            float gx = (pixels[i + kd]         >= wl && pixels[i - kd]         >= wl)
                ? (heightmapF[i + kd]         - heightmapF[i - kd])         / (2f * kd) : 0f;
            float gy = (pixels[i + kd * width] >= wl && pixels[i - kd * width] >= wl)
                ? (heightmapF[i + kd * width] - heightmapF[i - kd * width]) / (2f * kd) : 0f;
            steep[i] = 1f - 1f / MathF.Sqrt(gx * gx + gy * gy + 1f);
        }

        // ── 2. p95 of steepness over land pixels ─────────────────────────────
        // 1024 bins covering [0, 1.024), bin width = 0.001
        var hist = new long[1024];
        int landCount = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] < wl) continue;
            landCount++;
            hist[Math.Clamp((int)(steep[i] * 1000f), 0, 1023)]++;
        }

        float p95 = 0.001f;
        if (landCount > 0)
        {
            long target = (long)(landCount * 0.95), cum = 0;
            for (int b = 0; b < hist.Length; b++)
            {
                cum += hist[b];
                if (cum >= target) { p95 = Math.Max(0.001f, b / 1000f); break; }
            }
        }
        Logger.Info($"HeightmapMasks: steepness p95={p95:F4} (normalisation ceiling)");

        // ── 3. Fill mask arrays ───────────────────────────────────────────────
        var hillsMask     = new byte[width * height];
        var mountainsMask = new byte[width * height];
        var snowMask      = new byte[width * height];

        for (int i = 0; i < pixels.Length; i++)
        {
            byte h = pixels[i];
            if (h < wl) continue;

            float s = p95 > 0f ? steep[i] / p95 : 0f;

            hillsMask[i]     = FloatToByte(Tent(s,        HillsFloor, HillsPeak, HillsCeiling));
            mountainsMask[i] = FloatToByte(LinearRamp(s, MountainsFloor, 1f));
            snowMask[i]      = FloatToByte(LinearRamp(h / 255f, SnowLow,        SnowHigh));
        }

        // ── 4. Write PNGs ─────────────────────────────────────────────────────
        var settings = new MagickReadSettings
        {
            Width = width, Height = height,
            ColorSpace = ColorSpace.Gray,
            Format = MagickFormat.Gray,
        };

        await Task.WhenAll(
            WriteMask(hillsMask,     "hills_01_mask.png",           masksDir, settings),
            WriteMask(mountainsMask, "mountain_02_mask.png",        masksDir, settings),
            WriteMask(snowMask,      "mountain_02_c_snow_mask.png", masksDir, settings));

        Logger.Info("HeightmapMasks: hills_01_mask.png, mountain_02_mask.png, mountain_02_c_snow_mask.png written");
    }

    static async Task WriteMask(byte[] pixels, string fileName, string dir, MagickReadSettings settings)
    {
        using var img = new MagickImage(pixels, settings);
        img.Depth = 8;
        await img.WriteAsync(Path.Combine(dir, fileName), MagickFormat.Png);
    }

    // Ramps 0→1 over [low, high].
    static float LinearRamp(float x, float low, float high)
        => Math.Clamp((x - low) / (high - low), 0f, 1f);

    // Tent: rises 0→1 over [low, peak], falls 1→0 over [peak, high].
    static float Tent(float x, float low, float peak, float high)
        => Math.Min(LinearRamp(x, low, peak), 1f - LinearRamp(x, peak, high));

    static byte FloatToByte(float v) => (byte)Math.Clamp((int)(v * 255f), 0, 255);
}
