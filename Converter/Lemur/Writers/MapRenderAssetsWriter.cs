namespace Converter.Lemur.Writers;

/// <summary>
/// Ships the map-render rasters that make the standalone map look right — the things vanilla would
/// otherwise bleed through when TCS isn't in the playset:
///   * gfx/map/surround_map/   — the decorative terrain *outside* the province grid. Vanilla's shows
///                               real-world geography ("Siberia") faded around our map; TCS replaces
///                               the mask/fade with near-blank versions to neutralize it.
///   * gfx/map/water/          — water colour / flow / foam. Vanilla's gives wrong sea topology+colour.
///   * gfx/map/map_object_data/generated/ — vegetation generators. Replaced (see ModDescriptorWriter)
///                               so vanilla's don't place trees at vanilla-map positions.
///
/// STOPGAP: these are copied verbatim from the local TCS install at convert time (same build-time
/// source we already use for colormap.dds). That keeps the *runtime* mod TCS-free (the files end up
/// in the mod) while we still build against an installed TCS. Phase B replaces this with
/// synthesized / vanilla-derived assets so the build needs no TCS either. See
/// bugs/BUG_tcs-breakaway-bootcrash.md (visual-gap follow-ups).
///
/// surround_map + water override vanilla by filename (no replace_path needed); /generated is
/// replace_path'd by ModDescriptorWriter.
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
