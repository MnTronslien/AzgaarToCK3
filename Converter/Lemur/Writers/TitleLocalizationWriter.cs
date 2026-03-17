using Converter.Lemur;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class TitleLocalizationWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing title localization");

        var dir = Helper.GetPath(outputDirectory, "localization", "english");
        Directory.CreateDirectory(dir);

        var lines = new List<string> { "l_english:" };

        bool hasOrphans = map.Kingdoms.Any(k => k.Parent == null);
        if (hasOrphans)
            lines.Add(" e_orphan_0:0 \"Unaffiliated Kingdoms\"");

        foreach (var empire in map.Empires!)
        {
            if (!empire.Kingdoms.Any()) continue;
            lines.Add($" {empire.Ck3_Id()}:0 \"{empire.Name}\"");
            WriteKingdomsLoc(lines, empire.Kingdoms);
        }

        // Orphan kingdoms and their children
        WriteKingdomsLoc(lines, map.Kingdoms.Where(k => k.Parent == null));

        var path = Helper.GetPath(dir, "lemur_titles_l_english.yml");
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        Logger.Info($"Wrote lemur_titles_l_english.yml");
    }

    private static void WriteKingdomsLoc(List<string> lines, IEnumerable<L.Kingdom> kingdoms)
    {
        foreach (var kingdom in kingdoms)
        {
            lines.Add($" {kingdom.Ck3_Id()}:0 \"{kingdom.Name}\"");

            foreach (var duchy in kingdom.Duchies)
            {
                lines.Add($" {duchy.Ck3_Id()}:0 \"{duchy.Name}\"");

                foreach (var county in duchy.Counties)
                {
                    lines.Add($" {county.Ck3_Id()}:0 \"{county.Name}\"");

                    foreach (var barony in county.Baronies!)
                        lines.Add($" {barony.Ck3_Id()}:0 \"{barony.Name}\"");
                }
            }
        }
    }
}
