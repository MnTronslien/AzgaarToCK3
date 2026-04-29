using Converter.Lemur;
using Converter.Lemur.Entities;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace HeightmapLab;

/// <summary>
/// Generates an 8-bit greyscale heightmap byte array from Azgaar cell data.
///
/// Two-layer approach:
///   final height = base height (Delaunay-interpolated from cells) + poly-node displacement
///
/// The base layer uses Delaunay triangulation so cell boundaries produce smooth
/// gradients instead of flat Voronoi plateaus. The poly-node layer adds
/// roughness-driven local variation on top.
/// </summary>
static class HeightmapGenerator
{
    public const int CK3WaterLevel = 20;

    public record Params(
        float LonW, float LonT,
        float LatS, float LatT,
        int Width, int Height,
        int Seed,
        float DisplacementStrength,
        float PolyNodeDensity,
        int PolyNodeSampleCount,
        float RoughnessNorm);

    public static byte[] Generate(IReadOnlyDictionary<int, Cell> cells, Params p)
    {
        // ── 1. Cell centroids + heights in pixel space ───────────────────────
        var landCells = cells.Values.Where(c => Cell.IsDryLand(c.Type)).ToList();

        if (landCells.Count == 0)
            return new byte[p.Width * p.Height];

        var roughness = Helper.ComputeRoughness(cells, p.RoughnessNorm);

        var centroids = landCells
            .Select(c => (
                px: GeoToPixelX(Centroid(c)[0], p),
                py: GeoToPixelY(Centroid(c)[1], p),
                h: c.GeoHeight,
                r: roughness.GetValueOrDefault(c.Id, 0f)))
            .ToList();

        int minH = centroids.Min(c => c.h);
        int maxH = centroids.Max(c => c.h);
        if (maxH == minH) maxH = minH + 1;

        // ── 2. Delaunay triangulation ────────────────────────────────────────
        var builder = new DelaunayTriangulationBuilder();
        builder.SetSites(new GeometryFactory().CreateMultiPointFromCoords(
            centroids.Select(c => new Coordinate(c.px, c.py)).ToArray()));
        var triangleCollection = builder.GetTriangles(new GeometryFactory());

        // Build a lookup from centroid coordinate → (height, roughness)
        var coordIndex = new Dictionary<(int, int), (int h, float r)>(centroids.Count);
        foreach (var c in centroids)
        {
            var key = (RoundCoord(c.px), RoundCoord(c.py));
            coordIndex.TryAdd(key, (c.h, c.r));
        }

        // ── 3. Rasterize base height layer ───────────────────────────────────
        var baseHeight = new float[p.Width * p.Height];
        var painted = new bool[p.Width * p.Height];

        foreach (var geom in triangleCollection.Geometries)
        {
            var ring = geom.Boundary.Coordinates;
            if (ring.Length < 3) continue;

            var v0 = ring[0]; var v1 = ring[1]; var v2 = ring[2];

            var (h0, _) = Lookup(coordIndex, v0, centroids);
            var (h1, _) = Lookup(coordIndex, v1, centroids);
            var (h2, _) = Lookup(coordIndex, v2, centroids);

            RasterizeTriangle(
                v0, v1, v2,
                h0, h1, h2,
                minH, maxH,
                baseHeight, painted,
                p.Width, p.Height);
        }

        // ── 4. Poly-node displacement layer ──────────────────────────────────
        int nodeCount = (int)(landCells.Count * p.PolyNodeDensity);
        var rng = new Random(p.Seed);

        // Spatial index for centroid nearest-lookup (to inherit roughness)
        int gridCols = Math.Max(1, (int)Math.Sqrt(centroids.Count));
        int gridRows = Math.Max(1, gridCols * p.Height / p.Width);
        var centroidGrid = new SpatialGrid<float>(p.Width, p.Height, gridCols, gridRows);
        foreach (var c in centroids)
            centroidGrid.Add(c.px, c.py, c.r);

        // Spatial index for poly-nodes (for per-pixel IDW)
        int nodeCols = Math.Max(1, (int)Math.Sqrt(nodeCount));
        int nodeRows = Math.Max(1, nodeCols * p.Height / p.Width);
        var nodeGrid = new SpatialGrid<float>(p.Width, p.Height, nodeCols, nodeRows);

        for (int i = 0; i < nodeCount; i++)
        {
            float nx = (float)(rng.NextDouble() * p.Width);
            float ny = (float)(rng.NextDouble() * p.Height);

            // Inherit roughness from nearest land cell centroid
            var nearest = centroidGrid.NearestN(nx, ny, 1);
            float localRoughness = nearest.Count > 0 ? nearest[0].item : 0f;

            float disp = (float)(rng.NextDouble() * 2.0 - 1.0) * localRoughness * p.DisplacementStrength;
            nodeGrid.Add(nx, ny, disp);
        }

        // ── 5. Combine base + displacement ───────────────────────────────────
        var result = new byte[p.Width * p.Height];
        for (int py = 0; py < p.Height; py++)
        {
            for (int px = 0; px < p.Width; px++)
            {
                int idx = py * p.Width + px;
                if (!painted[idx]) continue; // sea — stays 0

                var nearby = nodeGrid.NearestN(px, py, p.PolyNodeSampleCount);
                float disp = IdwDisplacement(nearby);

                float final = baseHeight[idx] + disp * 255f;
                result[idx] = (byte)Math.Clamp((int)Math.Round(final), 0, 255);
            }
        }

        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static float GeoToPixelX(float lon, Params p) => (lon - p.LonW) / p.LonT * p.Width;
    static float GeoToPixelY(float lat, Params p) => p.Height - (lat - p.LatS) / p.LatT * p.Height;

    static float[] Centroid(Cell cell)
    {
        var coords = cell.GeoDataCoordinates;
        // GeoJSON ring: last point == first, so exclude it
        int n = coords.Length - 1;
        if (n <= 0) return coords[0];
        float lon = 0, lat = 0;
        for (int i = 0; i < n; i++) { lon += coords[i][0]; lat += coords[i][1]; }
        return [lon / n, lat / n];
    }

    static int RoundCoord(float v) => (int)Math.Round(v);

    static (int h, float r) Lookup(
        Dictionary<(int, int), (int h, float r)> index,
        Coordinate v,
        List<(float px, float py, int h, float r)> centroids)
    {
        var key = (RoundCoord((float)v.X), RoundCoord((float)v.Y));
        if (index.TryGetValue(key, out var val)) return val;
        // Fallback: find nearest centroid (handles floating-point rounding edge cases)
        var best = centroids.MinBy(c =>
        {
            float dx = c.px - (float)v.X, dy = c.py - (float)v.Y;
            return dx * dx + dy * dy;
        });
        return (best.h, best.r);
    }

    static void RasterizeTriangle(
        Coordinate v0, Coordinate v1, Coordinate v2,
        int h0, int h1, int h2,
        int minH, int maxH,
        float[] heightMap, bool[] painted,
        int width, int height)
    {
        // Bounding box
        int xMin = Math.Max(0, (int)Math.Min(v0.X, Math.Min(v1.X, v2.X)));
        int xMax = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(v0.X, Math.Max(v1.X, v2.X))));
        int yMin = Math.Max(0, (int)Math.Min(v0.Y, Math.Min(v1.Y, v2.Y)));
        int yMax = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(v0.Y, Math.Max(v1.Y, v2.Y))));

        float denom = (float)((v1.Y - v2.Y) * (v0.X - v2.X) + (v2.X - v1.X) * (v0.Y - v2.Y));
        if (MathF.Abs(denom) < 1e-6f) return; // degenerate triangle

        for (int py = yMin; py <= yMax; py++)
        {
            for (int px = xMin; px <= xMax; px++)
            {
                float w0 = (float)((v1.Y - v2.Y) * (px - v2.X) + (v2.X - v1.X) * (py - v2.Y)) / denom;
                float w1 = (float)((v2.Y - v0.Y) * (px - v2.X) + (v0.X - v2.X) * (py - v2.Y)) / denom;
                float w2 = 1f - w0 - w1;

                if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f) continue;

                float interpH = w0 * h0 + w1 * h1 + w2 * h2;
                // Scale [minH, maxH] → [CK3WaterLevel, 255]
                float scaled = (interpH - minH) / (maxH - minH) * (255f - CK3WaterLevel) + CK3WaterLevel;

                int idx = py * width + px;
                heightMap[idx] = Math.Clamp(scaled, CK3WaterLevel, 255f);
                painted[idx] = true;
            }
        }
    }

    static float IdwDisplacement(List<(float dist, float item)> nearest)
    {
        if (nearest.Count == 0) return 0f;
        float weightSum = 0f, valueSum = 0f;
        foreach (var (dist, disp) in nearest)
        {
            float w = dist < 0.001f ? 1e6f : 1f / (dist * dist);
            weightSum += w;
            valueSum += w * disp;
        }
        return weightSum > 0f ? valueSum / weightSum : 0f;
    }
}
