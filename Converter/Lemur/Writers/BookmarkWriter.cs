using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

/// <summary>
/// Writes the minimum CK3 files needed to avoid a crash at the bookmark/character-selection screen:
///   - history/characters/00_lemur_characters.txt   (one placeholder ruler)
///   - history/titles/00_lemur_titles.txt            (gives that ruler a county)
///   - common/bookmarks/bookmarks/z_lemur_bookmarks.txt
///       Overrides the TCS "bm_1066_canarias" bookmark (which references c_canarias —
///       a title that does not exist in our mod) with a version pointing to our first county.
///       The z_ prefix ensures LIOS: this file loads after 00_bookmarks.txt and wins.
/// </summary>
public static class BookmarkWriter
{
    // Character ID large enough to avoid collisions with TCS character IDs.
    private const int RulerCharacterId = 90001;
    private const string StartDate = "1066.10.1";

    public static async Task Write(L.Map map, string outputDirectory)
    {
        var firstCounty = map.Counties!.First();
        var countyId = LandedTitlesWriter.ToCk3Id("c", firstCounty.Name, firstCounty.Id);

        await WriteCharacters(outputDirectory);
        await WriteTitleHistory(outputDirectory, countyId);
        await WriteBookmark(outputDirectory, countyId);
    }

    private static async Task WriteCharacters(string outputDirectory)
    {
        var lines = new[]
        {
            $"# Lemur: placeholder ruler for the starting bookmark.",
            $"{RulerCharacterId} = {{",
            $"\tname = \"Adventurer\"",
            $"\tculture = english",
            $"\treligion = catholic",
            $"\t867.1.1 = {{ birth = yes }}",
            $"\t1100.1.1 = {{ death = yes }}",
            $"}}"
        };

        var path = Helper.GetPath(outputDirectory, "history", "characters", "00_lemur_characters.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        Console.WriteLine($"Wrote 00_lemur_characters.txt (character {RulerCharacterId})");
    }

    private static async Task WriteTitleHistory(string outputDirectory, string countyId)
    {
        var lines = new[]
        {
            $"# Lemur: give placeholder ruler the first county at game start.",
            $"{countyId} = {{",
            $"\t{StartDate} = {{",
            $"\t\tholder = {RulerCharacterId}",
            $"\t}}",
            $"}}"
        };

        var path = Helper.GetPath(outputDirectory, "history", "titles", "00_lemur_titles.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        Console.WriteLine($"Wrote 00_lemur_titles.txt ({countyId} → ruler {RulerCharacterId})");
    }

    private static async Task WriteBookmark(string outputDirectory, string countyId)
    {
        // Override TCS's bm_1066_canarias (references c_canarias which doesn't exist → crash).
        // Use the same bookmark ID so LIOS replaces it entirely.
        var lines = new[]
        {
            $"# Lemur: override TCS bookmark to point at our first county.",
            $"# c_canarias does not exist in this mod — using {countyId} instead.",
            $"bm_1066_canarias = {{",
            $"\tstart_date = {StartDate}",
            $"\tis_playable = yes",
            $"\tgroup = bm_group_1066",
            $"\tweight = {{ value = 100 }}",
            $"\tcharacter = {{",
            $"\t\tname = \"bookmark_lemur_ruler\"",
            $"\t\tdynasty_splendor_level = 1",
            $"\t\ttype = male",
            $"\t\tbirth = 867.1.1",
            $"\t\ttitle = {countyId}",
            $"\t\tgovernment = tribal_government",
            $"\t\tculture = english",
            $"\t\treligion = catholic",
            $"\t\tdifficulty = \"BOOKMARK_CHARACTER_DIFFICULTY_EASY\"",
            $"\t\thistory_id = {RulerCharacterId}",
            $"\t\tposition = {{ 250 750 }}",
            $"\t\tanimation = happiness",
            $"\t}}",
            $"}}"
        };

        var path = Helper.GetPath(outputDirectory, "common", "bookmarks", "bookmarks", "z_lemur_bookmarks.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        Console.WriteLine($"Wrote z_lemur_bookmarks.txt (overrides bm_1066_canarias → {countyId})");
    }
}
