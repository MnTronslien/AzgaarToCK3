using System.Linq;
using Converter.Lemur.Deserialization;
using Converter.Lemur.Entities;
using Converter.Lemur.Splats;
using ImageMagick;

namespace TerrainLab;

// MVP vegetation debug renderer (feature/vegetation-mvp).
//
// Single-rule thickness: thickness at a pixel = one Azgaar biome's *blended* weight
// (BiomeWeightField — the splatmap interpolation, so it fades 1→0 across biome edges).
// Placement: count-per-coarse-square = round(thickness * maxPerSquare), scattered at random
// inside the square (seeded). No tightening yet — this is the first light, to be looked at.
//
// Renders tree dots over a thickness-tinted biome background, downscaled for emailability.
// Stays entirely in IMAGE space (origin top-left, Y down) — NO WorldPixel flip. This is the
// tuning harness; the flip only matters when emitting the real CK3 map_object_data file.
static class VegetationDebug
{
    public static void Render(
        IReadOnlyDictionary<int, Cell> cells,
        AzgaarMapCoordinates coords,
        AzgaarBiome rule,
        int gridPx,
        float maxPerSquare,
        int seed,
        int downscale,
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

        // ── Scatter: count per coarse square from thickness sampled at the square centre ──
        int gw = (W + gridPx - 1) / gridPx;
        int gh = (H + gridPx - 1) / gridPx;
        var rng = new Random(seed);
        var trees = new List<(float x, float y)>();
        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                int cx = Math.Min(gx * gridPx + gridPx / 2, W - 1);
                int cy = Math.Min(gy * gridPx + gridPx / 2, H - 1);
                float thickness = biomes[cy * W + cx].WeightOf(rule);   // 0..1, blended
                if (thickness <= 0f) continue;
                int count = (int)MathF.Round(thickness * maxPerSquare);
                for (int k = 0; k < count; k++)
                {
                    float px = gx * gridPx + (float)rng.NextDouble() * gridPx;
                    float py = gy * gridPx + (float)rng.NextDouble() * gridPx;
                    if (px >= W || py >= H) continue;
                    trees.Add((px, py));
                }
            }
        Console.WriteLine($"Placed {trees.Count} {rule} trees (grid={gridPx}px, max/sq={maxPerSquare}, seed={seed}).");

        // ── Render: thickness-tinted background + tree dots, downscaled ──
        int cw = W / downscale, ch = H / downscale;
        var buf = new byte[cw * ch * 3];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int sx = Math.Min(x * downscale, W - 1);
                int sy = Math.Min(y * downscale, H - 1);
                var bt = biomes[sy * W + sx];
                bool land = bt.W0 > 0 || bt.W1 > 0 || bt.W2 > 0;
                float t = bt.WeightOf(rule);
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
            int x = (int)(tx / downscale), y = (int)(ty / downscale);
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
