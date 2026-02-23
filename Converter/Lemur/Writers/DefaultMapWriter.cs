namespace Converter.Lemur.Writers;

public static class DefaultMapWriter
{
    public static async Task Write(IEnumerable<int> seaZoneIndices, string outputDirectory)
    {
        var seaZoneList = string.Join(" ", seaZoneIndices);
        var content = $@"definitions = ""definition.csv""
provinces = ""provinces.png""
rivers = ""rivers.png""
topology = ""heightmap.heightmap""
adjacencies = ""adjacencies.csv""
island_region = ""island_region.txt""
seasons = ""seasons.txt""

sea_zones = LIST {{ {seaZoneList} }}

river_provinces = LIST {{ }}
";
        var path = Helper.GetPath(outputDirectory, "map_data", "default.map");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, Helper.Utf8Bom);
        Console.WriteLine($"Wrote default.map ({seaZoneList.Split(' ').Length} sea zones)");
    }
}
