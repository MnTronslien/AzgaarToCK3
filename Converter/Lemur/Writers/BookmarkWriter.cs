using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

/// <summary>
/// Emits the start bookmark CK3 requires to enter a game, pointed at our largest generated realms.
/// Writes bookmark group, bookmark + per-ruler characters, an empty bookmark_portraits dir, and loc
/// (all under <c>replace_path</c>s from <see cref="ModDescriptorWriter"/>).
/// </summary>
public static class BookmarkWriter
{
    private const string BookmarkKey = "bm_lemur";

    // How many of the largest realms to surface as selectable bookmark characters.
    private const int MaxRulers = 3;

    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing start bookmark");

        // The bookmark date is when holders are assigned — single source of truth on Map.StartDate
        // (the same date TitleHistoryWriter emits on every title-history block).
        var startDate = map.StartDate.ToString();
        var groupKey = $"bm_group_{map.StartDate.Year}";

        // Largest held realms first; kingdoms are the top playable tier (empires have no holder).
        var rulers = map.Kingdoms
            .Where(k => k.Holder != null)
            .OrderByDescending(k => k.Duchies.Sum(d => d.Counties.Count))
            .Take(MaxRulers)
            .ToList();

        if (rulers.Count == 0)
            Logger.Warning("BookmarkWriter: no held kingdoms — bookmark will list no playable characters.");

        // --- groups ---
        var groupsDir = Helper.GetPath(outputDirectory, "common", "bookmarks", "groups");
        Directory.CreateDirectory(groupsDir);
        await File.WriteAllTextAsync(
            Helper.GetPath(groupsDir, "00_bookmark_groups.txt"),
            $"{groupKey} = {{\n\tdefault_start_date = {startDate}\n}}\n",
            Helper.Utf8Bom);

        // --- bookmark + per-ruler character blocks ---
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{BookmarkKey} = {{");
        sb.AppendLine($"\tstart_date = {startDate}");
        sb.AppendLine("\tis_playable = yes");
        sb.AppendLine($"\tgroup = {groupKey}");
        sb.AppendLine();
        sb.AppendLine("\tweight = { value = 100 }");

        var locLines = new List<string>
        {
            "l_english:",
            $" {BookmarkKey}:0 \"A New Chronicle\"",
            $" {BookmarkKey}_desc:0 \"A freshly forged world awaits its first chronicle. Choose a realm and begin your dynasty.\"",
        };

        for (int i = 0; i < rulers.Count; i++)
        {
            var k = rulers[i];
            var holder = k.Holder!;
            var nameKey = $"bookmark_{holder.Id}";
            int x = 400 + i * 400; // UI layout position on the bookmark screen, not a map coordinate.

            sb.AppendLine();
            sb.AppendLine("\tcharacter = {");
            sb.AppendLine($"\t\tname = \"{nameKey}\"");
            sb.AppendLine("\t\ttype = male");
            sb.AppendLine($"\t\tbirth = {holder.BirthYear}.1.1");
            sb.AppendLine($"\t\ttitle = {k.Ck3_Id()}");
            sb.AppendLine($"\t\tgovernment = {k.Government?.Key ?? "feudal_government"}");
            sb.AppendLine($"\t\tculture = {holder.Culture.CK3Key}");
            sb.AppendLine($"\t\treligion = {holder.Faith.CK3Key}");
            sb.AppendLine("\t\tdifficulty = \"BOOKMARK_CHARACTER_DIFFICULTY_MEDIUM\"");
            sb.AppendLine($"\t\thistory_id = {holder.Id}");
            sb.AppendLine($"\t\tposition = {{ {x} 600 }}");
            sb.AppendLine("\t\tanimation = happiness");
            sb.AppendLine("\t}");

            // Characters have no history name (CK3 names them at runtime); fall back to the realm name.
            locLines.Add($" {nameKey}:0 \"{holder.Name ?? k.Name}\"");
        }

        sb.AppendLine("}");

        var bookmarksDir = Helper.GetPath(outputDirectory, "common", "bookmarks", "bookmarks");
        Directory.CreateDirectory(bookmarksDir);
        await File.WriteAllTextAsync(Helper.GetPath(bookmarksDir, "00_bookmarks.txt"), sb.ToString(), Helper.Utf8Bom);

        // --- empty bookmark_portraits (replace_path suppresses vanilla; we ship no bespoke genes) ---
        Directory.CreateDirectory(Helper.GetPath(outputDirectory, "common", "bookmark_portraits"));

        // --- localization ---
        var locDir = Helper.GetPath(outputDirectory, "localization", "english");
        Directory.CreateDirectory(locDir);
        await File.WriteAllLinesAsync(Helper.GetPath(locDir, "lemur_bookmarks_l_english.yml"), locLines, Helper.Utf8Bom);

        Logger.Info($"Wrote start bookmark ({rulers.Count} playable ruler(s))");
    }
}
