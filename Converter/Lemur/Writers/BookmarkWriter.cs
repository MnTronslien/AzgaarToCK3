using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

/// <summary>
/// Emits the start bookmark our generated world needs to be playable. CK3 requires at least one
/// playable bookmark to enter a game; the Total Conversion Sandbox mod used to supply this, so a
/// TCS-free playset had no startable game. We point the bookmark at our largest generated realms.
///
/// Files written (all under <c>replace_path</c>s declared by <see cref="ModDescriptorWriter"/>):
///   common/bookmarks/groups/00_bookmark_groups.txt   — one group at the start date
///   common/bookmarks/bookmarks/00_bookmarks.txt       — one bookmark, one character per ruler
///   common/bookmark_portraits/                        — empty (portraits render from culture/DNA)
///   localization/english/lemur_bookmarks_l_english.yml
/// </summary>
public static class BookmarkWriter
{
    // Must match TitleHistoryWriter.StartDate — the bookmark date is when holders are assigned.
    private const string StartDate = "1066.1.1";
    private const string GroupKey = "bm_group_1066";
    private const string BookmarkKey = "bm_lemur";

    // How many of the largest realms to surface as selectable bookmark characters.
    private const int MaxRulers = 3;

    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing start bookmark");

        // Largest held realms first (by county count). Empires are de jure shells with no holder,
        // so kingdoms are the top playable tier (matches TitleHistoryWriter).
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
            $"{GroupKey} = {{\n\tdefault_start_date = {StartDate}\n}}\n",
            Helper.Utf8Bom);

        // --- bookmark + per-ruler character blocks ---
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{BookmarkKey} = {{");
        sb.AppendLine($"\tstart_date = {StartDate}");
        sb.AppendLine("\tis_playable = yes");
        sb.AppendLine($"\tgroup = {GroupKey}");
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

            // Characters carry no name in history (CK3 names them from the culture list at runtime),
            // so the bookmark screen falls back to the realm name.
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
