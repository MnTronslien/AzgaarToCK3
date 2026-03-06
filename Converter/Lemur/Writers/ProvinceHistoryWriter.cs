using Converter.Lemur;
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
        using var _ = OperationTimer.Start("Writing province history");

        // Build barony → province ID lookup (1-based, baronies are first in AllProvinces).
        var baronies = map.Baronies!;
        var baroniesProvId = new Dictionary<L.Barony, int>(baronies.Count);
        for (int i = 0; i < baronies.Count; i++)
            baroniesProvId[baronies[i]] = i + 1;

        var dir = Helper.GetPath(outputDirectory, "history", "provinces");
        Directory.CreateDirectory(dir);

        int totalBaronies = 0;
        var tasks = new List<Task>();

        foreach (var kingdom in map.Kingdoms)
        {
            var lines = new List<string>
            {
                $"# Lemur: placeholder province history for {kingdom.Name}.",
                ""
            };

            foreach (var duchy in kingdom.Duchies)
            foreach (var county in duchy.Counties)
            foreach (var barony in county.Baronies!)
            {
                if (!baroniesProvId.TryGetValue(barony, out int provId)) continue;
                lines.Add($"{provId} = {{");
                lines.Add($"\tculture = {PlaceholderCulture}");
                lines.Add($"\treligion = {PlaceholderReligion}");
                lines.Add("\tholding = auto");
                lines.Add("}");
                lines.Add("");
                totalBaronies++;
            }

            var fileName = LandedTitlesWriter.ToCk3Id("k", kingdom.Name, kingdom.Id) + ".txt";
            var path = Path.Combine(dir, fileName);
            tasks.Add(File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom));
        }

        await Task.WhenAll(tasks);
        Logger.Info($"Wrote {map.Kingdoms.Count} province history files ({totalBaronies} baronies)");
    }
}
