using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class MapDefinesWriter
{
    public static async Task Write(string outputDirectory)
    {
        var definesDir = Helper.GetPath(outputDirectory, "common", "defines");
        Directory.CreateDirectory(definesDir);

        // mapsize_defines.txt — extents are 0-based max (width-1, height-1)
        //
        // WORLD_EXTENTS_Y and WATERLEVEL match upstream's formula exactly:
        //   WORLD_EXTENTS_Y = MaxElevation = 51
        //   WATERLEVEL = (MaxElevation / 255.0) * CK3WaterLevel = (51/255) * 20 = 4.0
        //
        // HeightmapWriter draws land starting at greyscale 20 (CK3WaterLevel).
        // In world-space: (20/255) * 51 = 4.0, which equals WATERLEVEL exactly.
        // CK3 renders pixels AT or BELOW WATERLEVEL as ocean, so land (grey=20 → 4.0)
        // sits right at the water surface — matching upstream's behaviour.
        const int maxElevation = 51;
        const int ck3WaterLevel = 20;  // must match HeightmapWriter.CK3WaterLevel
        var waterLevel = ((float)maxElevation / 255f) * ck3WaterLevel;
        var mapContent =
            "NJominiMap = {\n" +
            $"\tWORLD_EXTENTS_X = {L.Map.MapWidth - 1}\n" +
            $"\tWORLD_EXTENTS_Y = {maxElevation}\n" +
            $"\tWORLD_EXTENTS_Z = {L.Map.MapHeight - 1}\n" +
            $"\tWATERLEVEL = {waterLevel}\n" +
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
