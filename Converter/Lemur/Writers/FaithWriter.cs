using Converter.Lemur.Entities;
using Converter.Lemur;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class FaithWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing faith files");

        var faiths = map.Faiths;
        if (faiths.Count == 0)
        {
            Logger.Warning("FaithWriter: no faiths found, skipping.");
            return;
        }

        // Delete old hardcoded stub files if present
        var oldGermanic = Helper.GetPath(outputDirectory, "common", "religion", "religions", "00_germanic.txt");
        if (File.Exists(oldGermanic)) File.Delete(oldGermanic);
        var oldHolySites = Helper.GetPath(outputDirectory, "common", "religion", "holy_sites", "00_holy_sites.txt");
        if (File.Exists(oldHolySites)) File.Delete(oldHolySites);

        // Group faiths by religion container key.
        var byReligion = faiths.Values
            .GroupBy(f => f.CK3ReligionKey)
            .OrderBy(g => g.Key)
            .ToList();

        var t1 = WriteReligionsFile(byReligion, outputDirectory);
        var t2 = WriteHolySitesFile(map.HolySites, outputDirectory);
        var t3 = WriteLocalizationFile(byReligion, map.HolySites, map.JsonMap.pack.cultures, outputDirectory);

        await Task.WhenAll(t1, t2, t3);

        Logger.Info($"Wrote {faiths.Count} faiths in {byReligion.Count} religion containers.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // File 1: common/religion/religions/lemur_religions.txt
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task WriteReligionsFile(
        List<IGrouping<string, Faith>> byReligion,
        string outputDirectory)
    {
        var dir = Helper.GetPath(outputDirectory, "common", "religion", "religions");
        Directory.CreateDirectory(dir);

        var lines = new List<string>
        {
            "# Lemur conversion: generated religion/faith definitions.",
            ""
        };

        foreach (var group in byReligion)
        {
            var sortedFaiths = group.OrderBy(f => f.AzgaarId).ToList();
            var rootFaith = sortedFaiths[0];
            bool anyUnreformed = sortedFaiths.Any(f => f.IsUnreformed);

            lines.Add($"# {rootFaith.Name}");
            lines.Add($"{group.Key} = {{");
            lines.Add("\tfamily = rf_other");
            if (anyUnreformed)
                lines.Add("\tpagan_roots = yes");
            lines.Add("");
            lines.Add("\tfaiths = {");

            foreach (var faith in sortedFaiths)
            {
                var (r, g, b) = ParseHexColor(faith.HexColor);

                lines.Add($"\t\t# {faith.Name}");
                lines.Add($"\t\t{faith.CK3Key} = {{");
                lines.Add($"\t\t\tcolor = rgb {{ {r} {g} {b} }}");
                lines.Add($"\t\t\ticon = {faith.IconKey}");
                lines.Add($"\t\t\treformed_icon = {faith.IconKey}");

                // Emit all holy sites for this faith
                foreach (var site in faith.HolySites)
                    lines.Add($"\t\t\tholy_site = {site.Key}");

                lines.Add("");

                // 21 structural doctrines (one per required group)
                var labels = DoctrineData.Groups.Select(gr => gr.Label).ToArray();
                for (int i = 0; i < faith.Doctrines.Count && i < labels.Length; i++)
                {
                    lines.Add($"\t\t\t# {labels[i]}");
                    lines.Add($"\t\t\tdoctrine = {faith.Doctrines[i]}");
                }
                lines.Add("");

                // Special doctrine for unreformed faiths (not a tenet slot)
                if (faith.IsUnreformed)
                    lines.Add("\t\t\tdoctrine = unreformed_faith_doctrine");

                // Tenets
                foreach (var tenet in faith.Tenets)
                    lines.Add($"\t\t\tdoctrine = {tenet}");

                lines.Add("\t\t}");
            }

            lines.Add("\t}");
            lines.Add("}");
            lines.Add("");
        }

        var path = Helper.GetPath(dir, "lemur_religions.txt");
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // File 2: common/religion/holy_sites/lemur_holy_sites.txt
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task WriteHolySitesFile(
        List<HolySite> sites,
        string outputDirectory)
    {
        var dir = Helper.GetPath(outputDirectory, "common", "religion", "holy_sites");
        Directory.CreateDirectory(dir);

        var lines = new List<string>
        {
            "# Lemur conversion: generated holy sites.",
            ""
        };

        foreach (var site in sites.OrderBy(s => s.Key))
        {
            lines.Add($"{site.Key} = {{");
            lines.Add($"\tcounty = {site.County.Ck3_Id()}");
            if (site.Barony != null)
                lines.Add($"\tbarony = {site.Barony.Ck3_Id()}");
            lines.Add("\tcharacter_modifier = {");
            lines.Add($"\t\tname = {site.ModifierNameKey}");
            foreach (var (key, value) in site.Modifiers)
                lines.Add($"\t\t{key} = {value}");
            lines.Add("\t}");
            lines.Add("}");
            lines.Add("");
        }

        var path = Helper.GetPath(dir, "lemur_holy_sites.txt");
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // File 3: localization/english/lemur_faiths_l_english.yml
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task WriteLocalizationFile(
        List<IGrouping<string, Faith>> byReligion,
        List<HolySite> sites,
        Deserialization.AzgaarCulture[] cultures,
        string outputDirectory)
    {
        var dir = Helper.GetPath(outputDirectory, "localization", "english");
        Directory.CreateDirectory(dir);

        var lines = new List<string> { "l_english:" };

        var cultureById = cultures
            .Where(c => c.i > 0)
            .ToDictionary(c => c.i, c => c.name);

        foreach (var group in byReligion)
        {
            var sortedFaiths = group.OrderBy(f => f.AzgaarId).ToList();
            var rootFaith = sortedFaiths[0];

            // Religion container name, adjective, and description
            cultureById.TryGetValue(rootFaith.OriginalCultureId, out var cultureName);
            var desc = cultureName != null
                ? $"The ancient religion of the {cultureName}"
                : $"The ancient {rootFaith.Name} religion";

            lines.Add($" {group.Key}:0 \"{rootFaith.Name}\"");
            lines.Add($" {group.Key}_adj:0 \"{rootFaith.Name}\"");
            lines.Add($" {group.Key}_desc:0 \"{desc}\"");

            // Religion-level placeholder keys (inherited by all child faiths)
            lines.Add($" {group.Key}_house_of_worship:0 \"temple\"");
            lines.Add($" {group.Key}_house_of_worship_plural:0 \"temples\"");
            lines.Add($" {group.Key}_religious_symbol:0 \"holy symbol\"");
            lines.Add($" {group.Key}_religious_text:0 \"holy texts\"");
            lines.Add($" {group.Key}_positive_afterlife:0 \"paradise\"");
            lines.Add($" {group.Key}_negative_afterlife:0 \"the underworld\"");
            lines.Add($" {group.Key}_priest_male:0 \"priest\"");
            lines.Add($" {group.Key}_priest_male_plural:0 \"priests\"");
            lines.Add($" {group.Key}_priest_female:0 \"priestess\"");
            lines.Add($" {group.Key}_priest_female_plural:0 \"priestesses\"");
            lines.Add($" {group.Key}_bishop:0 \"high priest\"");
            lines.Add($" {group.Key}_bishop_plural:0 \"high priests\"");
            lines.Add($" {group.Key}_devotee_male:0 \"devotee\"");
            lines.Add($" {group.Key}_devotee_male_plural:0 \"devotees\"");
            lines.Add($" {group.Key}_devotee_female:0 \"devotee\"");
            lines.Add($" {group.Key}_devotee_female_plural:0 \"devotees\"");
            lines.Add($" {group.Key}_religious_head_title:0 \"High Priest\"");
            lines.Add($" {group.Key}_religious_head_title_name:0 \"High Priesthood\"");

            foreach (var faith in sortedFaiths)
            {
                lines.Add($" {faith.CK3Key}:0 \"{faith.Name}\"");
                lines.Add($" {faith.CK3Key}_adj:0 \"{faith.Name}\"");
                if (!string.IsNullOrEmpty(faith.Deity))
                {
                    lines.Add($" {faith.CK3Key}_high_god_name:0 \"{faith.Deity}\"");
                    lines.Add($" {faith.CK3Key}_high_god_name_possessive:0 \"{faith.Deity}'s\"");
                }
                lines.Add($" {faith.CK3Key}_adherent:0 \"{faith.Name}\"");
                lines.Add($" {faith.CK3Key}_adherent_plural:0 \"{faith.Name} followers\"");
                lines.Add($" {faith.CK3Key}_desc:0 \"The {faith.Name} faith.\"");
            }
        }

        // One loc entry per unique holy site (deduplicated — sites are already unique)
        foreach (var site in sites.OrderBy(s => s.Key))
        {
            var deity = site.OriginFaith?.Deity;
            var effectName = string.IsNullOrEmpty(deity)
                ? $"Blessing of {site.GroupName}"
                : $"Blessing of {site.GroupName} from {deity}";
            lines.Add($" {site.NameKey}:0 \"{site.County.Name}\"");
            lines.Add($" {site.ModifierNameKey}:0 \"{effectName}\"");
        }

        var path = Helper.GetPath(dir, "lemur_faiths_l_english.yml");
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static (int r, int g, int b) ParseHexColor(string hex)
    {
        // Expects "#rrggbb"
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            int r = Convert.ToInt32(hex[..2], 16);
            int g = Convert.ToInt32(hex[2..4], 16);
            int b = Convert.ToInt32(hex[4..6], 16);
            return (r, g, b);
        }
        return (128, 128, 128); // fallback grey
    }
}
