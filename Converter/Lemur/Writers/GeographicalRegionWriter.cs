using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class GeographicalRegionWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        var allProvinces = map.AllProvinces!;

        // Collect land province IDs (baronies + wastelands; not sea zones)
        var landIds = new List<int>();
        for (int i = 0; i < allProvinces.Count; i++)
        {
            if (allProvinces[i] is not L.SeaZone)
                landIds.Add(i + 1); // province IDs are 1-based
        }

        var lines = new List<string>
        {
            "# Lemur conversion: assign all land provinces to a visual geographical region.",
            "# This file is named z_... so it loads AFTER geographical_region.txt (LIOS),",
            "# overriding the empty graphical_western = {} from TCS.",
            "",
            "lemur_land_region = {",
            $"\tprovinces = {{ {string.Join(" ", landIds)} }}",
            "}",
            "",
            "graphical_western = {",
            "\tgraphical = yes",
            "\tcolor = { 255 0 0 }",
            "\tregions = {",
            "\t\tlemur_land_region",
            "\t}",
            "}",
        };

        var path = Helper.GetPath(outputDirectory, "map_data", "geographical_regions", "z_lemur_geographical_regions.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        Console.WriteLine($"Wrote z_lemur_geographical_regions.txt ({landIds.Count} land provinces)");
    }
}
