namespace Converter.Lemur.Writers;

public static class AdjacenciesCsvWriter
{
    // Header describes the format; the terminator line is mandatory — omitting it causes an infinite loading screen.
    private const string Header = "From;To;Type;Through;start_x;start_y;stop_x;stop_y;Comment";
    private const string Terminator = "-1;-1;;-1;-1;-1;-1;-1;";

    public static async Task Write(string outputDirectory)
    {
        var path = Helper.GetPath(outputDirectory, "map_data", "adjacencies.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, [Header, Terminator]);
        Console.WriteLine("Wrote adjacencies.csv (stub — no sea connections yet)");
    }
}
