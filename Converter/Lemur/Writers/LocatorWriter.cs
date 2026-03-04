using System.Globalization;
using System.Text;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class LocatorWriter
{
    private record LocatorSpec(
        string FileName, string Name, string Layer,
        bool BurgsOnly, double OffsetX, double OffsetZ);

    private static readonly LocatorSpec[] Locators =
    {
        new("building_locators.txt",     "buildings",             "building_layer",  BurgsOnly: true,  OffsetX:   0, OffsetZ:   0),
        new("combat_locators.txt",       "combat",                "combat_layer",    BurgsOnly: true,  OffsetX:   0, OffsetZ:  10),
        new("siege_locators.txt",        "sieges",                "siege_layer",     BurgsOnly: true,  OffsetX:  10, OffsetZ:  -5),
        new("player_stack_locators.txt", "unit_stack_player_owned","unit_stack_layer",BurgsOnly: false, OffsetX: -10, OffsetZ:  -5),
    };

    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing locator files");

        var dir = Helper.GetPath(outputDirectory, "gfx", "map", "map_object_data");
        Directory.CreateDirectory(dir);

        foreach (var spec in Locators)
        {
            var content = BuildLocatorFile(spec, map);
            var path = Path.Combine(dir, spec.FileName);
            await File.WriteAllTextAsync(path, content, Helper.Utf8Bom);
        }

        Logger.Info($"Wrote {Locators.Length} locator files ({map.Baronies!.Count} baronies)");
    }

    private static string BuildLocatorFile(LocatorSpec spec, L.Map map)
    {
        var sb = new StringBuilder();
        sb.AppendLine("game_object_locator={");
        sb.AppendLine($"\tname=\"{spec.Name}\"");
        sb.AppendLine("\tclamp_to_water_level=yes");
        sb.AppendLine("\trender_under_water=no");
        sb.AppendLine("\tgenerated_content=no");
        sb.AppendLine($"\tlayer=\"{spec.Layer}\"");
        sb.AppendLine("\tinstances={");

        int id = 0;

        // Barony (burg) provinces — apply the per-type offset
        foreach (var barony in map.Baronies!)
        {
            var (px, pz) = ComputeCentroid(barony.Cells, map);
            AppendInstance(sb, id++, px + spec.OffsetX, pz + spec.OffsetZ);
        }

        // Non-burg land provinces (wastelands) — player_stack only, no offset
        if (!spec.BurgsOnly)
        {
            foreach (var wasteland in map.Wastelands!)
            {
                var (px, pz) = ComputeCentroid(wasteland.Cells, map);
                AppendInstance(sb, id++, px, pz);
            }
        }

        sb.AppendLine("\t}");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendInstance(StringBuilder sb, int id, double x, double z)
    {
        string xs = x.ToString("F6", CultureInfo.InvariantCulture);
        string zs = z.ToString("F6", CultureInfo.InvariantCulture);
        sb.AppendLine("\t\t{");
        sb.AppendLine($"\t\t\tid={id}");
        sb.AppendLine($"\t\t\tposition={{ {xs} 0.000000 {zs} }}");
        sb.AppendLine("\t\t\trotation={ 0.000000 0.000000 0.000000 1.000000 }");
        sb.AppendLine("\t\t\tscale={ 1.000000 1.000000 1.000000 }");
        sb.AppendLine("\t\t}");
    }

    /// <summary>
    /// Computes the centroid of a province in CK3 map pixel coordinates
    /// (mean of all polygon vertex coordinates across all cells).
    /// </summary>
    private static (double x, double z) ComputeCentroid(List<L.Cell> cells, L.Map map)
    {
        double sumX = 0, sumZ = 0;
        int count = 0;

        foreach (var cell in cells)
        {
            foreach (var vertex in cell.GeoDataCoordinates)
            {
                sumX += (vertex[0] - map.XOffset) * map.XRatio;
                sumZ += L.Map.MapHeight - (vertex[1] - map.YOffset) * map.YRatio;
                count++;
            }
        }

        if (count == 0)
            return (L.Map.MapWidth / 2.0, L.Map.MapHeight / 2.0);

        return (sumX / count, sumZ / count);
    }
}
