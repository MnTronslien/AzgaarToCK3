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
        L.Map map,
        string ck3Directory,
        string outputDirectory,
        bool mapEditor = false)
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

        var baseMasksDir = Helper.GetPath(ck3Directory, "game", "gfx", "map", "terrain", "masks");

        // Build the splatmap ONCE. It is the single source of truth: it drives detail_index/
        // detail_intensity (runtime rendering) and, in mapEditor mode, the per-material editor
        // masks too — so the editor opens onto exactly what the game renders, with smooth blended
        // weights instead of the old binary per-cell fill. map.HeightmapF/Pixels are populated by
        // HeightmapWriter (runs before this), enabling the steepness materials (hills, mountain).
        var splat = SplatmapBuilder.Build(map.Cells!, map.JsonMap.mapCoordinates,
            map.HeightmapF, map.HeightmapPixels, map.SteepnessField);

        // Canonical blank PNG — created once, File.Copy'd into every non-painted mask slot + masks_gen.
        // Cache key includes "8bit": a prior build cached a 1-bit blank here, and File.Copy
        // would propagate that to every blank slot + masks_gen. Bumping the key forces an
        // 8-bit regeneration and avoids the stale-cache trap.
        var blankPath = Helper.GetPath(CacheDir, $"blank_8bit_{L.Map.MapWidth}x{L.Map.MapHeight}.png");
        if (!File.Exists(blankPath))
        {
            using var img = new MagickImage("xc:black", readSettings);
            ForceEditorMaskFormat(img);
            await img.WriteAsync(blankPath);
            Logger.Debug($"Cached blank PNG {L.Map.MapWidth}×{L.Map.MapHeight}");
        }

        // mapEditor: rasterise one editor mask per MaterialRegistry material from the splatmap
        // (filename = "<texture>_mask.png", value = that material's blend intensity per pixel).
        // Every other CK3 mask slot — and the whole set when not in mapEditor mode — stays blank.
        var splatMasks = mapEditor
            ? MaterialRegistry.All
                .Where(mat => Ck3MaterialBytes.ByName.ContainsKey(mat.TextureName))
                .ToDictionary(mat => mat.TextureName + "_mask.png",
                              mat => Ck3MaterialBytes.ByName[mat.TextureName],
                              StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        var baseMaskFiles = Directory.Exists(baseMasksDir)
            ? Directory.EnumerateFiles(baseMasksDir, "*.png").Select(Path.GetFileName).ToList()
            : [];
        int painted = baseMaskFiles.Count(f => splatMasks.ContainsKey(f!));

        // Run all groups concurrently — they write to different files, no shared mutable state.
        await Task.WhenAll(
            Task.WhenAll(baseMaskFiles.Select(f =>
            {
                var dst = Path.Combine(masksDir, f!);
                return splatMasks.TryGetValue(f!, out var materialByte)
                    ? WriteSplatMask(splat, materialByte, dst)
                    : Task.Run(() => File.Copy(blankPath, dst, overwrite: true));
            })),
            WriteColormapAsync(terrainDir),
            WriteMasksGenAsync(ck3Directory, terrainDir, blankPath),
            Task.Run(async () =>
            {
                using var img = SerialiseSplat(splat, intensityNotIndex: false);
                await img.WriteAsync(Helper.GetPath(terrainDir, "detail_index.tga"), MagickFormat.Tga);
            }),
            Task.Run(async () =>
            {
                using var img = SerialiseSplat(splat, intensityNotIndex: true);
                await img.WriteAsync(Helper.GetPath(terrainDir, "detail_intensity.tga"), MagickFormat.Tga);
            })
        );

        Logger.Info($"Wrote {baseMaskFiles.Count} terrain mask PNGs ({painted} splat-painted + {baseMaskFiles.Count - painted} blank) + colormap.dds + masks_gen + detail TGAs to gfx/map/terrain/");
    }

    // Rasterise one terrain mask from the splatmap: each pixel's grey value is the blend intensity
    // of the layer whose material byte matches `materialByte` (0 where this material is absent).
    // This is the same data CK3 renders from detail_index/intensity, so the editor mask matches the
    // in-game terrain exactly — with smooth blended edges, not hard per-cell boundaries.
    private static async Task WriteSplatMask(Splatmap splat, byte materialByte, string dst)
    {
        int w = splat.Width, h = splat.Height;
        var px = await Task.Run(() =>
        {
            var buf = new byte[w * h];
            for (int i = 0; i < buf.Length; i++)
            {
                var p = splat.Pixels[i];
                int v = 0;
                if (p.L0.BiomeIndex == materialByte) v += p.L0.Intensity;
                if (p.L1.BiomeIndex == materialByte) v += p.L1.Intensity;
                if (p.L2.BiomeIndex == materialByte) v += p.L2.Intensity;
                if (p.L3.BiomeIndex == materialByte) v += p.L3.Intensity;
                buf[i] = v > 255 ? (byte)255 : (byte)v;
            }
            return buf;
        });

        var settings = new MagickReadSettings
        {
            Width = w, Height = h,
            ColorSpace = ColorSpace.Gray,
            Format = MagickFormat.Gray,
        };
        using var img = new MagickImage(px, settings);
        ForceEditorMaskFormat(img);
        await img.WriteAsync(dst, MagickFormat.Png);
    }

    // The CK3 map editor rejects 1-bit masks. A single-colour PNG (the all-black blank) or a
    // two-colour black/white biome mask is otherwise collapsed to 1-bit grayscale by the PNG
    // encoder. Force 8-bit grayscale so the editor loads them; harmless to the CK3 runtime
    // (the prior alpha=255 workaround was dropped here — uniform-opaque alpha was encoded away
    // anyway, so shipped masks were already grayscale; this just pins the depth at 8).
    private static void ForceEditorMaskFormat(MagickImage img)
    {
        img.Depth = 8;
        img.Settings.SetDefine(MagickFormat.Png, "bit-depth", "8");
        img.Settings.SetDefine(MagickFormat.Png, "color-type", "0"); // 0 = grayscale
    }

    private static async Task WriteColormapAsync(string terrainDir)
    {
        // Flat mid-grey (132,132,132) colormap at map/4 resolution, to override vanilla's
        // real-world-geography colormap so our map isn't Earth-tinted.
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
