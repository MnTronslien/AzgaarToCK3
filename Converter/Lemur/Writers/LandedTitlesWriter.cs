using System.Text;
using System.Text.RegularExpressions;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class LandedTitlesWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        var sb = new StringBuilder();

        // Build a lookup: barony → province ID (1-based index in AllProvinces)
        var baronies = map.Baronies!;
        // AllProvinces order: Baronies, then Wastelands, then SeaZones
        // Baronies are first, so provinceId = Baronies.IndexOf(barony) + 1
        var baroniesProvId = new Dictionary<L.Barony, int>(baronies.Count);
        for (int i = 0; i < baronies.Count; i++)
            baroniesProvId[baronies[i]] = i + 1;

        // Track orphan kingdoms (no parent empire) — wrap in a synthetic empire
        var orphanKingdoms = map.Kingdoms.Where(k => k.Parent == null).ToList();
        if (orphanKingdoms.Any())
        {
            sb.AppendLine("e_orphan_0 = {");
            sb.AppendLine("\tlandless = yes");
            sb.AppendLine("\tcolor = { 0.5 0.5 0.5 }");
            foreach (var kingdom in orphanKingdoms)
                WriteKingdom(sb, kingdom, baroniesProvId);
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Write empires with their kingdoms
        foreach (var empire in map.Empires!)
        {
            if (!empire.Kingdoms.Any()) continue;
            var empId = ToCk3Id("e", empire.Name, empire.Id);
            sb.AppendLine($"{empId} = {{");
            sb.AppendLine($"\tcolor = {{ 0.5 0.5 0.5 }}");
            foreach (var kingdom in empire.Kingdoms)
                WriteKingdom(sb, kingdom, baroniesProvId);
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Vanilla stubs required by CK3 scripting — omitting these causes errors on load
        sb.AppendLine("e_hre = { landless = yes color = { 0.5 0.5 0.5 } }");
        sb.AppendLine("e_byzantium = { landless = yes color = { 0.5 0.5 0.5 } }");
        sb.AppendLine("e_roman_empire = { landless = yes color = { 0.5 0.5 0.5 } }");

        var path = Helper.GetPath(outputDirectory, "common", "landed_titles", "00_landed_titles.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, sb.ToString());
        Console.WriteLine($"Wrote 00_landed_titles.txt ({baronies.Count} baronies)");
    }

    private static void WriteKingdom(StringBuilder sb, L.Kingdom kingdom, Dictionary<L.Barony, int> baroniesProvId)
    {
        var kId = ToCk3Id("k", kingdom.Name, kingdom.Id);
        sb.AppendLine($"\t{kId} = {{");
        sb.AppendLine($"\t\tcolor = {{ 0.5 0.5 0.5 }}");
        foreach (var duchy in kingdom.Duchies)
            WriteDuchy(sb, duchy, baroniesProvId);
        sb.AppendLine($"\t}}");
    }

    private static void WriteDuchy(StringBuilder sb, L.Duchy duchy, Dictionary<L.Barony, int> baroniesProvId)
    {
        var dId = ToCk3Id("d", duchy.Name, duchy.Id);
        sb.AppendLine($"\t\t{dId} = {{");
        sb.AppendLine($"\t\t\tcolor = {{ 0.5 0.5 0.5 }}");
        foreach (var county in duchy.Counties)
            WriteCounty(sb, county, baroniesProvId);
        sb.AppendLine($"\t\t}}");
    }

    private static void WriteCounty(StringBuilder sb, L.County county, Dictionary<L.Barony, int> baroniesProvId)
    {
        var cId = ToCk3Id("c", county.Name, county.Id);
        sb.AppendLine($"\t\t\t{cId} = {{");
        sb.AppendLine($"\t\t\t\tcolor = {{ 0.5 0.5 0.5 }}");
        foreach (var barony in county.Baronies!)
        {
            if (!baroniesProvId.TryGetValue(barony, out int provId)) continue;
            var bId = ToCk3Id("b", barony.Name, barony.Id);
            sb.AppendLine($"\t\t\t\t{bId} = {{");
            sb.AppendLine($"\t\t\t\t\tprovince = {provId}");
            sb.AppendLine($"\t\t\t\t}}");
        }
        sb.AppendLine($"\t\t\t}}");
    }

    /// <summary>
    /// Converts a name to a valid CK3 identifier.
    /// Pattern: {prefix}_{lowercase_underscored_ascii_name}_{id}
    /// Example: ToCk3Id("e", "Roman Empire", 5) → "e_roman_empire_5"
    /// </summary>
    internal static string ToCk3Id(string prefix, string name, int id)
    {
        var lower = name.ToLowerInvariant();
        var underscored = Regex.Replace(lower, @"[\s\-]+", "_");
        var ascii = Regex.Replace(underscored, @"[^a-z0-9_]", "");
        var clean = Regex.Replace(ascii, @"_+", "_").Trim('_');
        if (string.IsNullOrEmpty(clean)) clean = "unnamed";
        return $"{prefix}_{clean}_{id}";
    }
}
