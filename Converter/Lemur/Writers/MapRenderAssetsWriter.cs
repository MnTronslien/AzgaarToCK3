namespace Converter.Lemur.Writers;

/// <summary>
/// Ships the map-render rasters that suppress vanilla bleed-through on the standalone map:
///   * gfx/map/surround_map/  — decorative terrain outside the grid (vanilla shows real geography)
///   * gfx/map/water/          — water colour/flow/foam (vanilla's gives wrong sea topology+colour)
///   * gfx/map/map_object_data/generated/ — vegetation generators (replace_path'd by ModDescriptorWriter)
/// surround_map + water override vanilla by filename (no replace_path needed).
///
/// Copied verbatim from the local TCS install at convert time; the runtime mod stays TCS-free.
/// Phase B will synthesize these so the build needs no TCS. See bugs/BUG_tcs-breakaway-bootcrash.md.
/// </summary>
public static class MapRenderAssetsWriter
{
    // Directories copied verbatim from <tcs>/gfx/map/ into <mod>/gfx/map/.
    private static readonly string[] AssetDirs =
    [
        Path.Combine("surround_map"),
        Path.Combine("water"),
        Path.Combine("map_object_data", "generated"),
    ];

    public static async Task Write(string tcsSandboxPath, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing map render assets");

        if (string.IsNullOrWhiteSpace(tcsSandboxPath) || !Directory.Exists(tcsSandboxPath))
        {
            Logger.Warning("MapRenderAssetsWriter: TCS path unavailable — skipping surround_map/water/generated " +
                           "(map will show vanilla bleed around edges / sea until Phase B synthesizes these).");
            return;
        }

        var copied = 0;
        foreach (var rel in AssetDirs)
        {
            var srcDir = Helper.GetPath(tcsSandboxPath, "gfx", "map", rel);
            if (!Directory.Exists(srcDir))
            {
                Logger.Warning($"MapRenderAssetsWriter: '{rel}' not found in TCS install — skipping.");
                continue;
            }

            var dstDir = Helper.GetPath(outputDirectory, "gfx", "map", rel);
            Directory.CreateDirectory(dstDir);
            foreach (var src in Directory.EnumerateFiles(srcDir))
            {
                File.Copy(src, Helper.GetPath(dstDir, Path.GetFileName(src)), overwrite: true);
                copied++;
            }
        }

        Logger.Info($"Copied {copied} map render assets from TCS (surround_map + water + vegetation generators) [stopgap]");
        await Task.CompletedTask;
    }
}
