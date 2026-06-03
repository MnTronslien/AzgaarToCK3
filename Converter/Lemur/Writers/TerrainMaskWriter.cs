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
        string ck3Directory,
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

        // Supplement with a blank mask for every CK3 mask filename we don't explicitly set, so
        // vanilla's full-resolution masks don't leak through at the wrong scale. Filenames come from
        // the CK3 install (same set TCS shipped) — no TCS dependency.
        var baseMasksDir = Helper.GetPath(ck3Directory, "game", "gfx", "map", "terrain", "masks");
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
            WriteColormapAsync(terrainDir),
            WriteMasksGenAsync(ck3Directory, terrainDir, blankPath),
            Task.Run(async () =>
            {
                // map.HeightmapPixels/F are populated by HeightmapWriter if it ran first.
                // Null → steepness materials produce 0 weight → biome-only output (M1 equivalent).
                using var img = RenderDetailIndex(map.Cells!, map.JsonMap.mapCoordinates,
                    map.HeightmapF, map.HeightmapPixels);
                await img.WriteAsync(Helper.GetPath(terrainDir, "detail_index.tga"), MagickFormat.Tga);
            }),
            Task.Run(async () =>
            {
                using var img = RenderDetailIntensity(map.Cells!, map.JsonMap.mapCoordinates,
                    map.HeightmapF, map.HeightmapPixels);
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

    private static async Task WriteColormapAsync(string terrainDir)
    {
        // Generate a flat colormap (every pixel mid-grey 132,132,132) at map/4 resolution. Its only
        // job is to override vanilla's real-world-geography colormap so our map isn't Earth-tinted;
        // 132 is the exact uniform value TCS shipped, so the look is unchanged. No TCS, no shipped
        // binary. Enrichment (biome-driven tint) is a logged follow-up — see notes-for-later.md.
        await DdsWriter.WriteSolidAsync(
            Helper.GetPath(terrainDir, "colormap.dds"),
            L.Map.MapWidth / 4, L.Map.MapHeight / 4,
            r: 132, g: 132, b: 132, a: 255);
    }

    private static async Task WriteMasksGenAsync(string ck3Directory, string terrainDir, string blankPath)
    {
        var srcDir = Helper.GetPath(ck3Directory, "game", "gfx", "map", "terrain", "masks_gen");
        if (!Directory.Exists(srcDir)) return;

        var dstDir = Helper.GetPath(terrainDir, "masks_gen");
        Directory.CreateDirectory(dstDir);

        var fileNames = Directory.EnumerateFiles(srcDir, "*.png").Select(Path.GetFileName).ToList();

        // blankPath is pre-created by Write() — parallel File.Copy for each slot.
        await Task.WhenAll(fileNames.Select(fileName =>
            Task.Run(() => File.Copy(blankPath, Path.Combine(dstDir, fileName!), overwrite: true))));

        Logger.Verbose($"Generated {fileNames.Count} blank masks_gen PNGs at {L.Map.MapWidth}×{L.Map.MapHeight}");
    }

    // Public static so TerrainLab can call directly for fast iteration. Takes raw cells + coord
    // transform — no Map dependency, so cell dumps work too. Caller disposes.
    //
    // Both files are built from the same Splatmap. M2 uses MaterialRegistry — see
    // SplatmapBuilder.Build and the canonical splat-map write-up in CK3_MAP_MODDING_FACTS.md.
    //
    // detail_index R/G/B/A = CK3 material index per layer (255 = "unused" sentinel).
    // detail_intensity R/G/B/A = blend weight per layer (sums to 255 per pixel from normalisation).
    // The two files MUST agree slot-for-slot: same SplatPixel drives both.
    //
    // heightmapF + heightmapBytes: required for steepness-based materials (hills, mountain).
    // Pass null for biome-only output (equivalent to M1).
    public static MagickImage RenderDetailIndex(
        IReadOnlyDictionary<int, L.Cell> cells,
        AzgaarMapCoordinates coords,
        float[]? heightmapF = null,
        byte[]? heightmapBytes = null)
    {
        var splat = SplatmapBuilder.Build(cells, coords, heightmapF, heightmapBytes);
        return SerialiseSplat(splat, intensityNotIndex: false);
    }

    public static MagickImage RenderDetailIntensity(
        IReadOnlyDictionary<int, L.Cell> cells,
        AzgaarMapCoordinates coords,
        float[]? heightmapF = null,
        byte[]? heightmapBytes = null)
    {
        var splat = SplatmapBuilder.Build(cells, coords, heightmapF, heightmapBytes);
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
