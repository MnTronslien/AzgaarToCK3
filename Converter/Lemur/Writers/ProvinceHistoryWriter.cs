using Converter.Lemur;
using Converter.Lemur.Entities;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class ProvinceHistoryWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing province history");

        var faiths = map.Faiths;
        var cultures = map.Cultures;
        string FallbackFaith() => faiths.Values.FirstOrDefault()?.CK3Key ?? "lemur_faith_1";
        string FallbackCulture() => cultures.Values.FirstOrDefault()?.CK3Key ?? "lemur_culture_1";

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
                $"# Lemur: province history for {kingdom.Name}.",
                ""
            };

            foreach (var duchy in kingdom.Duchies)
            foreach (var county in duchy.Counties)
            foreach (var barony in county.Baronies!)
            {
                if (!baroniesProvId.TryGetValue(barony, out int provId)) continue;

                var azgaarReligion = barony.GetDominantReligion(map);
                var faithKey = faiths.TryGetValue(azgaarReligion.i, out var faith)
                    ? faith.CK3Key
                    : FallbackFaith();

                var azgaarCulture = barony.GetDominantCulture(map);
                var cultureKey = (azgaarCulture.i > 0 && cultures.TryGetValue(azgaarCulture.i, out var culture))
                    ? culture.CK3Key
                    : FallbackCulture();

                lines.Add($"{provId} = {{");
                lines.Add($"\tculture = {cultureKey}");
                lines.Add($"\treligion = {faithKey}");
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
