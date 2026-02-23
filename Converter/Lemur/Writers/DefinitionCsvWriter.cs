using Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class DefinitionCsvWriter
{
    public static async Task Write(List<IProvince> allProvinces, string outputDirectory)
    {
        var lines = new List<string> { "0;0;0;0;Black - Impassable;x;" };
        for (int i = 0; i < allProvinces.Count; i++)
        {
            var p = allProvinces[i];
            lines.Add($"{i + 1};{p.Color.R};{p.Color.G};{p.Color.B};{p.Name};x;");
        }
        var path = Helper.GetPath(outputDirectory, "map_data", "definition.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        Console.WriteLine($"Wrote definition.csv ({lines.Count} entries)");
    }
}
