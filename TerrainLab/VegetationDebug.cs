using System.Linq;
using Converter.Lemur.Deserialization;
using Converter.Lemur.Entities;
using Converter.Lemur.Splats;
using Converter.Lemur.Vegetation;
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
    private const float DensityMul = 1.20f;   // global density scale (kept in sync with VegetationWriter)

    public readonly record struct Options(
        AzgaarBiome Rule,
        int GridPx,
        float MaxPerSquare,
        int Seed,
        int Downscale,
        bool ElevationFilter,
        int TreelineByte,        // reject land trees with heightmap byte above this (waterline ≈ 20)
        float TightenStrength,   // 0 = off
        int TightenIters,
        bool NoiseOverlay = false);   // debug: paint the noise field as the background

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
        int treelineByte = o.TreelineByte;

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
                float density = thickness * o.MaxPerSquare * DensityMul * VegetationCore.DensityFactor(cx, cy);
                int count = (int)MathF.Round(density);
                for (int k = 0; k < count; k++)
                {
                    float px = gx * o.GridPx + (float)rng.NextDouble() * o.GridPx;
                    float py = gy * o.GridPx + (float)rng.NextDouble() * o.GridPx;
                    if (px >= W || py >= H) continue;

                    if (heightBytes != null)
                    {
                        byte hb = heightBytes[(int)py * W + (int)px];
                        if (hb <= HeightmapAlgorithm.MaxWaterByte) { rejUnderwater++; continue; }
                        float keep = VegetationCore.HeightKeepProb(hb);
                        if (keep <= 0f || (keep < 1f && rng.NextDouble() > keep)) { rejTreeline++; continue; }
                        heights.Add(hb / 255f);
                    }
                    trees.Add((px, py));
                }
            }
        Console.WriteLine($"Placed {trees.Count} {o.Rule} trees (grid={o.GridPx}px, max/sq={o.MaxPerSquare}, seed={o.Seed}).");
        if (o.ElevationFilter)
        {
            Console.WriteLine($"Elevation filter: rejected {rejUnderwater} underwater, {rejTreeline} above treeline (heightByte > {o.TreelineByte}).");
            if (heights.Count > 0)
            {
                heights.Sort();
                Console.WriteLine($"  kept-tree height01: min={heights[0]:F3} median={heights[heights.Count/2]:F3} max={heights[^1]:F3}");
            }
        }

        // ── Slight tightening (per-mesh; here all trees are one mesh) ──
        if (o.TightenStrength > 0f && o.TightenIters > 0)
        {
            Tighten(trees, W, H, o.TightenStrength, o.TightenIters, k: 6, minGap: 1f);
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
    // A rule = one ecological category: a biome, a density, a debug colour, and the set of CK3
    // meshes that fill it (variants — picked at random per tree for visual variety).
    public sealed record Rule(AzgaarBiome Biome, float MaxPerSquare, byte R, byte G, byte B, string Label, string[] Meshes);

    // Drawn in this order: sparse ground cover first, dense forest painted on top.
    // Mesh names verified present in CK3 generated/*.txt (the 22-mesh foliage inventory).
    static readonly Rule[] Rules =
    {
        new(AzgaarBiome.Grassland,                1.5f, 150, 180,  90, "grassland",
            new[] { "steppe_bush_01_mesh" }),
        new(AzgaarBiome.Savanna,                  2.0f, 181, 160,  70, "savanna bush",
            new[] { "steppe_bush_01_mesh", "tree_palm_01_a_mesh" }),
        new(AzgaarBiome.Wetland,                  3.0f,  90, 120,  80, "wetland reeds",
            new[] { "reeds_01_tall_grass_mesh", "reeds_06_grass_mesh", "reeds_07_grass_mesh" }),
        new(AzgaarBiome.TropicalSeasonalForest,   5.0f, 140, 160,  50, "tropical seasonal",
            new[] { "tree_palm_01_a_mesh", "tree_jungle_01_c_mesh" }),
        new(AzgaarBiome.Taiga,                    5.0f,  60,  95,  80, "taiga",
            new[] { "tree_pine_01_b_mesh", "tree_pine_single_01_a_mesh", "tree_pine_impassable_01_a_mesh" }),
        new(AzgaarBiome.TemperateRainforest,      6.0f,  35,  95,  85, "pine (temp. rainforest)",
            new[] { "tree_pine_single_01_a_mesh", "tree_pine_single_01_b_mesh", "tree_pine_single_01_c_mesh", "tree_pine_01_b_mesh" }),
        new(AzgaarBiome.TemperateDeciduousForest, 6.0f,  40, 120,  45, "deciduous",
            new[] { "tree_leaf_01_a_mesh", "tree_leaf_01_b_mesh", "tree_leaf_01_c_mesh", "tree_leaf_01_single_a_mesh" }),
        new(AzgaarBiome.TropicalRainforest,       6.0f,  15,  85,  55, "jungle (trop. rainforest)",
            new[] { "tree_jungle_01_c_mesh", "tree_jungle_01_d_mesh", "tree_palm_01_a_mesh" }),
    };

    // CK3 foliage meshes that exist but map to no Azgaar biome (flavour / no matching biome).
    static readonly (string Label, string[] Meshes)[] Unassigned =
    {
        ("mediterranean / dry — no Azgaar biome", new[] { "tree_cypress_01_a_mesh", "tree_cypress_01_b_mesh", "tree_cypress_01_c_mesh" }),
        ("East-Asia flavour — not biome-driven",  new[] { "tree_sakura_01_mesh", "tree_sakura_02_mesh", "tree_sakura_03_mesh" }),
    };

    // Diagram: each rule and the meshes it draws from, grouped by rule. No Azgaar data needed.
    public static void RenderMeshGraph(string outPath)
    {
        const int Wd = 1500, Ht = 1000;
        const string font = "C:\\Windows\\Fonts\\arial.ttf";
        static string Shorten(string m) => (m.StartsWith("tree_") ? m[5..] : m).Replace("_mesh", "");

        using var img = new MagickImage("xc:white", new MagickReadSettings { Width = Wd, Height = Ht });
        var d = new Drawables();
        d.Font(font);
        d.StrokeColor(MagickColors.None).FillColor(MagickColors.Black).FontPointSize(22)
         .Text(30, 42, "Vegetation rules → CK3 meshes (variants), grouped by rule");
        d.FontPointSize(12).FillColor(new MagickColor("#555555"))
         .Text(30, 66, "8 biome rules · 22 distinct foliage meshes available · each rule fills its thickness budget by random pick among its variants");

        const int ruleX1 = 30, ruleX2 = 340, rowH = 74, pillW = 176, pillGap = 12, pillH = 56;
        int rowY = 100;
        foreach (var rule in Rules)
        {
            string hex = $"#{rule.R:X2}{rule.G:X2}{rule.B:X2}";
            double lum = 0.299 * rule.R + 0.587 * rule.G + 0.114 * rule.B;
            var txt = lum < 140 ? MagickColors.White : MagickColors.Black;

            d.StrokeColor(MagickColors.Black).StrokeWidth(1).FillColor(new MagickColor(hex))
             .Rectangle(ruleX1, rowY, ruleX2, rowY + pillH);
            d.StrokeColor(MagickColors.None).FillColor(txt).FontPointSize(15)
             .Text(ruleX1 + 10, rowY + 24, rule.Label);
            d.FontPointSize(11)
             .Text(ruleX1 + 10, rowY + 44, $"max/sq {rule.MaxPerSquare}  ·  {rule.Meshes.Length} variant(s)");

            double cy = rowY + pillH / 2.0;
            int firstX = ruleX2 + 40;
            int lastRight = firstX + rule.Meshes.Length * (pillW + pillGap) - pillGap;
            // connector spine first, so pills paint over it (no strikethrough through pills)
            d.StrokeColor(new MagickColor("#aaaaaa")).StrokeWidth(1).FillColor(MagickColors.None)
             .Line(ruleX2, cy, lastRight, cy);
            int mx = firstX;
            foreach (var m in rule.Meshes)
            {
                d.StrokeColor(MagickColors.Black).FillColor(new MagickColor("#eef3e8"))
                 .RoundRectangle(mx, rowY + 6, mx + pillW, rowY + pillH - 6, 6, 6);
                d.StrokeColor(MagickColors.None).FillColor(MagickColors.Black).FontPointSize(12)
                 .Text(mx + 9, rowY + pillH / 2.0 + 4, Shorten(m));
                mx += pillW + pillGap;
            }
            rowY += rowH;
        }

        rowY += 8;
        d.StrokeColor(MagickColors.None).FillColor(new MagickColor("#883333")).FontPointSize(15)
         .Text(ruleX1, rowY, "Available, not yet assigned to a rule:");
        rowY += 22;
        foreach (var (label, meshes) in Unassigned)
        {
            d.FillColor(new MagickColor("#555555")).FontPointSize(12).Text(ruleX1, rowY + 18, label);
            int mx = ruleX2 + 40;
            foreach (var m in meshes)
            {
                d.StrokeColor(MagickColors.Black).FillColor(new MagickColor("#f3eee4"))
                 .RoundRectangle(mx, rowY, mx + pillW, rowY + 28, 6, 6);
                d.StrokeColor(MagickColors.None).FillColor(MagickColors.Black).FontPointSize(12)
                 .Text(mx + 9, rowY + 19, Shorten(m));
                mx += pillW + pillGap;
            }
            rowY += 40;
        }

        img.Draw(d);
        var full = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        img.Write(full, MagickFormat.Png);
        Console.WriteLine($"Wrote mesh graph: {full} ({Wd}x{Ht})");
    }

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
                              o.Seed + (int)rule.Biome, o.TreelineByte);
            if (o.TightenStrength > 0f && o.TightenIters > 0)
                Tighten(pts, W, H, o.TightenStrength, o.TightenIters, k: 6, minGap: 1f);
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
                else if (o.NoiseOverlay)
                {
                    // heatmap of the noise field: low = dark purple, high = bright yellow
                    float n = VegetationCore.NoiseRaw(sx, sy);
                    buf[idx]     = (byte)(40 + 200 * n);
                    buf[idx + 1] = (byte)(30 + 200 * n);
                    buf[idx + 2] = (byte)(70 + 30 * n);
                }
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

    // Count-per-square scatter for one biome. Density modulated by VegetationCore's large-scale noise;
    // height uses VegetationCore's gradual treeline. Pure given its seed. (treelineByte param unused —
    // the gradual band lives in VegetationCore.) Mirrors VegetationWriter.Scatter.
    static List<(float x, float y)> Scatter(
        BiomeWeightTriple[] biomes, byte[]? heightBytes, AzgaarBiome biome,
        int gridPx, float maxPerSquare, int seed, int treelineByte)
    {
        int W = Map.MapWidth, H = Map.MapHeight;
        int gw = (W + gridPx - 1) / gridPx;
        int gh = (H + gridPx - 1) / gridPx;
        var rng = new Random(seed);
        var pts = new List<(float x, float y)>();
        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                int cx = Math.Min(gx * gridPx + gridPx / 2, W - 1);
                int cy = Math.Min(gy * gridPx + gridPx / 2, H - 1);
                float thickness = biomes[cy * W + cx].WeightOf(biome);
                if (thickness <= 0f) continue;
                float density = thickness * maxPerSquare * DensityMul * VegetationCore.DensityFactor(cx, cy);
                int count = (int)MathF.Round(density);
                for (int k = 0; k < count; k++)
                {
                    float px = gx * gridPx + (float)rng.NextDouble() * gridPx;
                    float py = gy * gridPx + (float)rng.NextDouble() * gridPx;
                    if (px >= W || py >= H) continue;
                    if (heightBytes != null)
                    {
                        byte hb = heightBytes[(int)py * W + (int)px];
                        if (hb <= HeightmapAlgorithm.MaxWaterByte) continue;   // underwater
                        float keep = VegetationCore.HeightKeepProb(hb);
                        if (keep <= 0f || (keep < 1f && rng.NextDouble() > keep)) continue;
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
