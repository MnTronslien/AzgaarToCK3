namespace Converter.Lemur.Writers;

public static class MapDefinesWriter
{
    public static async Task Write(string outputDirectory)
    {
        var content =
            "NJominiMap = {\n" +
            "\tWORLD_EXTENTS_X = 8192\n" +
            "\tWORLD_EXTENTS_Y = 51\n" +
            "\tWORLD_EXTENTS_Z = 4096\n" +
            "\tWATERLEVEL = 3.8\n" +
            "}\n";

        var path = Helper.GetPath(outputDirectory, "common", "defines", "mapsize_defines.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        Console.WriteLine("Wrote mapsize_defines.txt");
    }
}
