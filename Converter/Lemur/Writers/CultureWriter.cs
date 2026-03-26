using Converter.Lemur.Entities;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class CultureWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing culture files");

        var cultures = map.Cultures;
        if (cultures.Count == 0)
        {
            Logger.Warning("CultureWriter: no cultures found, skipping.");
            return;
        }

        await Task.WhenAll(
            WriteCultureDefinitionsFile(cultures, outputDirectory),
            WriteHeritagePillarsFile(cultures, outputDirectory),
            WriteLanguagePillarsFile(cultures, outputDirectory),
            WriteCultureHistoryFile(cultures, outputDirectory),
            WriteCultureLocalizationFile(cultures, outputDirectory)
        );

        Logger.Info($"Wrote {cultures.Count} cultures.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // common/culture/cultures/lemur_cultures.txt
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task WriteCultureDefinitionsFile(
        Dictionary<int, L.Culture> cultures,
        string outputDirectory)
    {
        var dir = Helper.GetPath(outputDirectory, "common", "culture", "cultures");
        Directory.CreateDirectory(dir);

        var lines = new List<string>
        {
            "# Lemur conversion: generated culture definitions.",
            ""
        };

        foreach (var culture in cultures.Values.OrderBy(c => c.AzgaarId))
        {
            var (r, g, b) = ParseHexColor(culture.HexColor);

            lines.Add($"# {culture.Name}");
            lines.Add($"{culture.CK3Key} = {{");
            lines.Add($"\tethos = {culture.Ethos}");
            lines.Add($"\theritage = {culture.Heritage}");
            lines.Add($"\tlanguage = {culture.Language}");
            lines.Add($"\tmartial_custom = {culture.MartialCustom}");
            lines.Add($"\thead_determination = {culture.HeadDetermination}");
            lines.Add($"\tname_list = {culture.ThemeBundle.NameList}");

            if (culture.Parents.Count > 0)
            {
                lines.Add($"\tparents = {{ {string.Join(" ", culture.Parents.Select(p => p.CK3Key))} }}");
            }

            lines.Add("\ttraditions = {");
            foreach (var tradition in culture.Traditions)
                lines.Add($"\t\t{tradition}");
            lines.Add("\t}");

            lines.Add($"\tcolor = {{ {r} {g} {b} }}");

            lines.Add($"\tcoa_gfx = {{ {culture.ThemeBundle.CoaGfx} }}");
            lines.Add($"\tbuilding_gfx = {{ {culture.ThemeBundle.BuildingGfx} }}");
            lines.Add($"\tclothing_gfx = {{ {culture.ThemeBundle.ClothingGfx} }}");
            lines.Add($"\tunit_gfx = {{ {culture.ThemeBundle.UnitGfx} }}");

            lines.Add("\tethnicities = {");
            foreach (var (weight, key) in culture.Ethnicity)
                lines.Add($"\t\t{weight} = {key}");
            lines.Add("\t}");

            lines.Add("}");
            lines.Add("");
        }

        var path = Helper.GetPath(dir, "lemur_cultures.txt");
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // common/culture/pillars/lemur_heritages.txt
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task WriteHeritagePillarsFile(
        Dictionary<int, L.Culture> cultures,
        string outputDirectory)
    {
        var dir = Helper.GetPath(outputDirectory, "common", "culture", "pillars");
        Directory.CreateDirectory(dir);

        var lines = new List<string>
        {
            "# Lemur conversion: generated heritage pillars.",
            ""
        };

        // Only foundational cultures get their own heritage pillar
        var distinctHeritages = cultures.Values
            .Where(c => c.Heritage == $"lemur_heritage_{c.AzgaarId}")
            .OrderBy(c => c.AzgaarId)
            .ToList();

        foreach (var culture in distinctHeritages)
        {
            lines.Add($"# {culture.Name}");
            lines.Add($"{culture.Heritage} = {{");
            lines.Add("\ttype = heritage");
            lines.Add("\tis_shown = {");
            lines.Add("\t\theritage_is_shown_trigger = {");
            lines.Add($"\t\t\tHERITAGE = {culture.Heritage}");
            lines.Add("\t\t}");
            lines.Add("\t}");
            lines.Add("\taudio_parameter = european");
            lines.Add("}");
            lines.Add("");
        }

        var path = Helper.GetPath(dir, "lemur_heritages.txt");
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // common/culture/pillars/lemur_languages.txt
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task WriteLanguagePillarsFile(
        Dictionary<int, L.Culture> cultures,
        string outputDirectory)
    {
        var dir = Helper.GetPath(outputDirectory, "common", "culture", "pillars");
        Directory.CreateDirectory(dir);

        var lines = new List<string>
        {
            "# Lemur conversion: generated language pillars.",
            ""
        };

        // Only foundational cultures get their own language pillar
        var distinctLanguages = cultures.Values
            .Where(c => c.Language == $"lemur_language_{c.AzgaarId}")
            .OrderBy(c => c.AzgaarId)
            .ToList();

        foreach (var culture in distinctLanguages)
        {
            lines.Add($"# {culture.Name}");
            lines.Add($"{culture.Language} = {{");
            lines.Add("\ttype = language");
            lines.Add("\tis_shown = {");
            lines.Add("\t\tlanguage_is_shown_trigger = {");
            lines.Add($"\t\t\tLANGUAGE = {culture.Language}");
            lines.Add("\t\t}");
            lines.Add("\t}");
            lines.Add("\tai_will_do = {");
            lines.Add("\t\tvalue = 10");
            lines.Add("\t\tif = {");
            lines.Add($"\t\t\tlimit = {{ has_cultural_pillar = {culture.Language} }}");
            lines.Add("\t\t\tmultiply = 10");
            lines.Add("\t\t}");
            lines.Add("\t}");
            lines.Add("}");
            lines.Add("");
        }

        var path = Helper.GetPath(dir, "lemur_languages.txt");
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // history/cultures/lemur_cultures.txt
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task WriteCultureHistoryFile(
        Dictionary<int, L.Culture> cultures,
        string outputDirectory)
    {
        var dir = Helper.GetPath(outputDirectory, "history", "cultures");
        Directory.CreateDirectory(dir);

        var lines = new List<string>
        {
            "# Lemur conversion: generated culture history.",
            ""
        };

        foreach (var culture in cultures.Values.OrderBy(c => c.AzgaarId))
        {
            lines.Add($"# {culture.Name}");
            lines.Add($"{culture.CK3Key} = {{");
            if (culture.CreationDate != null)
                lines.Add($"\tcreated = {culture.CreationDate}");
            lines.Add("}");
            lines.Add("");
        }

        var path = Helper.GetPath(dir, "lemur_cultures.txt");
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // localization/english/lemur_cultures_l_english.yml
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task WriteCultureLocalizationFile(
        Dictionary<int, L.Culture> cultures,
        string outputDirectory)
    {
        var dir = Helper.GetPath(outputDirectory, "localization", "english");
        Directory.CreateDirectory(dir);

        var lines = new List<string> { "l_english:" };

        foreach (var culture in cultures.Values.OrderBy(c => c.AzgaarId))
        {
            lines.Add($" {culture.CK3Key}:0 \"{culture.Name}\"");
            lines.Add($" {culture.CK3Key}_collective_noun:0 \"{culture.Name}s\"");
            lines.Add($" {culture.CK3Key}_prefix:0 \"{culture.Name}\"");

            // Heritage loc (only if this culture owns it)
            if (culture.Heritage == $"lemur_heritage_{culture.AzgaarId}")
            {
                lines.Add($" {culture.Heritage}:0 \"{culture.Name}\"");
                lines.Add($" {culture.Heritage}_name:0 \"{culture.Name} Heritage\"");
            }

            // Language loc (only if this culture owns it)
            if (culture.Language == $"lemur_language_{culture.AzgaarId}")
            {
                lines.Add($" {culture.Language}:0 \"{culture.Name}\"");
                lines.Add($" {culture.Language}_name:0 \"{culture.Name} Language\"");
            }
        }

        var path = Helper.GetPath(dir, "lemur_cultures_l_english.yml");
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static (int r, int g, int b) ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            int r = Convert.ToInt32(hex[..2], 16);
            int g = Convert.ToInt32(hex[2..4], 16);
            int b = Convert.ToInt32(hex[4..6], 16);
            return (r, g, b);
        }
        return (128, 128, 128);
    }
}
