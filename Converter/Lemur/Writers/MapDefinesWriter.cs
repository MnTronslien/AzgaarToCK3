using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class MapDefinesWriter
{
    public static async Task Write(string outputDirectory)
    {
        var definesDir = Helper.GetPath(outputDirectory, "common", "defines");
        Directory.CreateDirectory(definesDir);

        // mapsize_defines.txt — extents are 0-based max (width-1, height-1)
        // WATERLEVEL = (150.0 / 255.0) * 20.0 ≈ 11.765 (matches TCS elevation scale)
        var mapContent =
            "NJominiMap = {\n" +
            $"\tWORLD_EXTENTS_X = {L.Map.MapWidth - 1}\n" +
            "\tWORLD_EXTENTS_Y = 150\n" +
            $"\tWORLD_EXTENTS_Z = {L.Map.MapHeight - 1}\n" +
            "\tWATERLEVEL = 11.765\n" +
            "}\n";

        var mapPath = Helper.GetPath(definesDir, "mapsize_defines.txt");
        await File.WriteAllTextAsync(mapPath, mapContent, Helper.Utf8Bom);

        // common/defines/graphic/00_graphics.txt — map-size camera panning limits
        // (minimal subset; full zoom-steps array causes assertion failures on CK3 1.12.5)
        var graphicsDir = Helper.GetPath(definesDir, "graphic");
        Directory.CreateDirectory(graphicsDir);

        var graphicsContent =
            "NCamera = {\n" +
            "\tFOV = 60\n" +
            "\tZNEAR = 10\n" +
            "\tZFAR = 10000\n" +
            "\tEDGE_SCROLLING_PIXELS = 10\n" +
            "\tSCROLL_SPEED = 0.045\n" +
            "\tZOOM_RATE = 0.2\n" +
            $"\tPANNING_WIDTH = {L.Map.MapWidth}\n" +
            $"\tPANNING_HEIGHT = {L.Map.MapHeight}\n" +
            "}\n";

        var graphicsPath = Helper.GetPath(graphicsDir, "00_graphics.txt");
        await File.WriteAllTextAsync(graphicsPath, graphicsContent, Helper.Utf8Bom);

        Logger.Info("Wrote mapsize_defines.txt and common/defines/graphic/00_graphics.txt");
    }
}
