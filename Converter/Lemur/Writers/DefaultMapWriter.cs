namespace Converter.Lemur.Writers;

public static class DefaultMapWriter
{
    public static async Task Write(
        IEnumerable<int> seaZoneIndices,
        IEnumerable<int> wastelandIndices,
        IEnumerable<int> farSeaZoneIndices,
        IEnumerable<int> riverProvinceIndices,
        string outputDirectory)
    {
        var seaZoneList = string.Join(" ", seaZoneIndices);
        var wastelandList = string.Join(" ", wastelandIndices);
        var farSeaList = string.Join(" ", farSeaZoneIndices);
        var riverList = string.Join(" ", riverProvinceIndices);
        var content = $@"definitions = ""definition.csv""
provinces = ""provinces.png""
rivers = ""rivers.png""
topology = ""heightmap.heightmap""
adjacencies = ""adjacencies.csv""
island_region = ""island_region.txt""
seasons = ""seasons.txt""

sea_zones = LIST {{ {seaZoneList} }}

impassable_mountains = LIST {{ {wastelandList} }}

impassable_seas = LIST {{ {farSeaList} }}

river_provinces = LIST {{ {riverList} }}
";
        var path = Helper.GetPath(outputDirectory, "map_data", "default.map");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, Helper.Utf8Bom);
        Logger.Info($"Wrote default.map ({seaZoneList.Split(' ').Length} sea zones, {wastelandList.Split(' ').Length} impassable_mountains, {farSeaList.Split(' ').Length} impassable_seas, {riverList.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length} river_provinces)");
    }
}
