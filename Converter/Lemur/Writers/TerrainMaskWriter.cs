using ImageMagick;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class TerrainMaskWriter
{
    // Maps mask filename → list of Azgaar biome ints that contribute white pixels to it.
    // Source: upstream BiomeConverter.cs + inspection of UpstreamTest gfx/map/terrain/.
    private static readonly Dictionary<string, int[]> BiomeMasks = new()
    {
        ["desert_01_mask.png"]          = new[] { 1 },
        ["mountain_02_desert_mask.png"] = new[] { 2 },
        ["plains_01_mask.png"]          = new[] { 3, 4 },
        ["farmland_01_mask.png"]        = new[] { 5 },
        ["forest_leaf_01_mask.png"]     = new[] { 6 },
        ["forest_jungle_01_mask.png"]   = new[] { 7 },
        ["forest_pine_01_mask.png"]     = new[] { 8, 9 },
        ["mountain_02_snow_mask.png"]   = new[] { 10 },
        ["mountain_02_c_snow_mask.png"] = new[] { 11 },
        ["wetlands_02_mask.png"]        = new[] { 12 },
    };

    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing terrain mask PNGs");

        var terrainDir = Helper.GetPath(outputDirectory, "gfx", "map", "terrain");
        Directory.CreateDirectory(terrainDir);

        var landCells = map.Cells!.Values
            .Where(c => L.Cell.IsDryLand(c.Type))
            .ToList();

        // Compute max height for height-derived masks (hills / mountains).
        int maxHeight = landCells.Count > 0 ? landCells.Max(c => c.Height) : 1;
        if (maxHeight == 0) maxHeight = 1;

        var readSettings = new MagickReadSettings
        {
            Width = L.Map.MapWidth,
            Height = L.Map.MapHeight,
        };

        // Biome-based masks
        foreach (var (fileName, biomes) in BiomeMasks)
        {
            var biomeSet = new HashSet<int>(biomes);
            var matchingCells = landCells.Where(c => biomeSet.Contains(c.Biome)).ToList();
            await WriteMask(matchingCells, map, terrainDir, fileName, readSettings);
        }

        // Height-derived: hills (40–70% of max height)
        var hillsCells = landCells
            .Where(c => c.Height >= maxHeight * 0.40 && c.Height < maxHeight * 0.70)
            .ToList();
        await WriteMask(hillsCells, map, terrainDir, "hills_01_mask.png", readSettings);

        // Height-derived: mountains (>70% of max height)
        var mountainCells = landCells
            .Where(c => c.Height >= maxHeight * 0.70)
            .ToList();
        await WriteMask(mountainCells, map, terrainDir, "mountain_02_mask.png", readSettings);

        // Oasis: rare feature — empty mask for MVP
        await WriteMask(new List<L.Cell>(), map, terrainDir, "oasis_mask.png", readSettings);

        Logger.Info("Wrote 13 terrain mask PNGs to gfx/map/terrain/");
    }

    private static async Task WriteMask(
        List<L.Cell> cells, L.Map map, string terrainDir, string fileName,
        MagickReadSettings readSettings)
    {
        using var image = new MagickImage("xc:black", readSettings);
        if (cells.Count > 0)
        {
            var drawables = ImageUtility.GenerateCellPolygons(cells, MagickColors.White, map);
            image.Draw(drawables);
        }
        await image.WriteAsync(Path.Combine(terrainDir, fileName));
    }
}
