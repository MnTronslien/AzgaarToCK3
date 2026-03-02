using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class ProvinceHistoryWriter
{
    // Placeholder culture/religion so CK3 can initialise the world without crashing.
    // TODO: replace with Azgaar culture/religion data per barony.
    private const string PlaceholderCulture = "english";
    private const string PlaceholderReligion = "catholic";

    public static async Task Write(L.Map map, string outputDirectory)
    {
        // AllProvinces order: baronies first, then wastelands, then sea zones.
        // Province IDs are 1-based (index 0 → ID 1).
        // Only baronies need history entries (holdings, culture, religion).
        var baronies = map.Baronies!;

        var lines = new List<string>
        {
            "# Lemur: placeholder province history — english/catholic for all baronies.",
            "# TODO: derive culture and religion from Azgaar map data.",
            ""
        };

        for (int i = 0; i < baronies.Count; i++)
        {
            int provinceId = i + 1;
            lines.Add($"{provinceId} = {{");
            lines.Add($"\tculture = {PlaceholderCulture}");
            lines.Add($"\treligion = {PlaceholderReligion}");
            lines.Add("\tholding = auto");
            lines.Add("}");
            lines.Add("");
        }

        var path = Helper.GetPath(outputDirectory, "history", "provinces", "00_lemur_provinces.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        Logger.Info($"Wrote 00_lemur_provinces.txt ({baronies.Count} baronies)");
    }
}
