using Converter.Lemur.Deserialization;
using Converter.Lemur.Splats;
using ImageMagick;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class TerrainMaskWriter
{
    private static readonly string CacheDir = Helper.GetPath(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AzgaarToCK3", "cache");

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
        Directory.CreateDirectory(CacheDir);

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
        // Paint all TCS mask slots black — only hills/mountain masks (written by HeightmapMasks) carry data.
        var allMasks = masks.Select(m => new TerrainMaskEntry(m.FileName, [])).Concat(blanks).ToList();

        // Canonical blank PNG — created once here, shared by biome masks and masks_gen.
        // All 69 biome mask slots are black; MagickImage per-file took ~200s; File.Copy is ~0.1s.
        var blankPath = Helper.GetPath(CacheDir, $"blank_{L.Map.MapWidth}x{L.Map.MapHeight}.png");
        if (!File.Exists(blankPath))
        {
            using var img = new MagickImage("xc:black", readSettings);
            img.Alpha(AlphaOption.Set);
            img.Evaluate(Channels.Alpha, EvaluateOperator.Set, new Percentage(100));
            await img.WriteAsync(blankPath);
            Logger.Debug($"Cached blank PNG {L.Map.MapWidth}×{L.Map.MapHeight}");
        }

        // Run all 5 groups concurrently — they write to different files, no shared mutable state.
        await Task.WhenAll(
            Task.WhenAll(allMasks.Select(entry => WriteBiomeMask(entry, masksDir, readSettings, map, blankPath))),
            WriteColormapAsync(tcsSandboxPath, terrainDir),
            WriteMasksGenAsync(tcsSandboxPath, terrainDir, blankPath),
            Task.Run(async () =>
            {
                using var img = RenderDetailIndex(map.Cells!, map.JsonMap.mapCoordinates);
                await img.WriteAsync(Helper.GetPath(terrainDir, "detail_index.tga"), MagickFormat.Tga);
            }),
            Task.Run(async () =>
            {
                using var img = RenderDetailIntensity(map.Cells!, map.JsonMap.mapCoordinates);
                await img.WriteAsync(Helper.GetPath(terrainDir, "detail_intensity.tga"), MagickFormat.Tga);
            })
        );

        Logger.Info($"Wrote {allMasks.Count} terrain mask PNGs ({masks.Count} biome + {blanks.Count} blank) + colormap.dds + masks_gen + detail TGAs to gfx/map/terrain/");
    }

    private static async Task WriteBiomeMask(
        TerrainMaskEntry entry, string masksDir,
        MagickReadSettings readSettings, L.Map map, string blankPath)
    {
        var dst = Path.Combine(masksDir, entry.FileName);
        if (entry.WhiteCells.Count == 0)
        {
            // Fast path: all biome mask slots are blank — copy the canonical cached file.
            File.Copy(blankPath, dst, overwrite: true);
            return;
        }
        using var image = new MagickImage("xc:black", readSettings);
        // CK3 1.18 optimizes away alpha channels that are entirely 0 — explicit alpha=255 required.
        image.Alpha(AlphaOption.Set);
        image.Evaluate(Channels.Alpha, EvaluateOperator.Set, new Percentage(100));
        var drawables = ImageUtility.GenerateCellPolygons(entry.WhiteCells, MagickColors.White, map);
        image.Draw(drawables);
        await image.WriteAsync(dst);
    }

    private static async Task WriteColormapAsync(string tcsSandboxPath, string terrainDir)
    {
        var src = Helper.GetPath(tcsSandboxPath, "gfx", "map", "terrain", "colormap.dds");
        var dst = Helper.GetPath(terrainDir, "colormap.dds");

        // Cache key: source path + last-write time + output dimensions → stable per TCS version.
        var srcInfo = new FileInfo(src);
        var cacheKey = $"{src}|{srcInfo.LastWriteTimeUtc.Ticks}|{L.Map.MapWidth / 4}|{L.Map.MapHeight / 4}";
        var cacheHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(cacheKey)))[..16];
        var cachePath = Helper.GetPath(CacheDir, $"colormap_{cacheHash}.dds");

        if (!File.Exists(cachePath))
        {
            using var img = new MagickImage(src);
            img.Resize(L.Map.MapWidth / 4, L.Map.MapHeight / 4);
            await img.WriteAsync(cachePath);
            Logger.Debug($"Cached resized colormap.dds → {cachePath}");
        }
        else
        {
            Logger.Debug("colormap.dds cache hit — skipping 163MB load");
        }

        File.Copy(cachePath, dst, overwrite: true);
    }

    private static async Task WriteMasksGenAsync(string tcsSandboxPath, string terrainDir, string blankPath)
    {
        var srcDir = Helper.GetPath(tcsSandboxPath, "gfx", "map", "terrain", "masks_gen");
        if (!Directory.Exists(srcDir)) return;

        var dstDir = Helper.GetPath(terrainDir, "masks_gen");
        Directory.CreateDirectory(dstDir);

        var fileNames = Directory.EnumerateFiles(srcDir, "*.png").Select(Path.GetFileName).ToList();

        // blankPath is pre-created by Write() — parallel File.Copy for each slot.
        await Task.WhenAll(fileNames.Select(fileName =>
            Task.Run(() => File.Copy(blankPath, Path.Combine(dstDir, fileName!), overwrite: true))));

        Logger.Verbose($"Generated {fileNames.Count} blank masks_gen PNGs at {L.Map.MapWidth}×{L.Map.MapHeight}");
    }

    // Azgaar biome index → CK3 biome index (used in detail_index.tga splat layers).
    // Indices match the CK3Biome enum in upstream BiomeConverter.
    // Internal so SplatmapBuilder can share the same mapping.
    internal static readonly Dictionary<int, int> AzgaarBiomeToCk3Index = new()
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

    // Public static so TerrainLab can call directly for fast iteration. Takes raw cells + coord
    // transform — no Map dependency, so cell dumps work too. Caller disposes.
    //
    // M1 (current): both files are built from the same Splatmap. Per-pixel rule is Delaunay-
    // barycentric over land-cell centroids — see SplatmapBuilder.BuildM1 and the canonical
    // splat-map write-up in CK3_MAP_MODDING_FACTS.md.
    //
    // detail_index R/G/B/A = biome index per layer (255 = "unused" sentinel).
    // detail_intensity R/G/B/A = blend weight per layer (sums to ~255 per pixel from barycentric).
    // The two files MUST agree slot-for-slot: same SplatPixel drives both.
    public static MagickImage RenderDetailIndex(IReadOnlyDictionary<int, L.Cell> cells, AzgaarMapCoordinates coords)
    {
        var splat = SplatmapBuilder.BuildM1(cells, coords);
        return SerialiseSplat(splat, intensityNotIndex: false);
    }

    public static MagickImage RenderDetailIntensity(IReadOnlyDictionary<int, L.Cell> cells, AzgaarMapCoordinates coords)
    {
        var splat = SplatmapBuilder.BuildM1(cells, coords);
        return SerialiseSplat(splat, intensityNotIndex: true);
    }

    // Packs the 4-layer Splatmap into a single 8-bit RGBA MagickImage.
    // When intensityNotIndex=false: writes BiomeIndex of each layer into R/G/B/A.
    // When intensityNotIndex=true:  writes Intensity   of each layer into R/G/B/A.
    private static MagickImage SerialiseSplat(Splatmap splat, bool intensityNotIndex)
    {
        int w = splat.Width, h = splat.Height;
        var bytes = new byte[w * h * 4];
        for (int i = 0; i < splat.Pixels.Length; i++)
        {
            var p = splat.Pixels[i];
            int o = i * 4;
            if (intensityNotIndex)
            {
                bytes[o    ] = p.L0.Intensity;
                bytes[o + 1] = p.L1.Intensity;
                bytes[o + 2] = p.L2.Intensity;
                bytes[o + 3] = p.L3.Intensity;
            }
            else
            {
                bytes[o    ] = p.L0.BiomeIndex;
                bytes[o + 1] = p.L1.BiomeIndex;
                bytes[o + 2] = p.L2.BiomeIndex;
                bytes[o + 3] = p.L3.BiomeIndex;
            }
        }

        var settings = new MagickReadSettings
        {
            Width = w,
            Height = h,
            Format = MagickFormat.Rgba,
            ColorSpace = ColorSpace.sRGB,
        };
        return new MagickImage(bytes, settings);
    }
}
