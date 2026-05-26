using Converter.Lemur.Provinces;
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

        // Wastelands intentionally omitted — vanilla CK3 does not emit terrain entries for
        // impassable_mountains provinces; they inherit the default_land header.
        var baronies = map.Baronies!;

        for (int i = 0; i < baronies.Count; i++)
            lines.Add($"{i + 1}={baronies[i].Ck3Terrain.ToCk3String()}");

        var path = Helper.GetPath(outputDirectory, "common", "province_terrain", "00_province_terrain.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        Logger.Info($"Wrote 00_province_terrain.txt ({baronies.Count} baronies)");
    }
}
