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
        float RoughnessNorm = 1f,
        int RelaxIterations = 5,
        float TerrainToPolySep = 0.25f,
        float RelaxStep = 0.05f,
        int BlurRadius = 3,
        float RoughnessPower = 2.0f,
        bool BaseOnly = false);

    public record GenerateResult(
        byte[] Pixels,
        float[] HeightmapF,
        IReadOnlyList<TerrainNode> TerrainNodes,
        IReadOnlyList<PolyNode> PolyNodes,
        IReadOnlyList<CoastNode> CoastNodes);

    // Internal to this file — carries spawn position + parent context through relaxation
    private record struct SpawnedNode(float Px, float Py, float SpawnPx, float SpawnPy, int ParentIdx);

    public static GenerateResult Generate(IReadOnlyDictionary<int, Cell> cells, Params p)
    {
        // ── 1. Build terrain nodes ────────────────────────────────────────────
        var landCells = cells.Values.Where(c => Cell.IsDryLand(c.Type)).ToList();
        if (landCells.Count == 0)
            return new GenerateResult(new byte[p.Width * p.Height], new float[p.Width * p.Height], [], [], []);

        // Compute raw avg-height-diff per cell (no clamping), find p95, use that
        // as the normalization ceiling. RoughnessNorm > 1 pushes p95 below 1.0,
        // further suppressing the overall displacement contribution.
        var rawDiffs = new Dictionary<int, float>(cells.Count);
        foreach (var cell in cells.Values)
        {
            if (!Cell.IsDryLand(cell.Type)) continue;
            var nh = cell.Neighbors
                .Select(id => cells.TryGetValue(id, out var n) ? n : null)
                .Where(n => n != null && Cell.IsDryLand(n!.Type))
                .Select(n => n!.GeoHeight).ToList();
            rawDiffs[cell.Id] = nh.Count > 0
                ? (float)nh.Average(h => Math.Abs(cell.GeoHeight - h))
                : 0f;
        }
        var sortedDiffs = rawDiffs.Values.OrderBy(x => x).ToList();
        float p95val  = sortedDiffs.Count > 0 ? sortedDiffs[(int)(sortedDiffs.Count * 0.95f)] : 1f;
        float autoNorm = (p95val > 0f ? p95val : 1f) * p.RoughnessNorm;
        var roughness = rawDiffs.ToDictionary(kv => kv.Key, kv => Math.Clamp(kv.Value / autoNorm, 0f, 1f));
        var rv = roughness.Values;
        Console.WriteLine($"Roughness (p95raw={p95val:F1} autoNorm={autoNorm:F1}) — min:{rv.Min():F3} avg:{rv.Average():F3} p50:{rv.OrderBy(x=>x).ElementAt(rv.Count/2):F3} p75:{rv.OrderBy(x=>x).ElementAt(rv.Count*3/4):F3} max:{rv.Max():F3}");

        int minH = landCells.Min(c => c.GeoHeight);
        int maxH = landCells.Max(c => c.GeoHeight);
        if (maxH == minH) maxH = minH + 1;

        var terrainNodes = cells.Values.Select(c =>
        {
            bool isLand = Cell.IsDryLand(c.Type);
            float h = isLand
                ? (c.GeoHeight - minH) / (float)(maxH - minH) * (255f - CK3WaterLevel) + CK3WaterLevel
                : 0f;
            return new TerrainNode(
                Id:       c.Id,
                Px:       GeoToPixelX(Centroid(c)[0], p),
                Py:       GeoToPixelY(Centroid(c)[1], p),
                Height:   h,
                Roughness: roughness.GetValueOrDefault(c.Id, 0f),
                IsLand:   isLand,
                Area:     c.Area);
        }).ToList();

        // ── 2. Terrain spatial grid — stores list index, not value ────────────
        int terrainCols = Math.Max(1, (int)Math.Sqrt(terrainNodes.Count));
        int terrainRows = Math.Max(1, terrainCols * p.Height / p.Width);
        var terrainGrid = new SpatialGrid<int>(p.Width, p.Height, terrainCols, terrainRows);
        for (int i = 0; i < terrainNodes.Count; i++)
            terrainGrid.Add(terrainNodes[i].Px, terrainNodes[i].Py, i);

        // ── 3. Coast constraint nodes — polygon vertices shared between land and sea ──
        // Each such vertex is pinned to CK3WaterLevel so the Delaunay waterline
        // crosses at the actual cell boundary, not somewhere inland.
        var landVerts = new HashSet<(float, float)>();
        var seaVerts  = new HashSet<(float, float)>();
        foreach (var cell in cells.Values)
        {
            var coords = cell.GeoDataCoordinates;
            if (coords == null) continue;
            int n = coords.Length - 1; // last vertex == first, skip it
            var bucket = Cell.IsDryLand(cell.Type) ? landVerts : seaVerts;
            for (int vi = 0; vi < n; vi++)
                bucket.Add((coords[vi][0], coords[vi][1]));
        }
        var coastNodes = landVerts.Intersect(seaVerts)
            .Select(v => new CoastNode(
                Px: GeoToPixelX(v.Item1, p),
                Py: GeoToPixelY(v.Item2, p)))
            .ToList();
        Console.WriteLine($"Coast nodes: {coastNodes.Count} boundary vertices at h={CK3WaterLevel}");

        // ── 4. Early-out: terrain-only Delaunay rasterization ────────────────
        if (p.BaseOnly)
        {
            var basePoints = terrainNodes.Select(t  => (t.Px, t.Py, t.Height))
                                .Concat(coastNodes.Select(cn => (cn.Px, cn.Py, (float)CK3WaterLevel)));
            var baseCoords = terrainNodes.Select(t  => new Coordinate(t.Px, t.Py))
                                .Concat(coastNodes.Select(cn => new Coordinate(cn.Px, cn.Py)));
            var coordIndex   = BuildCoordIndex(basePoints);
            var triangulation = Triangulate(baseCoords);
            CheckCoastConnectivity(triangulation, coastNodes);
            var heightMap    = RasterizeTriangles(triangulation, coordIndex, p);
            ApplyGaussianBlur(heightMap, p);
            return new GenerateResult(ToBytes(heightMap), heightMap, terrainNodes, [], coastNodes);
        }

        // ── 4. Poly-node spawning — positions + spawn context only ────────────
        var rng    = new Random(p.Seed);
        float scaleX = p.Width  / p.LonT;
        float scaleY = p.Height / p.LatT;

        var spawnedNodes = new List<SpawnedNode>(landCells.Count * p.NodesPerCell);

        for (int ti = 0; ti < terrainNodes.Count; ti++)
        {
            var t = terrainNodes[ti];
            if (!t.IsLand) continue;

            float pixelArea = t.Area * scaleX * scaleY;
            float radius    = MathF.Sqrt(pixelArea / MathF.PI);

            for (int i = 0; i < p.NodesPerCell; i++)
            {
                // Stratified angular placement — one node per equal sector with jitter
                float angle = ((i + (float)rng.NextDouble()) / p.NodesPerCell) * 2f * MathF.PI;
                float r     = radius * MathF.Sqrt((float)rng.NextDouble());
                float nx    = Math.Clamp(t.Px + r * MathF.Cos(angle), 0, p.Width  - 1);
                float ny    = Math.Clamp(t.Py + r * MathF.Sin(angle), 0, p.Height - 1);
                spawnedNodes.Add(new SpawnedNode(nx, ny, nx, ny, ti));
            }
        }

        // ── 5. Relaxation pass (repulsion) ────────────────────────────────────
        float avgPixelArea = terrainNodes.Where(t => t.IsLand)
                                         .Average(t => t.Area * scaleX * scaleY);
        float minSep        = MathF.Sqrt(avgPixelArea / MathF.PI) / MathF.Sqrt(p.NodesPerCell) * 0.9f;
        float terrainMinSep = MathF.Sqrt(avgPixelArea) * p.TerrainToPolySep;

        var terrainPositions = terrainNodes.Select(t => (px: t.Px, py: t.Py)).ToList();
        if (p.RelaxIterations > 0)
            spawnedNodes = Relax(spawnedNodes, terrainPositions, p.RelaxIterations, minSep, terrainMinSep, p.RelaxStep, p.Width, p.Height);

        // ── 6. Cell polygons in pixel space for poly-node classification ─────────
        // Voronoi cells are small (6–10 vertices), so per-contributor polygon
        // containment tests are cheaper than building a union.
        var gf = new GeometryFactory();
        var cellPolygons = new Dictionary<int, NetTopologySuite.Geometries.Polygon>(cells.Count);
        foreach (var (id, cell) in cells)
        {
            var coords = cell.GeoDataCoordinates;
            if (coords == null || coords.Length < 4) continue;
            var ring = coords
                .Select(pt => new Coordinate(GeoToPixelX(pt[0], p), GeoToPixelY(pt[1], p)))
                .ToArray();
            if (!ring[0].Equals2D(ring[^1]))
                ring = [.. ring, ring[0]];
            try { cellPolygons[id] = gf.CreatePolygon(ring); }
            catch { /* degenerate polygon — skip */ }
        }

        // ── 7. Compute heights at final positions ─────────────────────────────
        // IDW height + IDW roughness from nearest terrain nodes — no parent-inherited values
        var polyNodes = new List<PolyNode>(spawnedNodes.Count);

        foreach (var s in spawnedNodes)
        {
            var nearest = terrainGrid.NearestN(s.Px, s.Py, p.PolyNodeSampleCount);
            if (nearest.Count == 0) continue;

            var weights = IdwWeights(nearest);

            float baseHeight = 0f, idwRoughness = 0f;
            for (int k = 0; k < nearest.Count; k++)
            {
                var tn       = terrainNodes[nearest[k].item];
                baseHeight   += weights[k] * tn.Height;
                idwRoughness += weights[k] * tn.Roughness;
            }

            float rawRand     = (float)(rng.NextDouble() * 2.0 - 1.0);
            float perturbation = rawRand * MathF.Pow(idwRoughness, p.RoughnessPower) * p.DisplacementStrength * (255f - CK3WaterLevel);
            float nodeHeight  = Math.Clamp(baseHeight + perturbation, 0f, 255f);

            // Classify the poly node by which contributor cell polygon contains it.
            // Walk IDW contributors nearest-first; first containing polygon wins.
            // A node that drifted into a sea cell gets capped below sea level;
            // one that stayed on land gets floored above sea level.
            // Fallback to IDW baseHeight comparison if no polygon matched.
            var pt = gf.CreatePoint(new Coordinate(s.Px, s.Py));
            bool? insideLand = null;
            for (int k = 0; k < nearest.Count && insideLand == null; k++)
            {
                var tn = terrainNodes[nearest[k].item];
                if (cellPolygons.TryGetValue(tn.Id, out var poly) && poly.Contains(pt))
                    insideLand = tn.IsLand;
            }
            insideLand ??= baseHeight > CK3WaterLevel;

            nodeHeight = insideLand.Value
                ? Math.Max(nodeHeight, CK3WaterLevel + 1f)   // land: floor above sea level
                : Math.Min(nodeHeight, CK3WaterLevel - 1f);  // sea:  cap below sea level

            // Top 3 contributors for debug (NearestN returns sorted by distance)
            int   c0 = nearest.Count > 0 ? nearest[0].item : -1;
            int   c1 = nearest.Count > 1 ? nearest[1].item : -1;
            int   c2 = nearest.Count > 2 ? nearest[2].item : -1;
            float w0 = weights.Length > 0 ? weights[0] : 0f;
            float w1 = weights.Length > 1 ? weights[1] : 0f;
            float w2 = weights.Length > 2 ? weights[2] : 0f;

            polyNodes.Add(new PolyNode(
                Px: s.Px, Py: s.Py,
                SpawnPx: s.SpawnPx, SpawnPy: s.SpawnPy,
                ParentId: s.ParentIdx,
                C0: c0, C1: c1, C2: c2,
                W0: w0, W1: w1, W2: w2,
                IdwRoughness: idwRoughness,
                RawRand: rawRand,
                Height: nodeHeight));
        }

        // ── 8. Delaunay triangulation + rasterization ─────────────────────────
        var allPoints = terrainNodes.Select(t  => (t.Px,  t.Py,  t.Height))
                           .Concat(polyNodes.Select(pn => (pn.Px, pn.Py, pn.Height)))
                           .Concat(coastNodes.Select(cn => (cn.Px, cn.Py, (float)CK3WaterLevel)));
        var combinedCoordIndex = BuildCoordIndex(allPoints);

        var allCoords = terrainNodes.Select(t  => new Coordinate(t.Px,  t.Py))
                           .Concat(polyNodes.Select(pn => new Coordinate(pn.Px, pn.Py)))
                           .Concat(coastNodes.Select(cn => new Coordinate(cn.Px, cn.Py)));
        var triangulation2 = Triangulate(allCoords);
        CheckCoastConnectivity(triangulation2, coastNodes);
        var heightMap2 = RasterizeTriangles(triangulation2, combinedCoordIndex, p);
        ApplyGaussianBlur(heightMap2, p);

        return new GenerateResult(ToBytes(heightMap2), heightMap2, terrainNodes, polyNodes, coastNodes);
    }

    // ── Relaxation ────────────────────────────────────────────────────────────

    static List<SpawnedNode> Relax(
        List<SpawnedNode> nodes,
        List<(float px, float py)> fixedRepulsors,
        int iterations, float minSep, float terrainMinSep, float step, int width, int height)
    {
        var pos = nodes.Select(n => (px: n.Px, py: n.Py)).ToList();

        // Terrain centroid grid — built once, never mutated
        int fc = Math.Max(1, (int)Math.Sqrt(fixedRepulsors.Count));
        int fr = Math.Max(1, fc * height / width);
        var fixedGrid = new SpatialGrid<int>(width, height, fc, fr);
        for (int i = 0; i < fixedRepulsors.Count; i++)
            fixedGrid.Add(fixedRepulsors[i].px, fixedRepulsors[i].py, i);

        for (int iter = 0; iter < iterations; iter++)
        {
            int cols = Math.Max(1, (int)Math.Sqrt(pos.Count));
            int rows = Math.Max(1, cols * height / width);
            var grid = new SpatialGrid<int>(width, height, cols, rows);
            for (int i = 0; i < pos.Count; i++)
                grid.Add(pos[i].px, pos[i].py, i);

            var dx = new float[pos.Count];
            var dy = new float[pos.Count];

            for (int i = 0; i < pos.Count; i++)
            {
                var (px, py) = pos[i];

                // Poly-poly repulsion (symmetric)
                foreach (var (dist, j) in grid.NearestN(px, py, 8))
                {
                    if (j == i || dist < 0.001f || dist >= minSep) continue;
                    float overlap = (minSep - dist) * 0.5f;
                    float fx = (px - pos[j].px) / dist * overlap;
                    float fy = (py - pos[j].py) / dist * overlap;
                    dx[i] += fx; dy[i] += fy;
                    dx[j] -= fx; dy[j] -= fy;
                }

                // Fixed terrain centroid push — one-sided, centroid never moves
                foreach (var (dist, fi) in fixedGrid.NearestN(px, py, 4))
                {
                    if (dist >= terrainMinSep) break;
                    if (dist < 0.001f) continue;
                    var (rx, ry) = fixedRepulsors[fi];
                    float overlap = terrainMinSep - dist;
                    dx[i] += (px - rx) / dist * overlap;
                    dy[i] += (py - ry) / dist * overlap;
                }
            }

            for (int i = 0; i < pos.Count; i++)
                pos[i] = (
                    px: Math.Clamp(pos[i].px + dx[i] * step, 0, width  - 1),
                    py: Math.Clamp(pos[i].py + dy[i] * step, 0, height - 1));
        }

        return nodes.Select((n, i) => n with { Px = pos[i].px, Py = pos[i].py }).ToList();
    }

    // ── Gaussian blur ─────────────────────────────────────────────────────────

    static void ApplyGaussianBlur(float[] heightMap, Params p)
    {
        int r = p.BlurRadius;
        if (r <= 0) return;

        // Precompute 1-D kernel (sigma = r/2)
        float sigma = r / 2f;
        int kLen = 2 * r + 1;
        var kernel = new float[kLen];
        float kSum = 0f;
        for (int i = 0; i < kLen; i++)
        {
            float x = i - r;
            kernel[i] = MathF.Exp(-(x * x) / (2 * sigma * sigma));
            kSum += kernel[i];
        }
        for (int i = 0; i < kLen; i++) kernel[i] /= kSum;

        int w = p.Width, h = p.Height;
        var temp = new float[w * h];  // one extra buffer, same size as heightMap

        // Horizontal pass: heightMap → temp  (sequential row access, cache-friendly)
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float val = 0f;
                for (int k = -r; k <= r; k++)
                    val += kernel[k + r] * heightMap[row + Math.Clamp(x + k, 0, w - 1)];
                temp[row + x] = val;
            }
        }

        // Vertical pass: temp → heightMap  (strided column access, unavoidable)
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float val = 0f;
                for (int k = -r; k <= r; k++)
                    val += kernel[k + r] * temp[Math.Clamp(y + k, 0, h - 1) * w + x];
                heightMap[row + x] = Math.Clamp(val, 0f, 255f);
            }
        }
    }

    // ── Triangulation helpers ─────────────────────────────────────────────────

    static GeometryCollection Triangulate(IEnumerable<Coordinate> points)
    {
        var gf = new GeometryFactory();
        var builder = new DelaunayTriangulationBuilder();
        builder.SetSites(gf.CreateMultiPointFromCoords(points.ToArray()));
        return builder.GetTriangles(gf);
    }

    // Accept a pre-built triangulation so the caller can inspect it (e.g. assertions) before rasterizing.
    static float[] RasterizeTriangles(
        GeometryCollection triangles,
        Dictionary<(int, int), float> coordIndex,
        Params p,
        float clampMax = 255f)
    {
        var heightMap = new float[p.Width * p.Height];
        foreach (var geom in triangles.Geometries)
        {
            var ring = geom.Boundary.Coordinates;
            if (ring.Length < 3) continue;
            var v0 = ring[0]; var v1 = ring[1]; var v2 = ring[2];
            RasterizeTriangle(v0, v1, v2,
                Lookup(coordIndex, v0), Lookup(coordIndex, v1), Lookup(coordIndex, v2),
                heightMap, p.Width, p.Height, clampMax);
        }
        return heightMap;
    }

    // Assert that every coast node has ≥ 2 Delaunay edges connecting it to other coast nodes.
    // A node with only 1 such edge is a degenerate "spike" — the waterline can't form a closed
    // loop through it, which produces visible artefacts in the heightmap.
    static void CheckCoastConnectivity(GeometryCollection triangles, List<CoastNode> coastNodes)
    {
        if (coastNodes.Count == 0) return;

        var coastKeys = new HashSet<(int, int)>(
            coastNodes.Select(cn => (RoundCoord(cn.Px), RoundCoord(cn.Py))));

        var connections = coastKeys.ToDictionary(k => k, _ => 0);
        var seen        = new HashSet<((int, int), (int, int))>();

        foreach (var geom in triangles.Geometries)
        {
            var ring = geom.Boundary.Coordinates;
            for (int e = 0; e < 3; e++)
            {
                var a = (RoundCoord((float)ring[e].X),         RoundCoord((float)ring[e].Y));
                var b = (RoundCoord((float)ring[(e + 1) % 3].X), RoundCoord((float)ring[(e + 1) % 3].Y));
                if (!coastKeys.Contains(a) || !coastKeys.Contains(b)) continue;
                // Normalise edge key so (a,b) and (b,a) are the same entry
                var edge = (a.CompareTo(b) <= 0) ? (a, b) : (b, a);
                if (!seen.Add(edge)) continue;
                connections[a]++;
                connections[b]++;
            }
        }

        var degenerate = connections.Where(kv => kv.Value < 2).ToList();
        if (degenerate.Count > 0)
            Console.WriteLine($"WARN: {degenerate.Count}/{coastNodes.Count} coast nodes have <2 coast–coast Delaunay edges (degenerate coastline spike)");
        else
            Console.WriteLine($"Coast connectivity OK — all {coastNodes.Count} nodes have ≥2 coast–coast edges");
    }

    // ── Public diagnostic rasters ─────────────────────────────────────────────

    /// <summary>
    /// Returns a float[width*height] where each pixel is the barycentric-interpolated
    /// roughness of the Delaunay triangle it falls in, using terrain nodes only.
    /// Values are in [0, 1] (same scale as TerrainNode.Roughness).
    /// </summary>
    public static float[] RasterizeRoughness(IReadOnlyList<TerrainNode> terrainNodes, Params p)
    {
        var coordIndex = BuildCoordIndex(
            terrainNodes.Select(t => (t.Px, t.Py, t.Roughness)));
        return Rasterize(coordIndex,
            terrainNodes.Select(t => new Coordinate(t.Px, t.Py)),
            p, clampMax: 1f);
    }

    // ── Core rasterization ────────────────────────────────────────────────────

    static float[] Rasterize(
        Dictionary<(int, int), float> coordIndex,
        IEnumerable<Coordinate> points,
        Params p,
        float clampMax = 255f)
    {
        var gf      = new GeometryFactory();
        var builder = new DelaunayTriangulationBuilder();
        builder.SetSites(gf.CreateMultiPointFromCoords(points.ToArray()));
        var triangles = builder.GetTriangles(gf);

        var heightMap = new float[p.Width * p.Height];

        foreach (var geom in triangles.Geometries)
        {
            var ring = geom.Boundary.Coordinates;
            if (ring.Length < 3) continue;
            var v0 = ring[0]; var v1 = ring[1]; var v2 = ring[2];
            RasterizeTriangle(v0, v1, v2,
                Lookup(coordIndex, v0), Lookup(coordIndex, v1), Lookup(coordIndex, v2),
                heightMap, p.Width, p.Height, clampMax);
        }

        return heightMap;
    }

    static void RasterizeTriangle(
        Coordinate v0, Coordinate v1, Coordinate v2,
        float h0, float h1, float h2,
        float[] heightMap, int width, int height,
        float clampMax = 255f)
    {
        int xMin = Math.Max(0,          (int)Math.Min(v0.X, Math.Min(v1.X, v2.X)));
        int xMax = Math.Min(width  - 1, (int)Math.Ceiling(Math.Max(v0.X, Math.Max(v1.X, v2.X))));
        int yMin = Math.Max(0,          (int)Math.Min(v0.Y, Math.Min(v1.Y, v2.Y)));
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
                heightMap[py * width + px] = Math.Clamp(w0 * h0 + w1 * h1 + w2 * h2, 0f, clampMax);
            }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static float[] IdwWeights(List<(float dist, int item)> nearest)
    {
        var weights = new float[nearest.Count];
        float sum = 0f;
        for (int i = 0; i < nearest.Count; i++)
        {
            float w = nearest[i].dist < 0.001f ? 1e6f : 1f / (nearest[i].dist * nearest[i].dist);
            weights[i] = w;
            sum += w;
        }
        if (sum > 0f)
            for (int i = 0; i < weights.Length; i++)
                weights[i] /= sum;
        return weights;
    }

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
}
