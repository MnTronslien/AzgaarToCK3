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

        // common/defines/graphic/00_graphics.txt — camera settings
        // Full block matching upstream HeightMapConverter output (verified working on CK3 1.12.5).
        // START_LOOK_AT is proportional to map dimensions so it scales if map size changes.
        var graphicsDir = Helper.GetPath(definesDir, "graphic");
        Directory.CreateDirectory(graphicsDir);

        double startX = L.Map.MapWidth  * (4825.0 / 8192.0);
        double startZ = L.Map.MapHeight * (1900.0 / 4096.0);

        var graphicsContent =
            "NCamera = {\n" +
            "\tFOV = 60\n" +
            "\tZNEAR = 10\n" +
            "\tZFAR = 10000\n" +
            "\tEDGE_SCROLLING_PIXELS = 10\n" +
            "\tSCROLL_SPEED = 0.045\n" +
            "\tZOOM_RATE = 0.2\n" +
            "\tZOOM_STEPS = { 100 125 146 165 183 204 229 260 300 350 405 461 518 578 643 714 793 881 981 1092 1218 1360 1521 1703 1903 2116 2341 2573 2809 3047 3282 3512 3733 }\n" +
            "\tZOOM_STEPS_TILT = { 50 53 56 59 62 65 67 70 72 74 76 77 79 80 82 83 83 84 85 85 85 85 85 85 85 85 85 85 85 85 85 85 85 }\n" +
            "\tZOOM_STEPS_MIN_TILT = { 40 41 43 44 45 46 47 48 49 50 51 52 52 53 54 54 54 55 55 55 55 55 55 55 55 55 55 55 55 55 55 55 55 }\n" +
            "\tZOOM_STEPS_MAX_TILT = { 70 73 76 78 80 82 84 85 86 87 88 88 89 89 89 89 89 89 89 89 89 89 89 89 89 89 89 89 89 89 89 89 89 }\n" +
            "\tZOOM_AUDIO_PARAMETER_SCALE = 0.1\n" +
            "\tMAX_PAN_TO_ZOOM_STEP = 4\n" +
            $"\tSTART_LOOK_AT = {{ {startX:F1} 0 {startZ:F1} }}\n" +
            "\tTITLE_ZOOM_LEVEL_BY_EXTENT = { 20 15 13 11 9 7 5 4 3 }\n" +
            "\tTITLE_ZOOM_LEVEL_EXTENTS = { 1000 800 600 400 300 200 100 -1 }\n" +
            "\tTITLE_ZOOM_OFFSET_IF_LEFT_VIEW_SHOWN = { 230 175 145 120 95 70 50 40 30 }\n" +
            $"\tPANNING_WIDTH = {L.Map.MapWidth}\n" +
            $"\tPANNING_HEIGHT = {L.Map.MapHeight}\n" +
            "}\n";

        var graphicsPath = Helper.GetPath(graphicsDir, "00_graphics.txt");
        await File.WriteAllTextAsync(graphicsPath, graphicsContent, Helper.Utf8Bom);

        Logger.Info("Wrote mapsize_defines.txt and common/defines/graphic/00_graphics.txt");
    }
}
