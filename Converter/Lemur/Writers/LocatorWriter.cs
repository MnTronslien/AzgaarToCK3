using System.Globalization;
using System.Text;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class LocatorWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing locator files");

        var dir = Helper.GetPath(outputDirectory, "gfx", "map", "map_object_data");
        Directory.CreateDirectory(dir);

        await Task.WhenAll(
            WriteFile(dir, BuildBuildingLocators(map)),
            WriteFile(dir, BuildSpecialBuildingLocators(map)),
            WriteFile(dir, BuildSiegeLocators(map)),
            WriteFile(dir, BuildCombatLocators(map)),
            WriteFile(dir, BuildActivitiesLocators(map)),
            WriteFile(dir, BuildPlayerStackLocators(map)),
            WriteFile(dir, BuildStackLocators(map)),
            WriteFile(dir, BuildOtherStackLocators(map)));

        Logger.Info($"Wrote 8 locator files ({map.Baronies!.Count} baronies)");

        if (Settings.Instance.GenerateDebugImages)
            await ImageUtility.DrawAllLocatorsDebugImage(map);
    }

    // Burg-directional locators (buildings, special building, siege):
    // position = burg nudged 20px toward cell centroid, with optional spread.

    private static (string fileName, string content) BuildBuildingLocators(L.Map map)
    {
        int id = 1;
        var sb = StartLocatorFile("buildings", "building_layer", clampToWaterLevel: true);
        foreach (var barony in map.Baronies!)
        {
            var p = Helper.CanvasToWorld(barony.burg.Position, map);
            AppendInstance(sb, id++, p.X, p.Z);
        }
        return ("building_locators.txt", EndLocatorFile(sb));
    }

    private static (string fileName, string content) BuildSpecialBuildingLocators(L.Map map)
    {
        int id = 1;
        var sb = StartLocatorFile("special_building", "building_layer", clampToWaterLevel: true);
        foreach (var barony in map.Baronies!)
        {
            var (x, z) = BurgNudgedTowardCentroid(barony, map);
            var (perpX, perpZ) = PerpendicularTowardCentroid(barony, map);
            AppendInstance(sb, id++, x - perpX * 10, z - perpZ * 10);
        }
        return ("special_building_locators.txt", EndLocatorFile(sb));
    }

    private static (string fileName, string content) BuildSiegeLocators(L.Map map)
    {
        int id = 1;
        var sb = StartLocatorFile("siege", "unit_layer", clampToWaterLevel: false);
        foreach (var barony in map.Baronies!)
        {
            var (x, z) = BurgNudgedTowardCentroid(barony, map);
            AppendInstance(sb, id++, x, z);
        }
        return ("siege_locators.txt", EndLocatorFile(sb));
    }

    // Centroid-based locators (combat, activities, player stack):
    // position = cell centroid + fixed pixel offset.

    private static (string fileName, string content) BuildCombatLocators(L.Map map)
    {
        int id = 1;
        var sb = StartLocatorFile("combat", "unit_layer", clampToWaterLevel: true);
        foreach (var barony in map.Baronies!)
        {
            var (x, z) = ComputeCentroid(barony.Cells, map);
            AppendInstance(sb, id++, x + 15, z + 10);
        }
        foreach (var wasteland in map.Wastelands!)
        {
            var (x, z) = ComputeCentroid(wasteland.Cells, map);
            AppendInstance(sb, id++, x + 15, z + 10);
        }
        // Sea zones too — CK3 expects a locator per province, else it flags it incomplete.
        foreach (var sea in map.SeaZones!.Concat(map.FarSeaZones!))
        {
            var (x, z) = ComputeCentroid(sea.Cells, map);
            AppendInstance(sb, id++, x + 15, z + 10);
        }
        return ("combat_locators.txt", EndLocatorFile(sb));
    }

    private static (string fileName, string content) BuildActivitiesLocators(L.Map map)
    {
        int id = 1;
        var sb = StartLocatorFile("activities", "activities_layer", clampToWaterLevel: false);
        foreach (var barony in map.Baronies!)
        {
            var (x, z) = ComputeCentroid(barony.Cells, map);
            AppendInstance(sb, id++, x - 10, z + 15);
        }
        foreach (var wasteland in map.Wastelands!)
        {
            var (x, z) = ComputeCentroid(wasteland.Cells, map);
            AppendInstance(sb, id++, x - 10, z + 15);
        }
        // Sea zones too, so the locator covers every province and CK3 doesn't flag it incomplete.
        foreach (var sea in map.SeaZones!.Concat(map.FarSeaZones!))
        {
            var (x, z) = ComputeCentroid(sea.Cells, map);
            AppendInstance(sb, id++, x - 10, z + 15);
        }
        return ("activities.txt", EndLocatorFile(sb));
    }

    private static (string fileName, string content) BuildPlayerStackLocators(L.Map map) =>
        BuildAllProvinceStackLocator(map, "unit_stack_player_owned", "player_stack_locators.txt");

    private static (string fileName, string content) BuildStackLocators(L.Map map) =>
        BuildAllProvinceStackLocator(map, "unit_stack", "stack_locators.txt");

    private static (string fileName, string content) BuildOtherStackLocators(L.Map map) =>
        BuildAllProvinceStackLocator(map, "unit_stack_other_owner", "other_stack_locators.txt");

    private static (string fileName, string content) BuildAllProvinceStackLocator(L.Map map, string locatorName, string fileName)
    {
        int id = 1;
        var sb = StartLocatorFile(locatorName, "unit_layer", clampToWaterLevel: true);
        foreach (var barony in map.Baronies!)
        {
            var (x, z) = ComputeCentroid(barony.Cells, map);
            AppendInstance(sb, id++, x, z);
        }
        foreach (var wasteland in map.Wastelands!)
        {
            var (x, z) = ComputeCentroid(wasteland.Cells, map);
            AppendInstance(sb, id++, x, z);
        }
        foreach (var sea in map.SeaZones!.Concat(map.FarSeaZones!))
        {
            var (x, z) = ComputeCentroid(sea.Cells, map);
            AppendInstance(sb, id++, x, z);
        }
        return (fileName, EndLocatorFile(sb));
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private static async Task WriteFile(string dir, (string fileName, string content) locator)
    {
        var path = Path.Combine(dir, locator.fileName);
        await File.WriteAllTextAsync(path, locator.content, Helper.Utf8Bom);
    }

    private static StringBuilder StartLocatorFile(string name, string layer, bool clampToWaterLevel)
    {
        var sb = new StringBuilder();
        sb.AppendLine("game_object_locator={");
        sb.AppendLine($"\tname=\"{name}\"");
        sb.AppendLine("\trender_pass=Map"); // required since CK3 1.18, else the renderer skips these objects
        sb.AppendLine($"\tclamp_to_water_level={(clampToWaterLevel ? "yes" : "no")}");
        sb.AppendLine("\tgenerated_content=no");
        sb.AppendLine($"\tlayer=\"{layer}\"");
        sb.AppendLine("\tinstances={");
        // Seed dummy province id 0 (per-province loops start at id 1); else CK3 logs
        // "Failed to get transform ... instance id 0".
        AppendInstance(sb, 0, 3, 5);
        return sb;
    }

    private static string EndLocatorFile(StringBuilder sb)
    {
        sb.AppendLine("\t}");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendInstance(StringBuilder sb, int id, double x, double z)
    {
        sb.AppendLine("\t\t{");
        sb.AppendLine($"\t\t\tid={id}");
        sb.AppendLine($"\t\t\tposition={{ {x.ToString("F6", CultureInfo.InvariantCulture)} 0.000000 {z.ToString("F6", CultureInfo.InvariantCulture)} }}");
        sb.AppendLine("\t\t\trotation={ 0.000000 0.000000 0.000000 1.000000 }");
        sb.AppendLine("\t\t\tscale={ 1.000000 1.000000 1.000000 }");
        sb.AppendLine("\t\t}");
    }

    /// <summary>
    /// Burg position nudged 20px toward the cell centroid (keeps coastal burgs inland).
    /// Falls back to the bare burg position when burg and centroid are within 5px.
    /// </summary>
    internal static (double x, double z) BurgNudgedTowardCentroid(L.Barony barony, L.Map map)
    {
        var burg = Helper.CanvasToWorld(barony.burg.Position, map);
        var (cx, cz) = ComputeCentroid(barony.Cells, map);

        double dx = cx - burg.X;
        double dz = cz - burg.Z;
        double length = Math.Sqrt(dx * dx + dz * dz);

        if (length < 5)
            return (burg.X, burg.Z);

        dx /= length;
        dz /= length;
        return (burg.X + dx * 20, burg.Z + dz * 20);
    }

    /// <summary>
    /// Unit vector perpendicular to the burg→centroid direction (90° clockwise), used to spread
    /// building/special_building locators sideways. Falls back to a fixed rightward vector within 5px.
    /// </summary>
    internal static (double x, double z) PerpendicularTowardCentroid(L.Barony barony, L.Map map)
    {
        var burg = Helper.CanvasToWorld(barony.burg.Position, map);
        var (cx, cz) = ComputeCentroid(barony.Cells, map);

        double dx = cx - burg.X;
        double dz = cz - burg.Z;
        double length = Math.Sqrt(dx * dx + dz * dz);

        if (length < 5)
            return (1, 0); // fallback: fixed rightward spread so building/special_building don't stack

        dx /= length;
        dz /= length;
        return (dz, -dx); // 90° clockwise rotation
    }

    /// <summary>
    /// Mean of all cell polygon vertex coordinates, in CK3 map pixel space.
    /// </summary>
    internal static (double x, double z) ComputeCentroid(List<L.Cell> cells, L.Map map)
    {
        double sumX = 0, sumZ = 0;
        int count = 0;

        // Sort by cell ID so summation order is independent of cell insertion order (FP determinism).
        foreach (var cell in cells.OrderBy(c => c.Id))
            foreach (var vertex in cell.GeoDataCoordinates)
            {
                var w = Helper.GeoToWorld(new GeoPoint(vertex[0], vertex[1]), map); // world Z, north = high (no flip)
                sumX += w.X;
                sumZ += w.Z;
                count++;
            }

        return count == 0
            ? (L.Map.MapWidth / 2.0, L.Map.MapHeight / 2.0)
            : (sumX / count, sumZ / count);
    }
}
