using Converter.Lemur;
using Converter.Lemur.Entities;

namespace HeightmapLab;

static class HeightmapGenerator
{
    public const int CK3WaterLevel = 20;

    public record Params(
        float LonW, float LonT,
        float LatS, float LatT,
        int Width, int Height,
        int Seed,
        float DisplacementStrength,
        int NodesPerCell,
        int PolyNodeSampleCount,
        float RoughnessNorm,
        bool BaseOnly = false);

    public record GenerateResult(
        byte[] Pixels,
        IReadOnlyList<(float px, float py)> Centroids,
        IReadOnlyList<(float px, float py)> PolyNodes);

    public static GenerateResult Generate(IReadOnlyDictionary<int, Cell> cells, Params p)
    {
        // ── 1. Terrain nodes in pixel space ──────────────────────────────────
        var landCells = cells.Values.Where(c => Cell.IsDryLand(c.Type)).ToList();
        if (landCells.Count == 0)
            return new GenerateResult(new byte[p.Width * p.Height], [], []);

        var roughness = Helper.ComputeRoughness(cells, p.RoughnessNorm);

        int minH = landCells.Min(c => c.GeoHeight);
        int maxH = landCells.Max(c => c.GeoHeight);
        if (maxH == minH) maxH = minH + 1;

        // Land cells → scaled to [CK3WaterLevel, 255]; sea cells → 0
        var allCentroids = cells.Values.Select(c =>
        {
            bool isLand = Cell.IsDryLand(c.Type);
            float h = isLand
                ? (c.GeoHeight - minH) / (float)(maxH - minH) * (255f - CK3WaterLevel) + CK3WaterLevel
                : 0f;
            return (
                px: GeoToPixelX(Centroid(c)[0], p),
                py: GeoToPixelY(Centroid(c)[1], p),
                h,
                r: roughness.GetValueOrDefault(c.Id, 0f),
                isLand,
                area: c.Area);
        }).ToList();

        var centroidPositions = allCentroids.Select(c => (c.px, c.py)).ToList();

        // ── 2. Terrain spatial grid ───────────────────────────────────────────
        int terrainCols = Math.Max(1, (int)Math.Sqrt(allCentroids.Count));
        int terrainRows = Math.Max(1, terrainCols * p.Height / p.Width);
        var terrainGrid = new SpatialGrid<float>(p.Width, p.Height, terrainCols, terrainRows);
        foreach (var c in allCentroids)
            terrainGrid.Add(c.px, c.py, c.h);

        // ── 3. Early-out: terrain-only IDW ────────────────────────────────────
        if (p.BaseOnly)
        {
            var (baseMask, baseMaskW) = BuildLandMask(terrainGrid, p.Width, p.Height);
            var baseOnly = new byte[p.Width * p.Height];
            for (int py = 0; py < p.Height; py++)
                for (int px = 0; px < p.Width; px++)
                {
                    if (!baseMask[(py / LandMaskScale) * baseMaskW + (px / LandMaskScale)]) continue;
                    var near = terrainGrid.NearestN(px, py, p.PolyNodeSampleCount);
                    float val = near.Count > 0 ? IdwWeightedAverage(near) : 0f;
                    baseOnly[py * p.Width + px] = (byte)Math.Clamp((int)Math.Round(val), 0, 255);
                }
            return new GenerateResult(baseOnly, centroidPositions, []);
        }

        // ── 4. Poly-node spawning ─────────────────────────────────────────────
        var rng = new Random(p.Seed);
        int estimatedNodeCount = landCells.Count * p.NodesPerCell;
        float scaleX = p.Width / p.LonT;
        float scaleY = p.Height / p.LatT;

        // Combined grid: terrain nodes + poly-nodes
        int combinedCols = Math.Max(1, (int)Math.Sqrt(allCentroids.Count + estimatedNodeCount));
        int combinedRows = Math.Max(1, combinedCols * p.Height / p.Width);
        var combinedGrid = new SpatialGrid<float>(p.Width, p.Height, combinedCols, combinedRows);
        foreach (var c in allCentroids)
            combinedGrid.Add(c.px, c.py, c.h);

        var polyNodePositions = new List<(float px, float py)>(estimatedNodeCount);

        foreach (var c in allCentroids.Where(c => c.isLand))
        {
            float pixelArea = c.area * scaleX * scaleY;
            float radius = MathF.Sqrt(pixelArea / MathF.PI);

            for (int i = 0; i < p.NodesPerCell; i++)
            {
                float angle = (float)(rng.NextDouble() * 2.0 * Math.PI);
                float r = radius * MathF.Sqrt((float)rng.NextDouble());
                float nx = Math.Clamp(c.px + r * MathF.Cos(angle), 0, p.Width - 1);
                float ny = Math.Clamp(c.py + r * MathF.Sin(angle), 0, p.Height - 1);

                // Base height from 3 nearest terrain nodes via IDW
                var nearTerrain = terrainGrid.NearestN(nx, ny, 3);
                float baseH = IdwWeightedAverage(nearTerrain);

                // Skip nodes that landed in ocean territory
                if (baseH < CK3WaterLevel) continue;

                float perturbation = (float)(rng.NextDouble() * 2.0 - 1.0)
                    * c.r * p.DisplacementStrength * (255f - CK3WaterLevel);
                float nodeHeight = Math.Clamp(baseH + perturbation, CK3WaterLevel, 255f);

                combinedGrid.Add(nx, ny, nodeHeight);
                polyNodePositions.Add((nx, ny));
            }
        }

        // ── 5. Rasterize: each pixel samples combined grid via IDW ────────────
        var (landMask, maskW) = BuildLandMask(terrainGrid, p.Width, p.Height);
        var result = new byte[p.Width * p.Height];
        for (int py = 0; py < p.Height; py++)
            for (int px = 0; px < p.Width; px++)
            {
                if (!landMask[(py / LandMaskScale) * maskW + (px / LandMaskScale)]) continue;
                var nearby = combinedGrid.NearestN(px, py, p.PolyNodeSampleCount);
                float val = nearby.Count > 0 ? IdwWeightedAverage(nearby) : 0f;
                result[py * p.Width + px] = (byte)Math.Clamp((int)Math.Round(val), 0, 255);
            }

        return new GenerateResult(result, centroidPositions, polyNodePositions);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    const int LandMaskScale = 16;

    // Coarse Voronoi sea/land mask at 1/LandMaskScale resolution.
    // Each coarse cell checks the nearest terrain centroid; sea centroids have height 0.
    static (bool[] mask, int maskW) BuildLandMask(SpatialGrid<float> terrainGrid, int width, int height)
    {
        int maskW = (width  + LandMaskScale - 1) / LandMaskScale;
        int maskH = (height + LandMaskScale - 1) / LandMaskScale;
        var mask = new bool[maskW * maskH];
        for (int my = 0; my < maskH; my++)
            for (int mx = 0; mx < maskW; mx++)
            {
                var near1 = terrainGrid.NearestN(
                    mx * LandMaskScale + LandMaskScale / 2f,
                    my * LandMaskScale + LandMaskScale / 2f, 1);
                mask[my * maskW + mx] = near1.Count > 0 && near1[0].item >= 1f;
            }
        return (mask, maskW);
    }

    static float GeoToPixelX(float lon, Params p) => (lon - p.LonW) / p.LonT * p.Width;
    static float GeoToPixelY(float lat, Params p) => p.Height - (lat - p.LatS) / p.LatT * p.Height;

    static float[] Centroid(Cell cell)
    {
        var coords = cell.GeoDataCoordinates;
        int n = coords.Length - 1;
        if (n <= 0) return coords[0];
        float lon = 0, lat = 0;
        for (int i = 0; i < n; i++) { lon += coords[i][0]; lat += coords[i][1]; }
        return [lon / n, lat / n];
    }

    static float IdwWeightedAverage(List<(float dist, float item)> nearest)
    {
        if (nearest.Count == 0) return 0f;
        float weightSum = 0f, valueSum = 0f;
        foreach (var (dist, val) in nearest)
        {
            float w = dist < 0.001f ? 1e6f : 1f / (dist * dist);
            weightSum += w;
            valueSum += w * val;
        }
        return weightSum > 0f ? valueSum / weightSum : 0f;
    }
}
