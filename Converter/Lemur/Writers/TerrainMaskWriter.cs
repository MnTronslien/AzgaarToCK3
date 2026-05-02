using ImageMagick;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class TerrainMaskWriter
{
    public static async Task Write(
        IReadOnlyList<TerrainMaskEntry> masks,
        L.Map map,
        string tcsSandboxPath,
        string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing terrain mask files");

        var masksDir = Helper.GetPath(outputDirectory, "gfx", "map", "terrain", "masks");
        var terrainDir = Helper.GetPath(outputDirectory, "gfx", "map", "terrain");
        Directory.CreateDirectory(masksDir);
        Directory.CreateDirectory(terrainDir);

        var readSettings = new MagickReadSettings
        {
            Width = L.Map.MapWidth,
            Height = L.Map.MapHeight,
        };

        // Supplement with blank masks for every TCS mask filename we don't explicitly set.
        // Without this, TCS files at 9216×4608 leak through at wrong scale.
        var baseMasksDir = Helper.GetPath(tcsSandboxPath, "gfx", "map", "terrain", "masks");
        var covered = masks.Select(m => m.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blanks = Directory.Exists(baseMasksDir)
            ? Directory.EnumerateFiles(baseMasksDir, "*.png")
                .Select(Path.GetFileName)
                .Where(f => !covered.Contains(f!))
                .Select(f => new TerrainMaskEntry(f!, []))
                .ToList()
            : [];
        var allMasks = masks.Concat(blanks).ToList();

        await Task.WhenAll(allMasks.Select(entry => WriteBiomeMask(entry, masksDir, readSettings, map)));

        await WriteColormapAsync(tcsSandboxPath, terrainDir);
        await WriteMasksGenAsync(tcsSandboxPath, terrainDir);
        await WriteDetailIndexAsync(terrainDir, map, readSettings);
        await WriteDetailIntensityAsync(terrainDir, map, readSettings);

        Logger.Info($"Wrote {allMasks.Count} terrain mask PNGs ({masks.Count} biome + {blanks.Count} blank) + colormap.dds + masks_gen + detail TGAs to gfx/map/terrain/");
    }

    private static async Task WriteBiomeMask(
        TerrainMaskEntry entry, string masksDir,
        MagickReadSettings readSettings, L.Map map)
    {
        using var image = new MagickImage("xc:black", readSettings);
        // CK3 1.18 optimizes away alpha channels that are entirely 0 — explicit alpha=255 required.
        image.Alpha(AlphaOption.Set);
        image.Evaluate(Channels.Alpha, EvaluateOperator.Set, new Percentage(100));
        if (entry.WhiteCells.Count > 0)
        {
            var drawables = ImageUtility.GenerateCellPolygons(entry.WhiteCells, MagickColors.White, map);
            image.Draw(drawables);
        }
        await image.WriteAsync(Path.Combine(masksDir, entry.FileName));
    }

    private static async Task WriteColormapAsync(string tcsSandboxPath, string terrainDir)
    {
        var src = Helper.GetPath(tcsSandboxPath, "gfx", "map", "terrain", "colormap.dds");
        var dst = Helper.GetPath(terrainDir, "colormap.dds");
        using var img = new MagickImage(src);
        img.Resize(L.Map.MapWidth / 4, L.Map.MapHeight / 4);
        await img.WriteAsync(dst);
    }

    private static async Task WriteMasksGenAsync(string tcsSandboxPath, string terrainDir)
    {
        var srcDir = Helper.GetPath(tcsSandboxPath, "gfx", "map", "terrain", "masks_gen");
        if (!Directory.Exists(srcDir)) return;

        var dstDir = Helper.GetPath(terrainDir, "masks_gen");
        Directory.CreateDirectory(dstDir);

        var readSettings = new MagickReadSettings { Width = L.Map.MapWidth, Height = L.Map.MapHeight };
        var fileNames = Directory.EnumerateFiles(srcDir, "*.png").Select(Path.GetFileName).ToList();

        var tasks = fileNames.Select(async fileName =>
        {
            using var img = new MagickImage("xc:black", readSettings);
            img.Alpha(AlphaOption.Set);
            img.Evaluate(Channels.Alpha, EvaluateOperator.Set, new Percentage(100));
            await img.WriteAsync(Path.Combine(dstDir, fileName!));
        });
        await Task.WhenAll(tasks);
        Logger.Verbose($"Generated {fileNames.Count} blank masks_gen PNGs at {L.Map.MapWidth}×{L.Map.MapHeight}");
    }

    // Azgaar biome index → CK3 biome index (R channel of detail_index.tga).
    // Indices match the CK3Biome enum in upstream BiomeConverter.
    private static readonly Dictionary<int, int> AzgaarBiomeToCk3Index = new()
    {
        [1]  = 32,  // HotDesert          → desert_01
        [2]  = 33,  // ColdDesert         → desert_02
        [3]  = 1,   // Savanna            → plains_01_dry
        [4]  = 0,   // Grassland          → plains_01
        [5]  = 5,   // TropicalSeasonalForest → farmland_01
        [6]  = 28,  // TemperateDeciduousForest → forest_leaf_01
        [7]  = 29,  // TropicalRainforest  → forest_jungle_01
        [8]  = 30,  // TemperateRainforest → forest_pine_01
        [9]  = 31,  // Taiga              → forestfloor
        [10] = 50,  // Tundra             → northern_plains_01
        [11] = 54,  // Glacier            → snow
        [12] = 14,  // Wetland            → floodplains_01
    };

    private static async Task WriteDetailIndexAsync(string terrainDir, L.Map map, MagickReadSettings readSettings)
    {
        // R channel = CK3 biome index; G=255 B=255 per upstream BiomeConverter convention.
        // Sea background = mud_wet_01 (index 6) — CK3 1.18 renders index 255 (all-white) as wrong colour.
        using var img = new MagickImage("xc:#06FFFF", readSettings);
        img.Alpha(AlphaOption.Set);
        img.Evaluate(Channels.Alpha, EvaluateOperator.Set, new Percentage(100));

        foreach (var group in map.Cells!.Values
            .Where(c => L.Cell.IsDryLand(c.Type))
            .GroupBy(c => c.Biome)
            .Where(g => AzgaarBiomeToCk3Index.ContainsKey(g.Key)))
        {
            var colour = new MagickColor($"#{AzgaarBiomeToCk3Index[group.Key]:X2}FFFF");
            var drawables = ImageUtility.GenerateCellPolygons(group.ToList(), colour, map);
            img.Draw(drawables);
        }

        await img.WriteAsync(Helper.GetPath(terrainDir, "detail_index.tga"), MagickFormat.Tga);
    }

    private static async Task WriteDetailIntensityAsync(string terrainDir, L.Map map, MagickReadSettings readSettings)
    {
        // Red channel = intensity: land cells painted red (R=255), sea remains black.
        // Alpha=255 throughout — CK3 1.18 crashes the GPU driver if the TGA lacks an alpha channel.
        using var img = new MagickImage("xc:black", readSettings);
        img.Alpha(AlphaOption.Set);
        img.Evaluate(Channels.Alpha, EvaluateOperator.Set, new Percentage(100));

        var landCells = map.Cells!.Values
            .Where(c => L.Cell.IsDryLand(c.Type))
            .ToList();

        if (landCells.Count > 0)
        {
            var drawables = ImageUtility.GenerateCellPolygons(landCells, MagickColors.Red, map);
            img.Draw(drawables);
        }

        await img.WriteAsync(Helper.GetPath(terrainDir, "detail_intensity.tga"), MagickFormat.Tga);
    }
}
