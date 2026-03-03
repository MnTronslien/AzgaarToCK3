using System.Globalization;
using System.Text;
using Converter.Lemur;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class LocatorWriter
{
    private static readonly string[] LocatorFileNames =
    {
        "building_locators.txt",
        "combat_locators.txt",
        "player_stack_locators.txt",
        "siege_locators.txt",
    };

    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing locator files");
        var baronies = map.Baronies!;

        // Build the instances block once — all four locator files have identical content.
        var content = BuildLocatorContent(baronies, map);

        foreach (var fileName in LocatorFileNames)
        {
            var path = Helper.GetPath(outputDirectory, "gfx", "map", "map_object_data", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, Helper.Utf8Bom);
        }

        Logger.Info($"Wrote {LocatorFileNames.Length} locator files ({baronies.Count} baronies)");
    }

    private static string BuildLocatorContent(List<L.Barony> baronies, L.Map map)
    {
        var sb = new StringBuilder();
        sb.AppendLine("instances={");

        for (int i = 0; i < baronies.Count; i++)
        {
            var barony = baronies[i];
            var (pixelX, pixelZ) = ComputeCentroid(barony, map);

            string x = pixelX.ToString("F6", CultureInfo.InvariantCulture);
            string z = pixelZ.ToString("F6", CultureInfo.InvariantCulture);

            sb.AppendLine("\t{");
            sb.AppendLine($"\t\tid={i}");
            sb.AppendLine($"\t\tposition={{ {x} 0.000000 {z} }}");
            sb.AppendLine("\t\trotation={ 0.000000 0.000000 0.000000 1.000000 }");
            sb.AppendLine("\t\tscale={ 1.000000 1.000000 1.000000 }");
            sb.AppendLine("\t}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Returns the barony centroid in CK3 map pixel coordinates.
    /// x = column (0..MapWidth-1), z = row (0..MapHeight-1, top = 0).
    ///
    /// Coordinate conversion mirrors ImageUtility.GenerateCellPolygons:
    ///   pixel_x = (lon - map.XOffset) * map.XRatio
    ///   pixel_z = MapHeight - (lat - map.YOffset) * map.YRatio
    ///
    /// GeoDataCoordinates is a float[][] of polygon vertices [lon, lat].
    /// The centroid is the mean of all polygon vertex coordinates across all cells.
    /// </summary>
    private static (double x, double z) ComputeCentroid(L.Barony barony, L.Map map)
    {
        double sumX = 0;
        double sumZ = 0;
        int count = 0;

        foreach (var cell in barony.Cells)
        {
            foreach (var vertex in cell.GeoDataCoordinates)
            {
                float lon = vertex[0];
                float lat = vertex[1];

                double px = (lon - map.XOffset) * map.XRatio;
                double pz = L.Map.MapHeight - (lat - map.YOffset) * map.YRatio;

                sumX += px;
                sumZ += pz;
                count++;
            }
        }

        if (count == 0)
        {
            // Fallback: place at map center if the barony somehow has no cells.
            return (L.Map.MapWidth / 2.0, L.Map.MapHeight / 2.0);
        }

        return (sumX / count, sumZ / count);
    }
}
