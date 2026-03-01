namespace Converter.Lemur.Writers;

/// <summary>
/// Writes a minimal placeholder file into each replace_path directory that would
/// otherwise be empty. CK3 only honours replace_path if the mod contains at least
/// one file in that directory.
/// </summary>
public static class StubFilesWriter
{
    private static readonly string[] StubDirectories =
    {
        "history/characters",
        "common/bookmark_portraits",
        "gfx/map/map_object_data",
    };

    public static async Task Write(string outputDirectory)
    {
        foreach (var dir in StubDirectories)
        {
            var path = Helper.GetPath(outputDirectory, dir, "00_empty.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "# placeholder\n");
        }

        Console.WriteLine($"Wrote stub files to {StubDirectories.Length} empty replace_path directories.");
    }
}
