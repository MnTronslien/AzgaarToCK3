namespace Converter.Lemur.Writers;

/// <summary>
/// Writes the rasters that keep the standalone (TCS-free) map from showing vanilla content:
/// surround_map (terrain outside the grid — tile copied from CK3, mask/fade generated flat), water
/// (flow/foam/colour, generated flat), and an empty map_object_data/generated (no vanilla trees).
/// Flat solids come from <see cref="DdsWriter"/> at the uniform values TCS used; enrich later (notes-for-later.md).
/// </summary>
public static class MapRenderAssetsWriter
{
    // (relative output path, width, height, r, g, b, a) — flat-solid rasters generated in code.
    private static readonly (string Rel, int W, int H, byte R, byte G, byte B, byte A)[] SolidRasters =
    [
        ("surround_map/surround_mask.dds",          4096, 2048,   0,   0,   0, 255),
        ("surround_map/surround_fade.dds",          1024,  512, 255, 255,   0, 255),
        ("water/flowmap.dds",                       2048, 1024, 126, 130, 255, 255),
        ("water/foam_map.dds",                      1024,  512,   0,   1,   0, 255),
        ("water/watercolor_rgb_waterspec_a.dds",    4096, 2048,  13,  27,  32, 255),
    ];

    public static async Task Write(string ck3Directory, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing map render assets");

        foreach (var s in SolidRasters)
            await DdsWriter.WriteSolidAsync(
                Helper.GetPath(outputDirectory, "gfx", "map", s.Rel.Replace('/', Path.DirectorySeparatorChar)),
                s.W, s.H, s.R, s.G, s.B, s.A);

        // surround_tile is byte-identical to vanilla — copy it from the CK3 install (not generated).
        var tileSrc = Helper.GetPath(ck3Directory, "game", "gfx", "map", "surround_map", "surround_tile.dds");
        var copied = 0;
        if (File.Exists(tileSrc))
        {
            var dst = Helper.GetPath(outputDirectory, "gfx", "map", "surround_map", "surround_tile.dds");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(tileSrc, dst, overwrite: true);
            copied = 1;
        }
        else
        {
            Logger.Warning("MapRenderAssetsWriter: surround_tile.dds not found in CK3 install — skipping.");
        }

        // gfx/map/map_object_data/generated — replace_path'd + shipped EMPTY (no vanilla vegetation).
        Directory.CreateDirectory(Helper.GetPath(outputDirectory, "gfx", "map", "map_object_data", "generated"));

        Logger.Info($"Wrote map render assets: {SolidRasters.Length} generated solids (surround mask/fade + water) + {copied} CK3 copy (surround_tile) + empty generated [no TCS, no shipped DDS]");
    }
}
