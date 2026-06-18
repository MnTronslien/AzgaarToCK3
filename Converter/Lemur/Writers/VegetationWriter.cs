using System.Globalization;
using System.Text;
using Converter.Lemur.Splats;
using Converter.Lemur.Vegetation;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

/// <summary>
/// Writes 3D foliage (trees/bushes/reeds) as CK3 map-object generators:
/// <c>gfx/map/map_object_data/generated/lemur_&lt;mesh&gt;.txt</c>. Per-pixel thickness comes from
/// the blended biome weight field (same one the splatmap uses); trees are scattered count-per-square,
/// elevation-filtered against the pipeline heightmap, and tightened per-mesh into groves.
///
/// Positions are emitted in WORLD coordinates (X east, Z north) — identical space to the town
/// locators (<see cref="LocatorWriter"/>). Image→world is a single vertical flip: Z = MapHeight − Y.
///
/// The algorithm here is the production sibling of TerrainLab's VegetationDebug harness (which renders
/// the same scatter as a debug PNG for tuning). Mirror, not shared — extract a common core if a third
/// consumer appears.
/// </summary>
public static class VegetationWriter
{
    // A rule = one ecological category: biome, density, and the CK3 mesh variants that fill it
    // (picked at random per tree for variety). Mesh names verified present in CK3 generated/*.txt.
    private sealed record Rule(AzgaarBiome Biome, float MaxPerSquare, string[] Meshes);

    private static readonly Rule[] Rules =
    {
        new(AzgaarBiome.Grassland,                1.5f, new[] { "steppe_bush_01_mesh" }),
        new(AzgaarBiome.Savanna,                  2.0f, new[] { "steppe_bush_01_mesh", "tree_palm_01_a_mesh" }),
        new(AzgaarBiome.Wetland,                  3.0f, new[] { "reeds_01_tall_grass_mesh", "reeds_06_grass_mesh", "reeds_07_grass_mesh" }),
        new(AzgaarBiome.TropicalSeasonalForest,   5.0f, new[] { "tree_palm_01_a_mesh", "tree_jungle_01_c_mesh" }),
        new(AzgaarBiome.Taiga,                    5.0f, new[] { "tree_pine_01_b_mesh", "tree_pine_single_01_a_mesh", "tree_pine_impassable_01_a_mesh" }),
        new(AzgaarBiome.TemperateRainforest,      6.0f, new[] { "tree_pine_single_01_a_mesh", "tree_pine_single_01_b_mesh", "tree_pine_single_01_c_mesh", "tree_pine_01_b_mesh" }),
        new(AzgaarBiome.TemperateDeciduousForest, 6.0f, new[] { "tree_leaf_01_a_mesh", "tree_leaf_01_b_mesh", "tree_leaf_01_c_mesh", "tree_leaf_01_single_a_mesh" }),
        new(AzgaarBiome.TropicalRainforest,       6.0f, new[] { "tree_jungle_01_c_mesh", "tree_jungle_01_d_mesh", "tree_palm_01_a_mesh" }),
    };

    // Tuning constants (kept in sync with TerrainLab's VegetationDebug harness).
    private const int   GridPx          = 16;
    // Height falloff (gradual, byte units) + large-scale density noise live in VegetationCore so the
    // writer and the TerrainLab harness share identical math.
    private const float DensityMul      = 1.20f;   // global density scale (+20% trees where vegetation exists)
    private const float TightenStrength = 0.60f;   // very strong clustering into groves + clearings
    private const int   TightenIters    = 6;
    private const float MinGap          = 1f;      // floor: trees never pulled closer than this (fights collapse)
    private const int   SeedBase        = 1337;    // deterministic; not tied to --seed (MVP)

    // grass_layer for ground cover (reeds, bushes); tree_high_layer for everything else — matches vanilla.
    private static string LayerFor(string mesh) =>
        mesh.StartsWith("reeds") || mesh.StartsWith("steppe_bush") ? "grass_layer" : "tree_high_layer";

    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing vegetation (map object generators)");

        int W = L.Map.MapWidth, H = L.Map.MapHeight;
        var biomes = BiomeWeightField.Build(map.Cells!, map.JsonMap.mapCoordinates);
        byte[]? heightBytes = map.HeightmapPixels;   // populated by HeightmapWriter (runs earlier)
        if (heightBytes == null)
            Logger.Warning("Vegetation: no heightmap — elevation filter skipped (trees may sit on water/peaks).");

        // mesh name → instance transform rows
        var byMesh = new Dictionary<string, List<string>>();

        foreach (var rule in Rules)
        {
            var pts = Scatter(biomes, heightBytes, rule.Biome, rule.MaxPerSquare,
                              SeedBase + (int)rule.Biome, W, H);
            if (pts.Count == 0) continue;
            Tighten(pts, W, H, TightenStrength, TightenIters, k: 6, minGap: MinGap);

            // Per-tree: pick a mesh variant, random yaw, slight scale jitter. Seeded per rule.
            var rng = new Random(SeedBase * 31 + (int)rule.Biome);
            foreach (var (px, py) in pts)
            {
                string mesh = rule.Meshes[rng.Next(rule.Meshes.Length)];
                double worldX = px;
                double worldZ = H - py;                       // ImagePixel → WorldPixel (vertical flip)
                double yaw = rng.NextDouble() * Math.PI * 2.0;
                double qy = Math.Sin(yaw * 0.5), qw = Math.Cos(yaw * 0.5);
                double s = 0.9 + rng.NextDouble() * 0.2;       // 0.9–1.1
                var row = string.Create(CultureInfo.InvariantCulture,
                    $"{worldX:F6} 0.000000 {worldZ:F6} 0.000000 {qy:F6} 0.000000 {qw:F6} {s:F6} {s:F6} {s:F6}");
                if (!byMesh.TryGetValue(mesh, out var list)) byMesh[mesh] = list = new List<string>();
                list.Add(row);
            }
        }

        var dir = Helper.GetPath(outputDirectory, "gfx", "map", "map_object_data", "generated");
        Directory.CreateDirectory(dir);

        int totalTrees = 0;
        foreach (var (mesh, rows) in byMesh)
        {
            var sb = new StringBuilder();
            sb.AppendLine("object={");
            sb.AppendLine($"\tname=\"lemur_{mesh}_0\"");
            sb.AppendLine("\trender_pass=Map");
            sb.AppendLine("\tclamp_to_water_level=no");
            sb.AppendLine("\tgenerated_content=yes");
            sb.AppendLine($"\tlayer=\"{LayerFor(mesh)}\"");
            sb.AppendLine($"\tpdxmesh=\"{mesh}\"");
            sb.AppendLine($"\tcount={rows.Count}");
            sb.Append("\ttransform=\"");
            sb.Append(string.Join("\n", rows));
            sb.AppendLine("\"");
            sb.AppendLine("}");

            var path = Helper.GetPath(dir, $"lemur_{mesh}.txt");
            await File.WriteAllTextAsync(path, sb.ToString(), Helper.Utf8Bom);
            totalTrees += rows.Count;
        }

        Logger.Info($"Vegetation: wrote {totalTrees} trees across {byMesh.Count} meshes to map_object_data/generated/");
    }

    // Count-per-square scatter for one biome. Density is modulated by the large-scale noise field;
    // height uses the gradual treeline (both shared via VegetationCore). Pure given its seed.
    private static List<(float x, float y)> Scatter(
        BiomeWeightTriple[] biomes, byte[]? heightBytes, AzgaarBiome biome,
        float maxPerSquare, int seed, int W, int H)
    {
        int gw = (W + GridPx - 1) / GridPx;
        int gh = (H + GridPx - 1) / GridPx;
        var rng = new Random(seed);
        var pts = new List<(float x, float y)>();
        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                int cx = Math.Min(gx * GridPx + GridPx / 2, W - 1);
                int cy = Math.Min(gy * GridPx + GridPx / 2, H - 1);
                float thickness = biomes[cy * W + cx].WeightOf(biome);
                if (thickness <= 0f) continue;
                float density = thickness * maxPerSquare * DensityMul * VegetationCore.DensityFactor(cx, cy);
                int count = (int)MathF.Round(density);
                for (int k = 0; k < count; k++)
                {
                    float px = gx * GridPx + (float)rng.NextDouble() * GridPx;
                    float py = gy * GridPx + (float)rng.NextDouble() * GridPx;
                    if (px >= W || py >= H) continue;
                    if (heightBytes != null)
                    {
                        byte hb = heightBytes[(int)py * W + (int)px];
                        if (hb <= HeightmapAlgorithm.MaxWaterByte) continue;     // underwater
                        float keep = VegetationCore.HeightKeepProb(hb);          // gradual treeline
                        if (keep <= 0f || (keep < 1f && rng.NextDouble() > keep)) continue;
                    }
                    pts.Add((px, py));
                }
            }
        return pts;
    }

    // Per-mesh reverse-relaxation: nudge each point toward the centroid of its k nearest neighbours.
    // Jacobi (snapshot → apply) so it's order-independent/deterministic; min-gap floor stops collapse.
    private static void Tighten(List<(float x, float y)> trees, int W, int H, float strength, int iters, int k, float minGap)
    {
        int n = trees.Count;
        if (n < k + 1) return;
        var xs = new float[n];
        var ys = new float[n];
        for (int i = 0; i < n; i++) { xs[i] = trees[i].x; ys[i] = trees[i].y; }

        int cols = Math.Max(1, W / 32), rows = Math.Max(1, H / 32);
        for (int it = 0; it < iters; it++)
        {
            var grid = new HeightmapSpatialGrid<int>(W, H, cols, rows);
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
                if (cnt == 0 || nearest < minGap) { nx[i] = xs[i]; ny[i] = ys[i]; continue; }
                cx /= cnt; cy /= cnt;
                nx[i] = xs[i] + strength * (cx - xs[i]);
                ny[i] = ys[i] + strength * (cy - ys[i]);
            }
            xs = nx; ys = ny;
        }
        for (int i = 0; i < n; i++) trees[i] = (xs[i], ys[i]);
    }
}
