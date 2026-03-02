using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

/// <summary>
/// Writes the minimum CK3 files needed to avoid a crash at the bookmark/character-selection screen:
///   - history/titles/00_lemur_titles.txt            (gives TCS character 20842 our first county)
///   - common/bookmarks/bookmarks/z_lemur_bookmarks.txt
///       Overrides the TCS "bm_1066_canarias" bookmark (which references c_canarias —
///       a title that does not exist in our mod) with a version pointing to our first county.
///       Reuses TCS character 20842 (Guanarigato) directly — no custom character needed.
///       The z_ prefix ensures LIOS: this file loads after 00_bookmarks.txt and wins.
/// </summary>
public static class BookmarkWriter
{
    private const string StartDate = "1066.10.1";

    // TCS character 20842 (Guanarigato) — declared in TCS berber.txt.
    // Born 1039.1.1, dies 1089.1.1 → alive at bookmark date 1066.10.1.
    // TCS already has portrait DDS: bm_1066_canarias_bookmark_canarias_guanarigato.dds
    private const int TcsCharacterId = 20842;
    private const string TcsDynasty = "100848";
    private const string TcsCulture = "baranis";
    private const string TcsReligion = "west_african_pagan";
    private const string TcsBirthDate = "1039.1.1";
    private const string TcsCharacterName = "bookmark_canarias_guanarigato"; // resolves TCS portrait DDS

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
            $"# Lemur: minimal definition for bookmark character (TCS character {TcsCharacterId} / Guanarigato).",
            $"# Required because replace_path=\"history/characters\" blocks TCS's berber.txt.",
            $"{TcsCharacterId} = {{",
            $"\tname = \"{TcsCharacterName}\"",
            $"\tculture = {TcsCulture}",
            $"\treligion = \"{TcsReligion}\"",
            $"\tdynasty = {TcsDynasty}",
            $"\t{TcsBirthDate} = {{",
            $"\t\tbirth = yes",
            $"\t}}",
            $"\t1089.1.1 = {{",
            $"\t\tdeath = yes",
            $"\t}}",
            $"}}"
        };

        var path = Helper.GetPath(outputDirectory, "history", "characters", "00_lemur_characters.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        Logger.Info($"Wrote 00_lemur_characters.txt (TCS character {TcsCharacterId} / Guanarigato)");
    }

    private static async Task WriteTitleHistory(string outputDirectory, string countyId)
    {
        var lines = new[]
        {
            $"# Lemur: give TCS character {TcsCharacterId} (Guanarigato) our first county.",
            $"{countyId} = {{",
            $"\t{StartDate} = {{",
            $"\t\tholder = {TcsCharacterId}",
            $"\t}}",
            $"}}"
        };

        var path = Helper.GetPath(outputDirectory, "history", "titles", "00_lemur_titles.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        Logger.Info($"Wrote 00_lemur_titles.txt ({countyId} → TCS character {TcsCharacterId})");
    }

    private static async Task WriteBookmark(string outputDirectory, string countyId)
    {
        // Override TCS's bm_1066_canarias (references c_canarias which doesn't exist → crash).
        // Use the same bookmark ID so LIOS replaces it entirely.
        // Keep the same character name so CK3 resolves TCS's existing portrait DDS automatically.
        var lines = new[]
        {
            "# Lemur: override TCS bookmark — same character, our county instead of c_canarias.",
            "bm_1066_canarias = {",
            $"\tstart_date = {StartDate}",
            "\tis_playable = yes",
            "\tgroup = bm_group_1066",
            "\tweight = { value = 100 }",
            "\tcharacter = {",
            $"\t\tname = \"{TcsCharacterName}\"",
            $"\t\tdynasty = {TcsDynasty}",
            "\t\tdynasty_splendor_level = 1",
            "\t\ttype = male",
            $"\t\tbirth = {TcsBirthDate}",
            $"\t\ttitle = {countyId}",
            "\t\tgovernment = tribal_government",
            $"\t\tculture = {TcsCulture}",
            $"\t\treligion = \"{TcsReligion}\"",
            "\t\tdifficulty = \"BOOKMARK_CHARACTER_DIFFICULTY_EASY\"",
            $"\t\thistory_id = {TcsCharacterId}",
            "\t\tposition = { 250 750 }",
            "\t\tanimation = happiness",
            "\t}",
            "}"
        };

        var path = Helper.GetPath(outputDirectory, "common", "bookmarks", "bookmarks", "z_lemur_bookmarks.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        Logger.Info($"Wrote z_lemur_bookmarks.txt (overrides bm_1066_canarias → {countyId}, TCS character {TcsCharacterId})");
    }
}
