using Converter.Lemur.Entities;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

/// <summary>
/// Writes <c>history/cultures/&lt;culture_key&gt;.txt</c> — one per culture. CK3 binds each file to the
/// culture by FILENAME (per <c>_culture.info</c>: "Name of the file is the culture key"); the body is
/// dated effect blocks, no inner wrapper. We emit a single block at <see cref="L.Map.StartDate"/>:
/// a forced <c>join_era</c> plus one <c>discover_innovation</c> per entry in <c>Culture.Innovations</c>.
/// <c>join_era</c>/<c>discover_innovation</c> are forced and ignore era <c>year</c>, so this fully
/// determines each culture's start snapshot. See PLAN_tech_levels.md.
/// </summary>
public static class CultureHistoryWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing culture history (tech)");
        var dir = Helper.GetPath(outputDirectory, "history", "cultures");
        Directory.CreateDirectory(dir);

        var date = map.StartDate;
        foreach (var c in map.Cultures.Values.OrderBy(c => c.AzgaarId))
        {
            var lines = new List<string>
            {
                $"# {c.Name} — generated start era + innovations",
                $"{date} = {{",
                $"\tjoin_era = culture_era_{EraKey(c.Era)}",
            };
            foreach (var inno in c.Innovations.OrderBy(i => (int)i.Era).ThenBy(i => i.Key))
                lines.Add($"\tdiscover_innovation = {inno.Key}");
            lines.Add("}");

            var path = Helper.GetPath(dir, $"{c.CK3Key}.txt");
            await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        }

        Logger.Info($"Wrote tech history for {map.Cultures.Count} cultures → history/cultures/.");
    }

    private static string EraKey(CultureEra e) => e switch
    {
        CultureEra.Tribal => "tribal",
        CultureEra.EarlyMedieval => "early_medieval",
        CultureEra.HighMedieval => "high_medieval",
        CultureEra.LateMedieval => "late_medieval",
        _ => "tribal",
    };
}
