namespace Converter.Lemur.Writers;

/// <summary>
/// Re-supplies the map-independent layer definitions and map-table objects masked by our
/// replace_path on gfx/map/map_object_data (see <see cref="ModDescriptorWriter"/>); copied verbatim
/// from the CK3 install since our locators and the 3D map table depend on them.
///
/// NOT copied here:
///   - *_locators.txt        → written per-map by <see cref="LocatorWriter"/>
///   - map_table_western.txt → written by <see cref="MapTableWriter"/> (carries our Y-offset fix)
///   - decoration instances (bridges/lakes/cliffs/audio/env_effects) → map-specific; omitted rather
///     than shipped at wrong positions (absence is non-fatal, just no eye-candy)
/// </summary>
public static class MapObjectDataWriter
{
    // Map-independent definition files to lift from <ck3>/game/gfx/map/map_object_data/.
    private static readonly string[] DefinitionFiles =
    [
        "layers.txt",              // foliage/lake/tree/map-table layer declarations
        "game_object_layers.txt",  // building_layer, unit_layer, activities_layer
        "effect_layers.txt",       // coast_foam / env_effect layers
        "map_table_ce1.txt",       // 3D map-table object variants (western is ours via MapTableWriter)
        "map_table_ep3.txt",
        "map_table_tgp.txt",
    ];

    public static async Task Write(string ck3Directory, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing map_object_data definitions");

        var srcDir = Helper.GetPath(ck3Directory, "game", "gfx", "map", "map_object_data");
        var dstDir = Helper.GetPath(outputDirectory, "gfx", "map", "map_object_data");
        Directory.CreateDirectory(dstDir);

        var copied = 0;
        foreach (var file in DefinitionFiles)
        {
            var src = Helper.GetPath(srcDir, file);
            if (!File.Exists(src))
            {
                Logger.Warning($"map_object_data definition '{file}' not found in CK3 install — skipping (CK3 version drift?)");
                continue;
            }
            File.Copy(src, Helper.GetPath(dstDir, file), overwrite: true);
            copied++;
        }

        Logger.Info($"Copied {copied} map_object_data definition files from CK3 (layers + map tables)");
        await Task.CompletedTask;
    }
}
