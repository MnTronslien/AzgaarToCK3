using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class ProvinceTerrainWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        var lines = new List<string>
        {
            "default_land=plains",
            "default_sea=sea",
            "default_coastal_sea=coastal_sea",
        };

        // Write terrain for baronies and wastelands (land provinces only — no sea zones)
        var baronies = map.Baronies!;
        var wastelands = map.Wastelands!;

        for (int i = 0; i < baronies.Count; i++)
            lines.Add($"{i + 1}=plains");

        for (int i = 0; i < wastelands.Count; i++)
            lines.Add($"{baronies.Count + i + 1}=plains");

        var path = Helper.GetPath(outputDirectory, "common", "province_terrain", "00_province_terrain.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        Console.WriteLine($"Wrote 00_province_terrain.txt ({baronies.Count + wastelands.Count} land provinces)");
    }
}
