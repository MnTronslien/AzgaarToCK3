using ImageMagick;

namespace Converter.Lemur.Writers;

/// <summary>
/// Writes the three geometry-driven terrain mask PNGs as all-black 8-bit greyscale.
///
/// hills_01_mask.png, mountain_02_mask.png, mountain_02_c_snow_mask.png
///
/// WHY all-black: these PNGs in gfx/map/terrain/masks/ are Map Editor input ONLY —
/// CK3 does NOT consume them at runtime. The Map Editor packs masks into
/// detail_index.tga + detail_intensity.tga, which is what CK3 actually reads at
/// runtime, and our converter writes those TGAs directly via the splatmap path.
/// The mask PNG *content* is therefore don't-care for our output; their *existence*
/// may still matter for CK3 1.18 startup, so we emit empty (black) placeholders.
/// See CK3_MAP_MODDING_FACTS.md §"Mask PNGs (gfx/map/terrain/masks/*_mask.png) —
/// Map Editor input, not runtime" for the full context.
///
/// All three are 8-bit greyscale at heightmap dimensions (8192×4096), written to
/// gfx/map/terrain/masks/. Black = no contribution, which is the safe inert value.
/// </summary>
public static class HeightmapMasks
{
    // heightmapF and pixels are kept on the signature for ABI compatibility with
    // the previous steepness/elevation-driven implementation; they are unused now.
    public static async Task Write(float[] heightmapF, byte[] pixels, int width, int height, string masksDir)
    {
        Directory.CreateDirectory(masksDir);

        // One shared zero-filled buffer for all three masks — no per-pixel work.
        var blackPixels = new byte[width * height];

        var settings = new MagickReadSettings
        {
            Width = width, Height = height,
            ColorSpace = ColorSpace.Gray,
            Format = MagickFormat.Gray,
        };

        await Task.WhenAll(
            WriteMask(blackPixels, "hills_01_mask.png",           masksDir, settings),
            WriteMask(blackPixels, "mountain_02_mask.png",        masksDir, settings),
            WriteMask(blackPixels, "mountain_02_c_snow_mask.png", masksDir, settings));

        Logger.Info("HeightmapMasks: hills_01_mask.png, mountain_02_mask.png, mountain_02_c_snow_mask.png written (all-black placeholders — Map Editor input only)");
    }

    static async Task WriteMask(byte[] pixels, string fileName, string dir, MagickReadSettings settings)
    {
        using var img = new MagickImage(pixels, settings);
        img.Depth = 8;
        await img.WriteAsync(Path.Combine(dir, fileName), MagickFormat.Png);
    }
}
