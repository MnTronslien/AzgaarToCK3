using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

// Override vanilla's gfx/map/map_object_data/map_table_western.txt so the 3D table
// doesn't occlude the flatmap at maximum zoom-out (see BUG_flatmap-missing-at-zoom).
//
// Two changes vs vanilla:
// 1. All object Y values lowered to -100 (vanilla: -15/-1/-20/-1). With
//    clamp_to_water_level=yes, the object's effective Y appears to be
//    max(table_Y + local_heightmap_world_Y, WATERLEVEL). Local heightmap can range
//    0..WORLD_EXTENTS_Y (51), so to guarantee clamping to WATERLEVEL regardless of
//    where the table sits over the map: table_Y < WATERLEVEL - 51 = -48. Y=-100
//    gives a comfortable safety margin.
// 2. Table recentered to (MapWidth/2, MapHeight/2) — our map is 8192×4096 centered at
//    (4096, 2048); vanilla's table sat at (4500, 2560), biased toward where Western
//    Europe sits on the vanilla world map. The recenter exposed why earlier Y=-10
//    only worked at vanilla offset: vanilla position happened to sit over low
//    heightmap (~byte 53), so the heightmap-aware clamp pulled Y to WATERLEVEL.
//    Recentered position sits over high heightmap (~byte 200) on Trimoyers, so
//    Y=-10 + ~40 was still 30 above WATERLEVEL — table visible, occluding flatmap.
//
// Follow-up (notes-for-later.md): consider clamp_to_water_level=no instead, which
// would decouple the table from heightmap entirely and allow a smaller Y offset.
// Untested — might break other engine behaviour we haven't characterised.
//
// MVP scope: _western only. The ce1/ep3/tgp tables won't activate for our generated
// cultures (no Asian / Byzantine heritage pillars). Follow-up in the bug doc.
public static class MapTableWriter
{
    public static async Task Write(string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing map_table overrides");

        var dir = Helper.GetPath(outputDirectory, "gfx", "map", "map_object_data");
        Directory.CreateDirectory(dir);

        double tableX = L.Map.MapWidth / 2.0;
        double tableY = -100.0;
        double tableZ = L.Map.MapHeight / 2.0;
        var transform = $"{tableX:F6} {tableY:F6} {tableZ:F6} 0.000000 0.000000 0.000000 0.000000 5.000000 5.000000 5.000000";

        // Four objects from vanilla — tabletop, candles, cloth, props — all share
        // identical render_pass / clamp / layer settings; only name + entity differ.
        var westernContent =
            BuildObject("western_tabletop",            "tabletop_west_basic_entity",      transform) +
            BuildObject("western_tabletop_candles_01", "tabletop_west_basic_candles_entity", transform) +
            BuildObject("western_tabletop_cloth",      "tabletop_west_basic_tablecloth_entity", transform) +
            BuildObject("western_tabletop_props_01",   "tabletop_west_basic_props_entity",    transform);

        var path = Helper.GetPath(dir, "map_table_western.txt");
        await File.WriteAllTextAsync(path, westernContent, Helper.Utf8Bom);
        Logger.Info($"Wrote map_table_western.txt (4 objects, recentered to ({tableX:F0}, {tableZ:F0}), Y={tableY:F0})");
    }

    private static string BuildObject(string name, string entity, string transform) =>
        "object={\n" +
        $"\tname=\"{name}\"\n" +
        "\trender_pass=MapUnderTerrain\n" +
        "\tclamp_to_water_level=yes\n" +
        "\tgenerated_content=no\n" +
        "\tlayer=\"map_table_layer_western\"\n" +
        $"\tentity=\"{entity}\"\n" +
        "\tcount=1\n" +
        $"\ttransform=\"{transform}\n" +
        "\"}\n";
}
