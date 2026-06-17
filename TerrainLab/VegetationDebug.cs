using System.Linq;
using Converter.Lemur.Deserialization;
using Converter.Lemur.Entities;
using Converter.Lemur.Splats;
using Converter.Lemur.Writers;
using ImageMagick;

namespace TerrainLab;

// MVP vegetation debug renderer (feature/vegetation-mvp).
//
// Single-rule thickness: thickness at a pixel = one Azgaar biome's *blended* weight
// (BiomeWeightField — the splatmap interpolation, so it fades 1→0 across biome edges).
// Placement: count-per-coarse-square = round(thickness * maxPerSquare), scattered at random
// inside the square (seeded).
//
// Added per Mattias' steer (2026-06-17):
//   - ELEVATION FILTER: drop trees that are underwater or above a treeline (needs heightmap).
//   - SLIGHT TIGHTENING: per-mesh reverse-relaxation — nudge each tree toward the local centroid
//     of its nearest same-kind neighbours; Jacobi (snapshot → apply) so it's order-independent
//     and deterministic, with a min-gap floor so clumps can't collapse to a point.
//
// Renders tree dots over a thickness-tinted biome background, downscaled for emailability.
// Stays entirely in IMAGE space (origin top-left, Y down) — NO WorldPixel flip. This is the
// tuning harness; the flip only matters when emitting the real CK3 map_object_data file.
static class VegetationDebug
{
    public readonly record struct Options(
        AzgaarBiome Rule,
        int GridPx,
        float MaxPerSquare,
        int Seed,
        int Downscale,
        bool ElevationFilter,
        float Treeline01,        // reject land trees with height01 above this (treeline)
        float TightenStrength,   // 0 = off; ~0.08 = very slight
        int TightenIters);

    public static void Render(
        IReadOnlyDictionary<int, Cell> cells,
        AzgaarMapCoordinates coords,
        float lonW, float lonT, float latS, float latT,
        Options o,
        string outPath)
    {
        int W = Map.MapWidth, H = Map.MapHeight;

        Console.WriteLine($"Building biome weight field ({W}x{H})…");
        var biomes = BiomeWeightField.Build(cells, coords);

        // Diagnostic histogram so we can tell if the chosen rule's biome is even present.
        var hist = new int[13];
        foreach (var c in cells.Values)
            if (Cell.IsDryLand(c.Type) && c.Biome >= 1 && c.Biome <= 12) hist[c.Biome]++;
        Console.WriteLine("Land cells per biome: " + string.Join(", ",
            Enumerable.Range(1, 12).Where(b => hist[b] > 0)
                      .Select(b => $"{(AzgaarBiome)b}={hist[b]}")));

        // ── Optional heightmap pre-pass (≈30s) for the elevation filter ──
        byte[]? heightBytes = null;
        if (o.ElevationFilter)
        {
            Console.WriteLine("Heightmap pre-pass (for elevation filter)…");
            var hp = new HeightmapAlgorithm.Params(
                LonW: lonW, LonT: lonT, LatS: latS, LatT: latT, Width: W, Height: H);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            heightBytes = HeightmapAlgorithm.Generate(cells, hp).Pixels;
            sw.Stop();
            Console.WriteLine($"Heightmap done in {sw.Elapsed.TotalSeconds:F1}s.");
        }
        int treelineByte = (int)MathF.Round(o.Treeline01 * 255f);

        // ── Scatter: count per coarse square from thickness at the square centre ──
        int gw = (W + o.GridPx - 1) / o.GridPx;
        int gh = (H + o.GridPx - 1) / o.GridPx;
        var rng = new Random(o.Seed);
        var trees = new List<(float x, float y)>();
        int rejUnderwater = 0, rejTreeline = 0;
        var heights = new List<float>();
        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                int cx = Math.Min(gx * o.GridPx + o.GridPx / 2, W - 1);
                int cy = Math.Min(gy * o.GridPx + o.GridPx / 2, H - 1);
                float thickness = biomes[cy * W + cx].WeightOf(o.Rule);   // 0..1, blended
                if (thickness <= 0f) continue;
                int count = (int)MathF.Round(thickness * o.MaxPerSquare);
                for (int k = 0; k < count; k++)
                {
                    float px = gx * o.GridPx + (float)rng.NextDouble() * o.GridPx;
                    float py = gy * o.GridPx + (float)rng.NextDouble() * o.GridPx;
                    if (px >= W || py >= H) continue;

                    if (heightBytes != null)
                    {
                        byte hb = heightBytes[(int)py * W + (int)px];
                        if (hb <= HeightmapAlgorithm.MaxWaterByte) { rejUnderwater++; continue; }
                        if (hb > treelineByte) { rejTreeline++; continue; }
                        heights.Add(hb / 255f);
                    }
                    trees.Add((px, py));
                }
            }
        Console.WriteLine($"Placed {trees.Count} {o.Rule} trees (grid={o.GridPx}px, max/sq={o.MaxPerSquare}, seed={o.Seed}).");
        if (o.ElevationFilter)
        {
            Console.WriteLine($"Elevation filter: rejected {rejUnderwater} underwater, {rejTreeline} above treeline (height01 > {o.Treeline01:F2}).");
            if (heights.Count > 0)
            {
                heights.Sort();
                Console.WriteLine($"  kept-tree height01: min={heights[0]:F3} median={heights[heights.Count/2]:F3} max={heights[^1]:F3}");
            }
        }

        // ── Slight tightening (per-mesh; here all trees are one mesh) ──
        if (o.TightenStrength > 0f && o.TightenIters > 0)
        {
            Tighten(trees, W, H, o.TightenStrength, o.TightenIters, k: 6, minGap: 3f);
            Console.WriteLine($"Tightened: strength={o.TightenStrength}, iters={o.TightenIters}.");
        }

        // ── Render: thickness-tinted background + tree dots, downscaled ──
        int cw = W / o.Downscale, ch = H / o.Downscale;
        var buf = new byte[cw * ch * 3];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int sx = Math.Min(x * o.Downscale, W - 1);
                int sy = Math.Min(y * o.Downscale, H - 1);
                var bt = biomes[sy * W + sx];
                bool land = bt.W0 > 0 || bt.W1 > 0 || bt.W2 > 0;
                float t = bt.WeightOf(o.Rule);
                int idx = (y * cw + x) * 3;
                if (!land)
                {
                    buf[idx] = 198; buf[idx + 1] = 214; buf[idx + 2] = 232;   // sea: pale blue
                }
                else
                {
                    // pale tan (t=0) → muted green (t=1): the thickness gradient shows under the dots
                    buf[idx]     = (byte)(224 - 150 * t);
                    buf[idx + 1] = (byte)(216 -  60 * t);
                    buf[idx + 2] = (byte)(186 - 140 * t);
                }
            }
        // tree dots (2×2, dark green) painted over the background
        foreach (var (tx, ty) in trees)
        {
            int x = (int)(tx / o.Downscale), y = (int)(ty / o.Downscale);
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= cw || yy >= ch) continue;
                    int idx = (yy * cw + xx) * 3;
                    buf[idx] = 24; buf[idx + 1] = 72; buf[idx + 2] = 28;
                }
        }

        var settings = new MagickReadSettings
        {
            Width = cw,
            Height = ch,
            Format = MagickFormat.Rgb,
        };
        using var img = new MagickImage(buf, settings);
        var full = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        img.Write(full, MagickFormat.Png);
        Console.WriteLine($"Wrote vegetation debug image: {full} ({cw}x{ch})");
    }

    // Reverse-relaxation: nudge each point a small fraction toward the centroid of its k nearest
    // neighbours. Jacobi (read snapshot, write new) → order-independent, deterministic. A min-gap
    // floor leaves already-tight points put, so clumps tighten but don't collapse to a single dot.
    static void Tighten(List<(float x, float y)> trees, int W, int H, float strength, int iters, int k, float minGap)
    {
        int n = trees.Count;
        if (n < k + 1) return;
        var xs = new float[n];
        var ys = new float[n];
        for (int i = 0; i < n; i++) { xs[i] = trees[i].x; ys[i] = trees[i].y; }

        int cols = Math.Max(1, W / 32), rows = Math.Max(1, H / 32);
        for (int it = 0; it < iters; it++)
        {
            var grid = new SpatialGrid<int>(W, H, cols, rows);
            for (int i = 0; i < n; i++) grid.Add(xs[i], ys[i], i);

            var nx = new float[n];
            var ny = new float[n];
            for (int i = 0; i < n; i++)
            {
                var near = grid.NearestN(xs[i], ys[i], k + 1);   // includes self at dist 0
                float cx = 0, cy = 0; int cnt = 0; float nearest = float.MaxValue;
                foreach (var (dist, j) in near)
                {
                    if (j == i) continue;
                    if (dist < nearest) nearest = dist;
                    cx += xs[j]; cy += ys[j]; cnt++;
                }
                if (cnt == 0 || nearest < minGap)   // floor: already tight → don't pull closer
                {
                    nx[i] = xs[i]; ny[i] = ys[i];
                    continue;
                }
                cx /= cnt; cy /= cnt;
                nx[i] = xs[i] + strength * (cx - xs[i]);
                ny[i] = ys[i] + strength * (cy - ys[i]);
            }
            xs = nx; ys = ny;
        }
        for (int i = 0; i < n; i++) trees[i] = (xs[i], ys[i]);
    }

    // ── Multi-rule "unified" vegetation map ──────────────────────────────
    // Each vegetated biome is its own rule: thickness = its blended weight, placed in its own
    // colour, tightened within its own set (per-mesh). Rules ADD UP — across biomes the blend
    // keeps the total bounded (a pixel's biome shares sum to 1), so edges mingle without crowding.
    public sealed record Rule(AzgaarBiome Biome, float MaxPerSquare, byte R, byte G, byte B, string Label);

    // Drawn in this order: sparse ground cover first, dense forest painted on top.
    static readonly Rule[] Rules =
    {
        new(AzgaarBiome.Grassland,                1.5f, 150, 180,  90, "grassland"),
        new(AzgaarBiome.Savanna,                  2.0f, 181, 160,  70, "savanna bush"),
        new(AzgaarBiome.Wetland,                  3.0f,  90, 120,  80, "wetland reeds"),
        new(AzgaarBiome.TropicalSeasonalForest,   5.0f, 140, 160,  50, "tropical seasonal"),
        new(AzgaarBiome.Taiga,                    5.0f,  60,  95,  80, "taiga"),
        new(AzgaarBiome.TemperateRainforest,      6.0f,  35,  95,  85, "pine (temp. rainforest)"),
        new(AzgaarBiome.TemperateDeciduousForest, 6.0f,  40, 120,  45, "deciduous"),
        new(AzgaarBiome.TropicalRainforest,       6.0f,  15,  85,  55, "jungle (trop. rainforest)"),
    };

    public static void RenderUnified(
        IReadOnlyDictionary<int, Cell> cells, AzgaarMapCoordinates coords,
        float lonW, float lonT, float latS, float latT,
        Options o, string outPath)
    {
        int W = Map.MapWidth, H = Map.MapHeight;
        Console.WriteLine($"Building biome weight field ({W}x{H})…");
        var biomes = BiomeWeightField.Build(cells, coords);

        byte[]? heightBytes = null;
        if (o.ElevationFilter)
        {
            Console.WriteLine("Heightmap pre-pass (for elevation filter)…");
            var hp = new HeightmapAlgorithm.Params(
                LonW: lonW, LonT: lonT, LatS: latS, LatT: latT, Width: W, Height: H);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            heightBytes = HeightmapAlgorithm.Generate(cells, hp).Pixels;
            sw.Stop();
            Console.WriteLine($"Heightmap done in {sw.Elapsed.TotalSeconds:F1}s.");
        }

        // Scatter + tighten each rule independently (per-mesh), seeded per rule for determinism.
        var perRule = new List<(Rule rule, List<(float x, float y)> pts)>();
        Console.WriteLine("Per-rule tree counts:");
        foreach (var rule in Rules)
        {
            var pts = Scatter(biomes, heightBytes, rule.Biome, o.GridPx, rule.MaxPerSquare,
                              o.Seed + (int)rule.Biome, o.Treeline01);
            if (o.TightenStrength > 0f && o.TightenIters > 0)
                Tighten(pts, W, H, o.TightenStrength, o.TightenIters, k: 6, minGap: 3f);
            Console.WriteLine($"  {rule.Label,-26} {pts.Count,8}");
            perRule.Add((rule, pts));
        }

        // Render: neutral land/sea background, then each rule's dots in its own colour.
        int cw = W / o.Downscale, ch = H / o.Downscale;
        var buf = new byte[cw * ch * 3];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int sx = Math.Min(x * o.Downscale, W - 1);
                int sy = Math.Min(y * o.Downscale, H - 1);
                var bt = biomes[sy * W + sx];
                bool land = bt.W0 > 0 || bt.W1 > 0 || bt.W2 > 0;
                int idx = (y * cw + x) * 3;
                if (!land) { buf[idx] = 198; buf[idx + 1] = 214; buf[idx + 2] = 232; }   // sea
                else       { buf[idx] = 226; buf[idx + 1] = 220; buf[idx + 2] = 198; }   // tan
            }
        foreach (var (rule, pts) in perRule)
            foreach (var (tx, ty) in pts)
            {
                int x = (int)(tx / o.Downscale), y = (int)(ty / o.Downscale);
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= cw || yy >= ch) continue;
                        int idx = (yy * cw + xx) * 3;
                        buf[idx] = rule.R; buf[idx + 1] = rule.G; buf[idx + 2] = rule.B;
                    }
            }

        var settings = new MagickReadSettings { Width = cw, Height = ch, Format = MagickFormat.Rgb };
        using var img = new MagickImage(buf, settings);
        var full = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        img.Write(full, MagickFormat.Png);
        Console.WriteLine($"Wrote unified vegetation debug image: {full} ({cw}x{ch})");
    }

    // Count-per-square scatter for one biome, with optional elevation filter. Pure given its seed.
    static List<(float x, float y)> Scatter(
        BiomeWeightTriple[] biomes, byte[]? heightBytes, AzgaarBiome biome,
        int gridPx, float maxPerSquare, int seed, float treeline01)
    {
        int W = Map.MapWidth, H = Map.MapHeight;
        int gw = (W + gridPx - 1) / gridPx;
        int gh = (H + gridPx - 1) / gridPx;
        int treelineByte = (int)MathF.Round(treeline01 * 255f);
        var rng = new Random(seed);
        var pts = new List<(float x, float y)>();
        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                int cx = Math.Min(gx * gridPx + gridPx / 2, W - 1);
                int cy = Math.Min(gy * gridPx + gridPx / 2, H - 1);
                float thickness = biomes[cy * W + cx].WeightOf(biome);
                if (thickness <= 0f) continue;
                int count = (int)MathF.Round(thickness * maxPerSquare);
                for (int k = 0; k < count; k++)
                {
                    float px = gx * gridPx + (float)rng.NextDouble() * gridPx;
                    float py = gy * gridPx + (float)rng.NextDouble() * gridPx;
                    if (px >= W || py >= H) continue;
                    if (heightBytes != null)
                    {
                        byte hb = heightBytes[(int)py * W + (int)px];
                        if (hb <= HeightmapAlgorithm.MaxWaterByte) continue;   // underwater
                        if (hb > treelineByte) continue;                       // above treeline
                    }
                    pts.Add((px, py));
                }
            }
        return pts;
    }

    public static AzgaarBiome ParseRule(string? s) => s?.ToLowerInvariant() switch
    {
        null or "deciduous" or "leaf"            => AzgaarBiome.TemperateDeciduousForest,
        "jungle" or "rainforest" or "tropical"   => AzgaarBiome.TropicalRainforest,
        "tropicalseasonal" or "seasonal"         => AzgaarBiome.TropicalSeasonalForest,
        "taiga"                                  => AzgaarBiome.Taiga,
        "pine" or "temperaterainforest"          => AzgaarBiome.TemperateRainforest,
        "savanna"                                => AzgaarBiome.Savanna,
        "grassland"                              => AzgaarBiome.Grassland,
        "wetland" or "swamp"                     => AzgaarBiome.Wetland,
        _                                        => AzgaarBiome.TemperateDeciduousForest,
    };
}
