using System.Numerics;
using ImageMagick;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

/// <summary>
/// Generates CK3 heightmap files from Azgaar cell elevation data.
///
/// Outputs:
///   map_data/heightmap.png             — 8-bit grayscale, 8192×4096
///   map_data/packed_heightmap.png      — hierarchical tile structure
///   map_data/indirection_heightmap.png — tile index map, 256×128
///   map_data/heightmap.heightmap       — text config
///
/// Algorithm adapted from upstream HeightMapConverter.cs (pryvyd9/AzgaarToCK3).
/// The packed/indirection generation is ported verbatim; only the initial
/// heightmap.png draw step is replaced (upstream uses SVG; we use cell polygons).
/// </summary>
public static class HeightmapWriter
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Constants (mirror upstream HeightMapConverter)
    // ──────────────────────────────────────────────────────────────────────────
    // (waterline byte threshold lives on HeightmapAlgorithm.MaxWaterByte — single source of truth)

    private static readonly int[] detailSize = [33, 17, 9, 5, 3];
    private static readonly byte[] averageSize = [1, 2, 4, 8, 16];
    private const int IndirectionProportion = 32;
    private static int MaxColumnN => L.Map.MapWidth / IndirectionProportion;
    private static int PackedWidth => MaxColumnN * 17;

    // Percentile cutoffs for detail-level bucketing (higher = stricter; only the
    // top tail goes to the highest-detail bucket). Splits within the non-zero
    // tile population, cumulative from the top of the metric distribution:
    //   L0 = top 12.5%, L1 = next 25%, L2 = next 25%, L3 = next 25%,
    //   L4 = bottom 12.5% + all zero-metric tiles (ocean / dead-flat).
    private const double PercentileLevel0 = 0.875;   // tiles ≥ this percentile → L0
    private const double PercentileLevel1 = 0.625;
    private const double PercentileLevel2 = 0.375;
    private const double PercentileLevel3 = 0.125;

    // ──────────────────────────────────────────────────────────────────────────
    //  Public packing stats — what TerrainLab inspects after PackAndWrite.
    // ──────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Diagnostic numbers returned by <see cref="PackAndWrite"/>.
    /// All five detail levels are always present in TilesPerDetailLevel (zero if unused).
    /// Detail level 0 = highest detail (33×33 px tile, 1× averaging),
    /// level 4 = lowest detail (3×3 px tile, 16× averaging).
    ///
    /// PerTileMetrics is non-empty only if a debug PNG was requested — the harness uses
    /// the raw float values to print distribution percentiles vs the threshold cutoffs.
    /// </summary>
    public sealed record PackingStats(
        IReadOnlyList<int> TilesPerDetailLevel,
        int IndirectionWidth,
        int IndirectionHeight,
        int PackedWidth,
        int PackedHeight,
        IReadOnlyList<PerTileMetric> PerTileMetrics);

    /// <summary>
    /// One indirection-tile metric sample emitted by PackAndWrite for diagnostic use.
    /// IndirRowFromSouth follows the gradient-array convention (south-up); flip with
    /// `IndirectionHeight - 1 - row` to convert to north-up display coordinates.
    /// </summary>
    public readonly record struct PerTileMetric(
        int IndirCol, int IndirRowFromSouth, float Value);

    // ──────────────────────────────────────────────────────────────────────────
    //  Entry point
    // ──────────────────────────────────────────────────────────────────────────
    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing heightmap files");

        var mapDataDir = Helper.GetPath(outputDirectory, "map_data");
        Directory.CreateDirectory(mapDataDir);

        var heightmapPath = Helper.GetPath(mapDataDir, "heightmap.png");

        var (pixels, heightmapF) = await GenerateHeightmap(map, heightmapPath);

        // Stash on Map so TerrainMaskWriter can use them for steepness-based splat materials.
        // The TerrainMasks writer must run AFTER this one — ordering enforced in ConversionManager.
        map.HeightmapPixels = pixels;
        map.HeightmapF = heightmapF;

        var masksDir = Helper.GetPath(outputDirectory, "gfx", "map", "terrain", "masks");
        var packed = await CreatePackedHeightmap(pixels, L.Map.MapWidth, L.Map.MapHeight);
        await Task.WhenAll(
            WritePackedHeightmap(packed, mapDataDir),
            HeightmapMasks.Write(heightmapF, pixels, L.Map.MapWidth, L.Map.MapHeight, masksDir));

        Logger.Info("HeightmapWriter: wrote heightmap.png, packed_heightmap.png, indirection_heightmap.png, heightmap.heightmap + geometry masks");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Lab entry point — pack an existing heightmap byte array and emit the
    //  three CK3 packed files. Use this when iterating on the packing algorithm
    //  in isolation (e.g. TerrainLab --pack-heightmap). The full-converter path
    //  goes through Write(map, outputDirectory) above.
    //
    //  If detailLevelsDebugPath is set, ALSO writes a per-indirection-tile
    //  color-coded PNG showing which detail level the packer assigned to each
    //  tile — the killer diagnostic for "is the algorithm making sensible
    //  detail-level decisions across the map?".
    // ──────────────────────────────────────────────────────────────────────────
    public static async Task<PackingStats> PackAndWrite(
        byte[] pixels, int width, int height, string mapDataDir,
        string? detailLevelsDebugPath = null,
        string? metricDebugPath = null)
    {
        Directory.CreateDirectory(mapDataDir);

        // Capture per-tile metric values when ANY debug PNG is requested. Cheap — it's
        // just the float we already compute, copied out by (col, row, value).
        var metrics = (detailLevelsDebugPath != null || metricDebugPath != null)
            ? new List<PerTileMetric>()
            : null;

        var packed = await CreatePackedHeightmap(pixels, width, height, metrics);
        await WritePackedHeightmap(packed, mapDataDir);

        var tilesPerLevel = new int[detailSize.Length];
        for (int i = 0; i < packed.Details.Length; i++)
        {
            var d = packed.Details[i];
            if (d == null) continue;
            int sum = 0;
            foreach (var row in d.Rows) sum += row.Length;
            tilesPerLevel[i] = sum;
        }

        if (detailLevelsDebugPath != null)
            await WriteDetailLevelDebugPng(packed, width, height, detailLevelsDebugPath);

        if (metricDebugPath != null && metrics != null)
            await WriteMetricDebugPng(metrics, width, height, metricDebugPath);

        return new PackingStats(
            TilesPerDetailLevel: tilesPerLevel,
            IndirectionWidth:  width  / IndirectionProportion,
            IndirectionHeight: height / IndirectionProportion,
            PackedWidth:  PackedWidth,
            PackedHeight: packed.PixelHeight,
            PerTileMetrics: metrics ?? (IReadOnlyList<PerTileMetric>)Array.Empty<PerTileMetric>());
    }

    // Writes a grayscale PNG sized to the indirection grid where each pixel encodes
    // the raw per-tile metric value, linearly mapped from [min..max] → [0..255].
    // Use this to see the *distribution* of the metric across the map: bimodal vs
    // smooth, where the high-detail signal actually lives, etc.
    //
    // Linear scaling shows the signal as the algorithm sees it — if the high tail
    // is much wider than the bulk, the bulk will compress to near-black; that
    // itself is a useful observation about the metric.
    private static async Task WriteMetricDebugPng(
        IReadOnlyList<PerTileMetric> metrics, int width, int height, string path)
    {
        int ihW = width  / IndirectionProportion;
        int ihH = height / IndirectionProportion;
        var px = new byte[ihW * ihH];   // single-channel grayscale

        float lo =  float.PositiveInfinity, hi = float.NegativeInfinity;
        foreach (var m in metrics)
        {
            if (m.Value < lo) lo = m.Value;
            if (m.Value > hi) hi = m.Value;
        }
        if (!float.IsFinite(lo) || !float.IsFinite(hi) || hi <= lo) hi = lo + 1f;
        float range = hi - lo;

        foreach (var m in metrics)
        {
            int col    = m.IndirCol;
            int rowPng = ihH - 1 - m.IndirRowFromSouth;   // match orientation of detail-level PNG
            if (col < 0 || col >= ihW || rowPng < 0 || rowPng >= ihH) continue;
            float t = (m.Value - lo) / range;
            byte v = (byte)Math.Clamp((int)(t * 255f), 0, 255);
            px[rowPng * ihW + col] = v;
        }

        var settings = new MagickReadSettings
        {
            Width = ihW,
            Height = ihH,
            ColorSpace = ColorSpace.Gray,
            Format = MagickFormat.Gray,
        };
        using var img = new MagickImage(px, settings);
        img.Depth = 8;
        await img.WriteAsync(path, MagickFormat.Png);
    }

    // Writes an indirection-grid-sized PNG (width/32 × height/32) where each
    // pixel is colored by the detail level assigned to that tile:
    //   level 0 (highest detail) = bright red
    //   level 1                  = orange
    //   level 2                  = yellow
    //   level 3                  = green
    //   level 4 (lowest detail)  = blue
    //   unassigned (e.g. open ocean tiles that hit no detail bucket) = black
    // The coordinate space matches packed.Details[i].Coordinates — those are
    // the top-left pixel coords of each tile's source area, sampled every
    // IndirectionProportion (32) px.
    private static async Task WriteDetailLevelDebugPng(
        PackedHeightmap packed, int width, int height, string path)
    {
        int ihW = width  / IndirectionProportion;
        int ihH = height / IndirectionProportion;
        var px = new byte[ihW * ihH * 3];   // RGB

        // Level colours roughly matching CK3 modder mental model
        (byte r, byte g, byte b)[] colors = new (byte, byte, byte)[]
        {
            (230,  40,  40),   // L0  bright red    — highest detail
            (240, 140,  40),   // L1  orange
            (230, 220,  40),   // L2  yellow
            ( 80, 200,  80),   // L3  green
            ( 60, 110, 220),   // L4  blue          — lowest detail
        };

        for (int di = 0; di < packed.Details.Length; di++)
        {
            var d = packed.Details[di];
            if (d == null) continue;
            var (r, g, b) = colors[Math.Min(di, colors.Length - 1)];

            foreach (var c in d.Coordinates)
            {
                int col = (int)c.X / IndirectionProportion;
                // c.Y is the iteration index over the second-derivative array, which is itself
                // built south-up by Gradient() (pixel row `height - vi - 1` is read at result[*, vi]).
                // WritePackedHeightmap applies the flip `verticalTiles - TileI - 1` so the
                // indirection PNG comes out north-up — do the same here to match.
                int rowFromSouth = (int)c.Y / IndirectionProportion;
                int rowPng = ihH - 1 - rowFromSouth;
                if (col < 0 || col >= ihW || rowPng < 0 || rowPng >= ihH) continue;
                int o = (rowPng * ihW + col) * 3;
                px[o] = r; px[o + 1] = g; px[o + 2] = b;
            }
        }

        var settings = new MagickReadSettings
        {
            Width = ihW,
            Height = ihH,
            ColorSpace = ColorSpace.sRGB,
            Format = MagickFormat.Rgb,
        };
        using var img = new MagickImage(px, settings);
        img.Depth = 8;
        await img.WriteAsync(path, MagickFormat.Png);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Step 1: Generate heightmap pixels via Delaunay + poly-node algorithm
    // ──────────────────────────────────────────────────────────────────────────
    private static async Task<(byte[] pixels, float[] heightmapF)> GenerateHeightmap(L.Map map, string outputPath)
    {
        using var timer = OperationTimer.Start("  Generating heightmap.png");

        var genParams = HeightmapAlgorithm.Params.FromMap(map);

        // Major rivers feed the algorithm as centerline TerrainNodes so the CDT
        // has interior anchors between the two bank coast chains, producing a
        // carved channel instead of dashed water-level scratches.
        IReadOnlyList<HeightmapAlgorithm.RiverInput>? riverInputs = null;
        if (map.Rivers != null && map.Rivers.Count > 0)
        {
            float threshold = Settings.Instance.MajorRiverThreshold;
            riverInputs = map.Rivers
                .Where(r => r.IsMajor(threshold) && r.ControlPoints != null && r.ControlPoints.Count >= 2)
                .Select(r => new HeightmapAlgorithm.RiverInput(
                    Id:            r.Id,
                    Width:         r.Width,
                    SourceWidth:   r.SourceWidth,
                    ControlPoints: r.ControlPoints!.ToArray()))
                .ToList();
        }

        var result = HeightmapAlgorithm.Generate(map.Cells!, genParams, riverInputs);

        var readSettings = new MagickReadSettings
        {
            Width      = L.Map.MapWidth,
            Height     = L.Map.MapHeight,
            ColorSpace = ColorSpace.Gray,
            Format     = MagickFormat.Gray,
        };
        using var image = new MagickImage(result.Pixels, readSettings);
        image.Depth = 8;
        await image.WriteAsync(outputPath, MagickFormat.Png);

        Logger.Info($"  heightmap.png written ({L.Map.MapWidth}x{L.Map.MapHeight}, {result.TerrainNodes.Count} terrain nodes, {result.PolyNodes.Count} poly-nodes)");
        return (result.Pixels, result.HeightmapF);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Inner types (verbatim from upstream HeightMapConverter)
    // ──────────────────────────────────────────────────────────────────────────
    private class Tile
    {
        public byte[,] Values = new byte[0, 0];
        public int TileI;   // indirection row (in tile units)
        public int TileJ;   // indirection column (in tile units)
    }

    private class Detail
    {
        public Tile[][] Rows = Array.Empty<Tile[]>();
        public Vector2[] Coordinates = Array.Empty<Vector2>();
    }

    private class PackedHeightmap
    {
        public Detail[] Details = Array.Empty<Detail>();
        public int PixelHeight;
        public int MapWidth;
        public int MapHeight;
        public int RowCount;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Step 2: Build PackedHeightmap data structure from heightmap.png
    //  (adapted from upstream CreatePackedHeightMap, using ImageMagick instead
    //   of SixLabors to stay consistent with the Lemur codebase)
    // ──────────────────────────────────────────────────────────────────────────
    private static Task<PackedHeightmap> CreatePackedHeightmap(
        byte[] pixels, int mapWidth, int mapHeight,
        List<PerTileMetric>? perTileMetricsOut = null)
    {
        using var _ = OperationTimer.Start("  Building packed heightmap structure");

        const int samplesPerTile = 32;

        // Per-tile metric = mean of first-derivative magnitudes — i.e. "average steepness
        // across this 32×32 patch". Replaces upstream's signed-sum-of-2nd-derivative,
        // which collapsed to ~0 on both flat ocean AND noisy interiors (cancellation),
        // pushing every middle bucket below the L0 threshold.
        //
        // Magnitude (Euclidean norm of the gradient vector) is non-negative, so the
        // distribution is single-sided (no negative tail) and rises monotonically from
        // 0 over ocean to large values over mountain slopes — exactly the signal the
        // detail-level bucketing wants.
        var firstDerivative = Gradient(pixels, mapWidth, mapHeight);

        var gradientAreas = new List<Vector2[,]>();
        var areaCoordinates = new List<Vector2>();

        for (int vi = 0; vi < firstDerivative.GetLength(1); vi += samplesPerTile)
            for (int hi = 0; hi < firstDerivative.GetLength(0); hi += samplesPerTile)
            {
                gradientAreas.Add(GetArea(firstDerivative, hi, vi, samplesPerTile, samplesPerTile));
                areaCoordinates.Add(new Vector2(hi, vi));
            }

        var weightedDerivatives = gradientAreas
            .Select((n, i) => (i, nonZeroP90: MeanMagnitude(n), coordinates: areaCoordinates[i]))
            .ToArray();

        if (perTileMetricsOut != null)
        {
            foreach (var w in weightedDerivatives)
                perTileMetricsOut.Add(new PerTileMetric(
                    IndirCol:           (int)w.coordinates.X / IndirectionProportion,
                    IndirRowFromSouth:  (int)w.coordinates.Y / IndirectionProportion,
                    Value:              w.nonZeroP90));
        }

        // Percentile-based detail-level cutoffs computed from the actual metric
        // distribution. Self-calibrating across maps: no re-tuning when the input
        // heightmap's gradient statistics shift (e.g. all-mountains vs all-flatland).
        //
        // Zero-metric tiles (true flat — pure ocean far from any coast, dead-flat
        // plateau interior) go straight to L4 as a sentinel bypass. Everything else
        // is percentile-bucketed within the non-zero population so the distribution
        // stays stable regardless of land-to-ocean ratio. On a 40%-land map the L4
        // share will be roughly the ocean share; on a 90%-land map most tiles still
        // see meaningful detail differentiation.
        //
        // Splits within the NON-ZERO tile population (cumulative from top):
        //   L0 = top 12.5%   (steepest fraction — mountain spines, sharp cliffs)
        //   L1 = next 25%
        //   L2 = next 25%
        //   L3 = next 25%
        //   L4 = bottom 12.5% (textured-but-near-flat ground) + all zero-metric tiles
        // Tweak via the PercentileLevel* constants at the top of the class.
        var nonZeroSorted = weightedDerivatives
            .Where(w => w.nonZeroP90 > 0f)
            .Select(w => w.nonZeroP90)
            .OrderBy(v => v)
            .ToArray();

        float Pct(double p)
        {
            if (nonZeroSorted.Length == 0) return 0f;
            int idx = (int)Math.Clamp(Math.Round(p * (nonZeroSorted.Length - 1)), 0, nonZeroSorted.Length - 1);
            return nonZeroSorted[idx];
        }
        float t0 = Pct(PercentileLevel0);
        float t1 = Pct(PercentileLevel1);
        float t2 = Pct(PercentileLevel2);
        float t3 = Pct(PercentileLevel3);

        // Note: comparisons use `> 0` (not `>=`) for the lower buckets so zero-metric
        // tiles always fall through to L4 regardless of where t3 lands.
        var detail = new[]
        {
            weightedDerivatives.Where(n => n.nonZeroP90 >= t0).ToArray(),
            weightedDerivatives.Where(n => n.nonZeroP90 >= t1 && n.nonZeroP90 < t0).ToArray(),
            weightedDerivatives.Where(n => n.nonZeroP90 >= t2 && n.nonZeroP90 < t1).ToArray(),
            weightedDerivatives.Where(n => n.nonZeroP90 >= t3 && n.nonZeroP90 < t2).ToArray(),
            weightedDerivatives.Where(n => n.nonZeroP90 < t3).ToArray(),
        };

        var detailSamples = detail.Select((d, i) =>
            d.Select(n => new Tile
            {
                Values = GetPackedArea(
                    pixels,
                    mapHeight - (int)n.coordinates.Y,
                    (int)n.coordinates.X,
                    i, mapHeight, mapWidth),
                TileI = (int)n.coordinates.Y / IndirectionProportion,
                TileJ = (int)n.coordinates.X / IndirectionProportion,
            }).ToArray()
        ).ToArray();

        var dPerLine = detailSize
            .Select(n => PackedWidth / n is var dpl && dpl > MaxColumnN ? MaxColumnN : dpl)
            .ToArray();

        var details = new Detail[detail.Length];
        int packedHeightPixels = 0;
        int? previousI = null;
        int rowCount = 0;

        for (int i = 0; i < details.Length; i++)
        {
            if (detailSamples[i].Length == 0) continue;

            var d = details[i] = new Detail();
            d.Coordinates = detail[i].Select(n => n.coordinates).ToArray();
            d.Rows = detailSamples[i].Chunk(dPerLine[i]).ToArray();

            packedHeightPixels += d.Rows.Length * detailSize[i];
            rowCount += d.Rows.Length;

            if (detailSamples[i].Length != 0)
                previousI = i;
        }

        return Task.FromResult(new PackedHeightmap
        {
            Details = details,
            PixelHeight = packedHeightPixels,
            MapWidth = mapWidth,
            MapHeight = mapHeight,
            RowCount = rowCount,
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Step 3: Write packed_heightmap.png, indirection_heightmap.png,
    //          and heightmap.heightmap
    //  (adapted from upstream WritePackedHeightMap, using ImageMagick)
    // ──────────────────────────────────────────────────────────────────────────
    private static async Task WritePackedHeightmap(PackedHeightmap heightmap, string mapDataDir)
    {
        using var _ = OperationTimer.Start("  Writing packed_heightmap.png + indirection");

        int horizontalTiles = heightmap.MapWidth / IndirectionProportion;
        int verticalTiles = heightmap.MapHeight / IndirectionProportion;

        // Indirection heightmap: RGBA8 pixels (colI, rowI, avgSize, detailLevel)
        var indirectionPixels = new byte[horizontalTiles * verticalTiles * 4]; // RGBA

        // Packed heightmap: 8-bit grayscale
        var packedPixels = new byte[PackedWidth * heightmap.PixelHeight];

        int verticalOffset = 0;
        int[] levelOffsets = new int[5];

        for (byte di = 0; di < detailSize.Length; di++)
        {
            var d = heightmap.Details[di];
            if (d is null) continue;

            int detailColCount = PackedWidth < MaxColumnN * detailSize[di]
                ? PackedWidth / detailSize[di]
                : MaxColumnN;

            for (int ri = 0; ri < d.Rows.Length; ri++)
            {
                if (ri == 0)
                    levelOffsets[di] = verticalOffset;

                verticalOffset += detailSize[di];

                byte colI = 0;
                var row = d.Rows[ri];

                for (int ti = 0; ti < row.Length; ti++, colI++)
                {
                    var tile = row[ti];

                    // Write tile pixels into packed_heightmap
                    for (int tx = 0; tx < detailSize[di]; tx++)
                        for (int ty = 0; ty < detailSize[di]; ty++)
                        {
                            var c = tile.Values[tx, ty];
                            packedPixels[PackedWidth * (heightmap.PixelHeight - verticalOffset + ty)
                                + ti * detailSize[di] + tx] = c;
                        }

                    // Write indirection entry
                    byte ihColumnIndex = colI;
                    byte ihRowIndex = (byte)ri;
                    byte ihDetailSize = averageSize[di];
                    byte ihDetailLevel = di;

                    int indirIdx = horizontalTiles * (verticalTiles - tile.TileI - 1) + tile.TileJ;
                    if (indirIdx >= 0 && indirIdx < horizontalTiles * verticalTiles)
                    {
                        int baseIdx = indirIdx * 4;
                        indirectionPixels[baseIdx + 0] = ihColumnIndex;
                        indirectionPixels[baseIdx + 1] = ihRowIndex;
                        indirectionPixels[baseIdx + 2] = ihDetailSize;
                        indirectionPixels[baseIdx + 3] = ihDetailLevel;
                    }
                }
            }
        }

        // Write packed_heightmap.png (grayscale)
        var phPath = Helper.GetPath(mapDataDir, "packed_heightmap.png");
        var phSettings = new MagickReadSettings
        {
            Width = PackedWidth,
            Height = heightmap.PixelHeight,
            ColorSpace = ColorSpace.Gray,
            Format = MagickFormat.Gray,
        };
        using (var packed = new MagickImage(packedPixels, phSettings))
        {
            packed.Depth = 8;
            await packed.WriteAsync(phPath, MagickFormat.Png);
        }
        Logger.Info($"  packed_heightmap.png written ({PackedWidth}x{heightmap.PixelHeight})");

        // Write indirection_heightmap.png (RGBA)
        var ihPath = Helper.GetPath(mapDataDir, "indirection_heightmap.png");
        var ihSettings = new MagickReadSettings
        {
            Width = horizontalTiles,
            Height = verticalTiles,
            ColorSpace = ColorSpace.sRGB,
            Format = MagickFormat.Rgba,
        };
        using (var indirection = new MagickImage(indirectionPixels, ihSettings))
        {
            indirection.Depth = 8;
            await indirection.WriteAsync(ihPath, MagickFormat.Png);
        }
        Logger.Info($"  indirection_heightmap.png written ({horizontalTiles}x{verticalTiles})");

        // Write heightmap.heightmap config
        var hhPath = Helper.GetPath(mapDataDir, "heightmap.heightmap");
        string levelOffsetsStr = string.Join(" ", levelOffsets.Select(n => $"{{ 0 {n} }}"));
        var config = $"heightmap_file=\"map_data/packed_heightmap.png\"\n" +
                     $"indirection_file=\"map_data/indirection_heightmap.png\"\n" +
                     $"original_heightmap_size={{ {heightmap.MapWidth} {heightmap.MapHeight} }}\n" +
                     $"tile_size=33\n" +
                     $"should_wrap_x=no\n" +
                     $"level_offsets={{ {levelOffsetsStr} }}\n" +
                     $"max_compress_level=4\n" +
                     $"empty_tile_offset={{ 255 127 }}\n";
        await File.WriteAllTextAsync(hhPath, config, Helper.Utf8Bom);
        Logger.Info("  heightmap.heightmap written");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helper methods (verbatim from upstream HeightMapConverter)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>First-order gradient from a flat byte pixel array.</summary>
    private static Vector2[,] Gradient(byte[] values, int width, int height)
    {
        var result = new Vector2[width, height];
        for (int vi = 1; vi < height; vi++)
            for (int hi = 1; hi < width; hi++)
            {
                var ci = (height - vi - 1) * width + hi;
                var li = ci - 1;
                var up = ci + width;
                var h = values[ci] - values[li];
                var v = values[ci] - values[up];
                result[hi, vi] = new Vector2(h, v);
            }
        return result;
    }

    /// <summary>Second-order gradient from a Vector2 array.</summary>
    private static Vector2[,] Gradient(Vector2[,] values, int width, int height)
    {
        var result = new Vector2[width, height];
        for (int vi = 1; vi < height; vi++)
            for (int hi = 1; hi < width; hi++)
            {
                var hd = values[hi, vi].X - values[hi - 1, vi].X;
                var vd = values[hi, vi].Y - values[hi, vi - 1].Y;
                result[hi, vi] = new Vector2(hd, vd);
            }
        return result;
    }

    /// <summary>Extract a rectangular sub-array from a 2-D Vector2 array.</summary>
    private static Vector2[,] GetArea(Vector2[,] values, int x, int y, int xl, int yl)
    {
        var result = new Vector2[xl, yl];
        for (int vi = y, j = 0; vi < y + yl; vi++, j++)
            for (int hi = x, i = 0; hi < x + xl; hi++, i++)
                result[i, j] = values[hi, vi];
        return result;
    }

    /// <summary>Mean of all X and Y components across a 2-D Vector2 array.</summary>
    private static float Avg(Vector2[,] values)
    {
        double sum = 0;
        int count = values.GetLength(0) * values.GetLength(1) * 2;
        for (int vi = 0; vi < values.GetLength(1); vi++)
            for (int hi = 0; hi < values.GetLength(0); hi++)
            {
                sum += values[hi, vi].X;
                sum += values[hi, vi].Y;
            }
        return count > 0 ? (float)(sum / count) : 0f;
    }

    /// <summary>
    /// Mean of Euclidean magnitudes √(x² + y²) across a 2-D Vector2 array, normalised
    /// to [0..1] by the maximum possible 8-bit gradient magnitude (√(255²+255²) ≈ 360.62).
    /// Used as the per-tile detail-level metric in CreatePackedHeightmap.
    /// </summary>
    private static float MeanMagnitude(Vector2[,] values)
    {
        const float maxByteGradient = 360.624458f;   // √(255² + 255²)
        double sum = 0;
        int count = values.GetLength(0) * values.GetLength(1);
        for (int vi = 0; vi < values.GetLength(1); vi++)
            for (int hi = 0; hi < values.GetLength(0); hi++)
            {
                var v = values[hi, vi];
                sum += Math.Sqrt(v.X * v.X + v.Y * v.Y);
            }
        return count > 0 ? (float)(sum / count) / maxByteGradient : 0f;
    }

    /// <summary>
    /// Sample a 33×17×9×5×3 tile from the flat pixel array with averaging.
    /// Verbatim from upstream GetPackedArea.
    /// </summary>
    private static byte[,] GetPackedArea(
        byte[] values, int tileI, int tileJ, int di, int height, int width)
    {
        var tileWidth = detailSize[di];
        var samples = new byte[tileWidth, tileWidth];

        var avgWidth = averageSize[di];
        var avgSize = avgWidth * avgWidth;

        for (int ci = tileI - IndirectionProportion, i = 0; i < tileWidth; i++, ci += avgWidth)
            for (int cj = tileJ, j = 0; j < tileWidth; j++, cj += avgWidth)
            {
                double avg = 0;
                var avgHalfWidth = (double)avgWidth / 2;
                var denominator = (double)avgSize;

                int aiFrom = (int)(ci - avgHalfWidth);
                int aiTo = (int)(ci + avgHalfWidth);
                if (tileI == IndirectionProportion)
                {
                    aiFrom = ci;
                    denominator /= 2;
                }
                else if (tileI == height)
                {
                    aiTo = ci;
                    denominator /= 2;
                }

                int ajFrom = (int)(cj - avgHalfWidth);
                int ajTo = (int)(cj + avgHalfWidth);
                if (tileJ == 0)
                {
                    ajFrom = cj;
                    denominator /= 2;
                }
                else if (tileJ == width - IndirectionProportion)
                {
                    ajTo = cj;
                    denominator /= 2;
                }

                for (int ai = aiFrom; ai < aiTo; ai++)
                    for (int aj = ajFrom; aj < ajTo; aj++)
                    {
                        if (ai >= 0 && ai < height && aj >= 0 && aj < width)
                            avg += (double)values[width * ai + aj] / denominator;
                    }

                // Transpose (verbatim from upstream)
                samples[j, i] = (byte)avg;
            }

        return samples;
    }
}
