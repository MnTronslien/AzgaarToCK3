using System.Text.RegularExpressions;
using Converter.Lemur.Entities;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

/// <summary>
/// Emits the two start-relative date files for the tech feature (PLAN_tech_levels.md):
/// <list type="bullet">
/// <item><b>Era override</b> → <c>common/culture/eras/00_culture_eras.txt</c>. CK3 culture-era
/// definitions REPLACE per top-level key (they don't merge sub-fields), so to keep each era's
/// modifiers / maa_upgrades / invalid_for_government we copy the live vanilla file verbatim and
/// rewrite only the <c>year</c> per block. Reading vanilla each run avoids the staleness of a baked
/// copy. Tribal stays 0.</item>
/// <item><b>END_DATE override</b> → <c>common/defines/00_lemur_tech_defines.txt</c>. Defines merge per
/// key within a namespace, so a partial <c>NGame = { END_DATE = ... }</c> overrides only END_DATE.</item>
/// </list>
/// </summary>
public static class TechDatesWriter
{
    public static async Task Write(L.Map map, string ck3Directory, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing tech era/END_DATE overrides");
        var s = Settings.Instance;
        int start = map.StartDate.Year;
        var baseline = s.WorldTechLevel;
        int step = s.YearsBetweenEras;

        await WriteEraOverride(ck3Directory, outputDirectory, start, baseline, step);
        await WriteEndDate(outputDirectory, start, baseline, step);
    }

    private static async Task WriteEraOverride(
        string ck3Directory, string outputDirectory, int start, CultureEra baseline, int step)
    {
        var src = Helper.GetPath(ck3Directory, "game", "common", "culture", "eras", "00_culture_eras.txt");
        if (!File.Exists(src))
        {
            Logger.Warning($"Tech era override: vanilla 00_culture_eras.txt not found at '{src}'; skipping (post-start tech progression keeps vanilla absolute years).");
            return;
        }

        var text = await File.ReadAllTextAsync(src);
        foreach (var era in new[] { CultureEra.Tribal, CultureEra.EarlyMedieval, CultureEra.HighMedieval, CultureEra.LateMedieval })
        {
            int year = TechDates.EraYear(era, start, baseline, step);
            text = ReplaceEraYear(text, EraKey(era), year);
        }

        var dir = Helper.GetPath(outputDirectory, "common", "culture", "eras");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Helper.GetPath(dir, "00_culture_eras.txt"), text, Helper.Utf8Bom);
        Logger.Info($"Wrote era override: baseline={baseline} at {start}, step={step} " +
            $"(early={TechDates.EraYear(CultureEra.EarlyMedieval, start, baseline, step)}, " +
            $"high={TechDates.EraYear(CultureEra.HighMedieval, start, baseline, step)}, " +
            $"late={TechDates.EraYear(CultureEra.LateMedieval, start, baseline, step)}).");
    }

    private static async Task WriteEndDate(
        string outputDirectory, int start, CultureEra baseline, int step)
    {
        int end = TechDates.EndYear(start, baseline, step);
        var dir = Helper.GetPath(outputDirectory, "common", "defines");
        Directory.CreateDirectory(dir);
        var content = "# Generated: campaign end paired with the start date + tech ladder.\n" +
                      "NGame = {\n" +
                      $"\tEND_DATE = \"{end}.1.1\"\n" +
                      "}\n";
        await File.WriteAllTextAsync(Helper.GetPath(dir, "00_lemur_tech_defines.txt"), content, Helper.Utf8Bom);
        Logger.Info($"Wrote END_DATE override: {end}.1.1.");
    }

    // Replace the first 'year = N' inside the named era block. The year precedes any nested '{', so
    // [^{}] safely confines the match to the block header before its modifier/maa_upgrade sub-blocks.
    private static string ReplaceEraYear(string text, string eraKey, int year)
    {
        var rx = new Regex($@"({Regex.Escape(eraKey)}\s*=\s*\{{[^{{}}]*?\byear\s*=\s*)\d+");
        return rx.Replace(text, m => m.Groups[1].Value + year, 1);
    }

    private static string EraKey(CultureEra e) => e switch
    {
        CultureEra.Tribal => "culture_era_tribal",
        CultureEra.EarlyMedieval => "culture_era_early_medieval",
        CultureEra.HighMedieval => "culture_era_high_medieval",
        CultureEra.LateMedieval => "culture_era_late_medieval",
        _ => "culture_era_tribal",
    };
}
