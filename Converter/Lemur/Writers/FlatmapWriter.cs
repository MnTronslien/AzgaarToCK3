using ImageMagick;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class FlatmapWriter
{
    // Approximate terrain colors per Azgaar biome for the fallback (no SVG) path.
    private static readonly Dictionary<int, MagickColor> BiomeColors = new()
    {
        [1]  = new MagickColor("#F0D898"),  // Hot Desert — sandy
        [2]  = new MagickColor("#C8BCA8"),  // Cold Desert — light gray
        [3]  = new MagickColor("#C8C87A"),  // Savanna — pale yellow-green
        [4]  = new MagickColor("#90B450"),  // Grassland — mid green
        [5]  = new MagickColor("#7A9C3C"),  // Tropical Seasonal Forest
        [6]  = new MagickColor("#4A7828"),  // Temperate Deciduous Forest
        [7]  = new MagickColor("#246024"),  // Tropical Rainforest
        [8]  = new MagickColor("#246048"),  // Temperate Rainforest
        [9]  = new MagickColor("#204820"),  // Taiga
        [10] = new MagickColor("#C0D0E8"),  // Tundra
        [11] = new MagickColor("#E8ECF8"),  // Glacier
        [12] = new MagickColor("#608060"),  // Wetland
    };

    private static readonly MagickColor DefaultLandColor = new("#907848"); // neutral brownish

    public static async Task Write(L.Map map, string? svgPath, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing flatmap DDS");

        var flatmapDir = Helper.GetPath(outputDirectory, "gfx", "map", "terrain", "flat_maps");
        Directory.CreateDirectory(flatmapDir);

        var flatmapPath = Path.Combine(flatmapDir, "flatmap.dds");
        var flatmapTgpPath = Path.Combine(flatmapDir, "flatmap_tgp.dds");

        if (!string.IsNullOrWhiteSpace(svgPath) && File.Exists(svgPath))
            await WriteFlatmapFromSvg(svgPath, flatmapPath, flatmapTgpPath);
        else
            await WriteFlatmapFromCells(map, flatmapPath, flatmapTgpPath, svgPath);

        Logger.Info("Wrote flatmap.dds and flatmap_tgp.dds");
    }

    private static async Task WriteFlatmapFromSvg(string svgPath, string flatmapPath, string flatmapTgpPath)
    {
        Logger.Info($"Generating flatmap from SVG: {svgPath}");
        var readSettings = new MagickReadSettings
        {
            Width = L.Map.MapWidth,
            Height = L.Map.MapHeight,
            Format = MagickFormat.Svg,
        };
        using var image = new MagickImage(svgPath, readSettings);
        image.Resize(L.Map.MapWidth, L.Map.MapHeight);
        image.HasAlpha = false;
        await image.WriteAsync(flatmapPath, MagickFormat.Dds);
        File.Copy(flatmapPath, flatmapTgpPath, overwrite: true);
    }

    private static async Task WriteFlatmapFromCells(L.Map map, string flatmapPath, string flatmapTgpPath, string? svgPath)
    {
        if (svgPath != null)
            Logger.Info($"SVG not found at '{svgPath}' — generating flatmap from cell biome data");
        else
            Logger.Info("No SVG path configured — generating flatmap from cell biome data");

        var settings = new MagickReadSettings { Width = L.Map.MapWidth, Height = L.Map.MapHeight };
        using var image = new MagickImage("xc:#4080B0", settings); // ocean blue background

        // Draw land cells grouped by biome
        var cellsByBiome = map.Cells!.Values
            .Where(c => L.Cell.IsDryLand(c.Type))
            .GroupBy(c => c.Biome);

        foreach (var group in cellsByBiome)
        {
            var color = BiomeColors.TryGetValue(group.Key, out var c) ? c : DefaultLandColor;
            var drawables = ImageUtility.GenerateCellPolygons(group.ToList(), color, map);
            image.Draw(drawables);
        }

        image.HasAlpha = false;
        await image.WriteAsync(flatmapPath, MagickFormat.Dds);
        File.Copy(flatmapPath, flatmapTgpPath, overwrite: true);
    }
}
