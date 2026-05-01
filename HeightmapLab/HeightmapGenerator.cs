using Converter.Lemur;
using Converter.Lemur.Entities;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

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

        // Land cells → [CK3WaterLevel, 255]; sea cells → 0
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

        // ── 2. Terrain spatial grid (used only during poly-node spawning) ─────
        int terrainCols = Math.Max(1, (int)Math.Sqrt(allCentroids.Count));
        int terrainRows = Math.Max(1, terrainCols * p.Height / p.Width);
        var terrainGrid = new SpatialGrid<float>(p.Width, p.Height, terrainCols, terrainRows);
        foreach (var c in allCentroids)
            terrainGrid.Add(c.px, c.py, c.h);

        // ── 3. Early-out: terrain-only Delaunay rasterization ────────────────
        if (p.BaseOnly)
        {
            var coordIndex = BuildCoordIndex(allCentroids.Select(c => (c.px, c.py, c.h)));
            var heightMap = Rasterize(coordIndex, allCentroids.Select(c => new Coordinate(c.px, c.py)), p);
            return new GenerateResult(ToBytes(heightMap), centroidPositions, []);
        }

        // ── 4. Poly-node spawning ─────────────────────────────────────────────
        var rng = new Random(p.Seed);
        float scaleX = p.Width / p.LonT;
        float scaleY = p.Height / p.LatT;

        var polyNodePositions = new List<(float px, float py)>(landCells.Count * p.NodesPerCell);
        var polyNodeHeights   = new List<float>(landCells.Count * p.NodesPerCell);

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

                // Base height from 3 nearest terrain nodes
                var nearTerrain = terrainGrid.NearestN(nx, ny, 3);
                float baseH = IdwWeightedAverage(nearTerrain);
                if (baseH < CK3WaterLevel) continue; // landed in ocean territory

                float perturbation = (float)(rng.NextDouble() * 2.0 - 1.0)
                    * c.r * p.DisplacementStrength * (255f - CK3WaterLevel);
                float nodeHeight = Math.Clamp(baseH + perturbation, CK3WaterLevel, 255f);

                polyNodePositions.Add((nx, ny));
                polyNodeHeights.Add(nodeHeight);
            }
        }

        // ── 5. Delaunay triangulation + rasterization ─────────────────────────
        // Combined coordinate → height lookup (terrain centroids + poly-nodes)
        var allPoints = allCentroids.Select(c => (c.px, c.py, c.h))
            .Concat(polyNodePositions.Select((pos, i) => (pos.px, pos.py, polyNodeHeights[i])));

        var combinedCoordIndex = BuildCoordIndex(allPoints);

        var allCoords = allCentroids.Select(c => new Coordinate(c.px, c.py))
            .Concat(polyNodePositions.Select(pos => new Coordinate(pos.px, pos.py)));

        var heightMap2 = Rasterize(combinedCoordIndex, allCoords, p);
        return new GenerateResult(ToBytes(heightMap2), centroidPositions, polyNodePositions);
    }

    // ── Core rasterization ────────────────────────────────────────────────────

    static float[] Rasterize(
        Dictionary<(int, int), float> coordIndex,
        IEnumerable<Coordinate> points,
        Params p)
    {
        var gf = new GeometryFactory();
        var builder = new DelaunayTriangulationBuilder();
        builder.SetSites(gf.CreateMultiPointFromCoords(points.ToArray()));
        var triangles = builder.GetTriangles(gf);

        var heightMap = new float[p.Width * p.Height];

        foreach (var geom in triangles.Geometries)
        {
            var ring = geom.Boundary.Coordinates;
            if (ring.Length < 3) continue;

            var v0 = ring[0]; var v1 = ring[1]; var v2 = ring[2];
            float h0 = Lookup(coordIndex, v0);
            float h1 = Lookup(coordIndex, v1);
            float h2 = Lookup(coordIndex, v2);

            RasterizeTriangle(v0, v1, v2, h0, h1, h2, heightMap, p.Width, p.Height);
        }

        return heightMap;
    }

    static void RasterizeTriangle(
        Coordinate v0, Coordinate v1, Coordinate v2,
        float h0, float h1, float h2,
        float[] heightMap, int width, int height)
    {
        int xMin = Math.Max(0, (int)Math.Min(v0.X, Math.Min(v1.X, v2.X)));
        int xMax = Math.Min(width  - 1, (int)Math.Ceiling(Math.Max(v0.X, Math.Max(v1.X, v2.X))));
        int yMin = Math.Max(0, (int)Math.Min(v0.Y, Math.Min(v1.Y, v2.Y)));
        int yMax = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(v0.Y, Math.Max(v1.Y, v2.Y))));

        float denom = (float)((v1.Y - v2.Y) * (v0.X - v2.X) + (v2.X - v1.X) * (v0.Y - v2.Y));
        if (MathF.Abs(denom) < 1e-6f) return;

        for (int py = yMin; py <= yMax; py++)
            for (int px = xMin; px <= xMax; px++)
            {
                float w0 = (float)((v1.Y - v2.Y) * (px - v2.X) + (v2.X - v1.X) * (py - v2.Y)) / denom;
                float w1 = (float)((v2.Y - v0.Y) * (px - v2.X) + (v0.X - v2.X) * (py - v2.Y)) / denom;
                float w2 = 1f - w0 - w1;

                if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f) continue;

                heightMap[py * width + px] = Math.Clamp(w0 * h0 + w1 * h1 + w2 * h2, 0f, 255f);
            }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static Dictionary<(int, int), float> BuildCoordIndex(IEnumerable<(float px, float py, float h)> points)
    {
        var index = new Dictionary<(int, int), float>();
        foreach (var (px, py, h) in points)
            index.TryAdd((RoundCoord(px), RoundCoord(py)), h);
        return index;
    }

    static float Lookup(Dictionary<(int, int), float> index, Coordinate v)
    {
        index.TryGetValue((RoundCoord((float)v.X), RoundCoord((float)v.Y)), out float h);
        return h;
    }

    static byte[] ToBytes(float[] heightMap)
    {
        var bytes = new byte[heightMap.Length];
        for (int i = 0; i < heightMap.Length; i++)
            bytes[i] = (byte)Math.Clamp((int)Math.Round(heightMap[i]), 0, 255);
        return bytes;
    }

    static int RoundCoord(float v) => (int)Math.Round(v);

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
