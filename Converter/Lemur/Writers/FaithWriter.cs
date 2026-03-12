using Converter.Lemur.Entities;
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
        // Skip sentinel/placeholder faiths inserted by CharacterFactory (AzgaarId <= 0).
        var byReligion = faiths.Values
            .Where(f => f.AzgaarId > 0)
            .GroupBy(f => f.CK3ReligionKey)
            .OrderBy(g => g.Key)
            .ToList();

        // Build a lookup: cellId → barony, to find holy site counties
        var cellIdToBarony = BuildCellToBaronyLookup(map);

        var t1 = WriteReligionsFile(byReligion, outputDirectory);
        var t2 = WriteHolySitesFile(faiths.Values.Where(f => f.AzgaarId > 0).ToList(), map, cellIdToBarony, outputDirectory);
        var t3 = WriteLocalizationFile(byReligion, outputDirectory);

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
            bool anyUnreformed = group.Any(f => f.IsUnreformed);

            lines.Add($"{group.Key} = {{");
            lines.Add("\tfamily = rf_other");
            if (anyUnreformed)
                lines.Add("\tpagan_roots = yes");
            lines.Add("");
            lines.Add("\tdoctrine = doctrine_no_head");
            lines.Add("\tdoctrine = doctrine_gender_equal");
            lines.Add("\tdoctrine = doctrine_clerical_marriage_allowed");
            lines.Add("\tdoctrine = doctrine_clerical_succession_temporal");
            lines.Add("\tdoctrine = doctrine_clerical_gender_either");
            lines.Add("\tdoctrine = doctrine_pluralism_pluralistic");
            lines.Add("");
            lines.Add("\tfaiths = {");

            foreach (var faith in group.OrderBy(f => f.AzgaarId))
            {
                var (r, g, b) = ParseHexColor(faith.HexColor);
                var holySiteKey = $"lemur_site_{faith.AzgaarId}";

                lines.Add($"\t\t{faith.CK3Key} = {{");
                lines.Add($"\t\t\tcolor = rgb {{ {r} {g} {b} }}");
                lines.Add($"\t\t\ticon = {faith.IconKey}");
                lines.Add($"\t\t\treformed_icon = {faith.IconKey}");
                lines.Add("");
                lines.Add($"\t\t\tholy_site = {holySiteKey}");
                lines.Add("");

                if (faith.IsUnreformed)
                    lines.Add("\t\t\tdoctrine = unreformed_faith_doctrine");

                lines.Add("\t\t\tdoctrine = tenet_animism");
                lines.Add("\t\t\tdoctrine = tenet_ancestor_worship");
                lines.Add("\t\t\tdoctrine = tenet_sanctioned_looting");
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
        List<Faith> faiths,
        L.Map map,
        Dictionary<int, L.Barony> cellIdToBarony,
        string outputDirectory)
    {
        var dir = Helper.GetPath(outputDirectory, "common", "religion", "holy_sites");
        Directory.CreateDirectory(dir);

        var lines = new List<string>
        {
            "# Lemur conversion: generated holy sites.",
            ""
        };

        foreach (var faith in faiths.OrderBy(f => f.AzgaarId))
        {
            var countyId = FindHolySiteCountyId(faith, map, cellIdToBarony);
            var countyKey = $"c_{countyId}";

            lines.Add($"lemur_site_{faith.AzgaarId} = {{");
            lines.Add($"\tcounty = {countyKey}");
            lines.Add("\tcharacter_modifier = {");
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
        string outputDirectory)
    {
        var dir = Helper.GetPath(outputDirectory, "localization", "english");
        Directory.CreateDirectory(dir);

        var lines = new List<string> { "l_english:" };

        // Track which religion containers we've already written
        var writtenReligions = new HashSet<string>();

        foreach (var group in byReligion)
        {
            // Write religion container name once (use root faith name)
            var rootFaith = group.OrderBy(f => f.AzgaarId).First();
            if (writtenReligions.Add(group.Key))
            {
                lines.Add($" {group.Key}: \"{rootFaith.Name}\"");
            }

            foreach (var faith in group.OrderBy(f => f.AzgaarId))
            {
                lines.Add($" {faith.CK3Key}: \"{faith.Name}\"");
                lines.Add($" {faith.CK3Key}_adj: \"{faith.Name}\"");
                if (!string.IsNullOrEmpty(faith.Deity))
                    lines.Add($" {faith.CK3Key}_HighGodName: \"{faith.Deity}\"");
            }
        }

        var path = Helper.GetPath(dir, "lemur_faiths_l_english.yml");
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Dictionary<int, L.Barony> BuildCellToBaronyLookup(L.Map map)
    {
        var dict = new Dictionary<int, L.Barony>();
        if (map.Baronies == null) return dict;

        foreach (var barony in map.Baronies)
        {
            foreach (var cell in barony.Cells)
            {
                dict.TryAdd(cell.Id, barony);
            }
        }
        return dict;
    }

    private static int FindHolySiteCountyId(
        Faith faith,
        L.Map map,
        Dictionary<int, L.Barony> cellIdToBarony)
    {
        // Primary: find the barony that owns the origin cell
        if (faith.OriginCellId > 0 && cellIdToBarony.TryGetValue(faith.OriginCellId, out var originBarony))
        {
            if (originBarony.Parent is L.County county)
                return county.Id;
        }

        // Fallback: find the county with the most cells matching this faith's AzgaarId
        if (map.Counties != null && map.Cells != null)
        {
            var bestCounty = map.Counties
                .Select(c => (county: c,
                    count: c.GetAllCells().Count(cell => cell.Religion == faith.AzgaarId)))
                .Where(x => x.count > 0)
                .OrderByDescending(x => x.count)
                .FirstOrDefault();

            if (bestCounty.county != null)
                return bestCounty.county.Id;
        }

        // Last resort: first county
        return map.Counties?.FirstOrDefault()?.Id ?? 1;
    }

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
