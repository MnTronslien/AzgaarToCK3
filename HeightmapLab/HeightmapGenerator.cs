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
        bool BaseOnly = false,
        float CoastExclusionRadius = 30f,
        float MaxCoastEdgePixels = 60f);  // pre-subdivide coast edges longer than this

    // Per-node coast connectivity result, indexed parallel to the CoastNodes list.
    public record CoastConnectivity(
        IReadOnlyList<int>  Connections, // coast-coast Delaunay edge count per node
        IReadOnlyList<bool> IsHullNode); // true = node has a boundary (hull) Delaunay edge → probably a valid exception

    public record GenerateResult(
        byte[] Pixels,
        float[] HeightmapF,
        IReadOnlyList<TerrainNode> TerrainNodes,
        IReadOnlyList<PolyNode> PolyNodes,
        IReadOnlyList<CoastNode> CoastNodes,
        CoastConnectivity Connectivity,
        int OriginalCoastNodeCount,         // nodes at index ≥ this were absorbed as Steiner points
        IReadOnlyList<CoastNode> CascadingNodes); // Steiner nodes involved in Steiner-Steiner edges

    // Internal to this file — carries spawn position + parent context through relaxation
    private record struct SpawnedNode(float Px, float Py, float SpawnPx, float SpawnPy, int ParentIdx);

    public static GenerateResult Generate(IReadOnlyDictionary<int, Cell> cells, Params p)
    {
        // ── 1. Build terrain nodes ────────────────────────────────────────────
        var landCells = cells.Values.Where(c => Cell.IsDryLand(c.Type)).ToList();
        if (landCells.Count == 0)
            return new GenerateResult(new byte[p.Width * p.Height], new float[p.Width * p.Height], [], [], [], new CoastConnectivity([], []), 0, []);

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

        // ── 3. Coast nodes via per-sea-body coastline walk ────────────────────
        // A land cell can border more than one sea body (e.g. it sits between the
        // ocean and an inland lake). If we build a single global adjacency the
        // shared corner vertices would have degree-4, mixing the two coastlines
        // during the walk. Instead we flood-fill sea cells into connected bodies
        // and run the adjacency build + walk once per body. Shared vertices appear
        // as degree-2 nodes in each body's graph independently.

        // 3a — sea connected-component flood fill
        var seaBodyId  = new Dictionary<int, int>(cells.Count / 2);
        int numSeaBodies = 0;
        foreach (var (id, cell) in cells)
        {
            if (Cell.IsDryLand(cell.Type) || seaBodyId.ContainsKey(id)) continue;
            int body = numSeaBodies++;
            var bfsQ = new Queue<int>();
            bfsQ.Enqueue(id); seaBodyId[id] = body;
            while (bfsQ.Count > 0)
            {
                int cur = bfsQ.Dequeue();
                foreach (var nId in cells[cur].Neighbors)
                {
                    if (!cells.TryGetValue(nId, out var nc) || Cell.IsDryLand(nc.Type) || seaBodyId.ContainsKey(nId)) continue;
                    seaBodyId[nId] = body; bfsQ.Enqueue(nId);
                }
            }
        }
        if (numSeaBodies > 1)
            Console.WriteLine($"Sea: {numSeaBodies} bodies (1 ocean + {numSeaBodies - 1} inland lake(s))");

        // 3b — per-body adjacency build + walk + flatten
        var gfConstr   = new GeometryFactory();
        var pixToFloat = new Dictionary<(int,int), (float px, float py)>();
        var coastNodes     = new List<CoastNode>();
        var constraintSegs = new List<NetTopologySuite.Geometries.LineString>();
        int preInserted = 0, totalWalkVerts = 0, totalLoops = 0, totalOpenPaths = 0, totalIsolated = 0;

        void AddSegment(CoastNode a, CoastNode b)
        {
            if (RoundCoord(a.Px) == RoundCoord(b.Px) && RoundCoord(a.Py) == RoundCoord(b.Py)) return;
            constraintSegs.Add(gfConstr.CreateLineString([
                new Coordinate(a.Px, a.Py), new Coordinate(b.Px, b.Py)
            ]));
        }

        for (int body = 0; body < numSeaBodies; body++)
        {
            var coastAdj  = new Dictionary<(int,int), List<(int,int)>>();
            var coastEdges = new HashSet<((int,int),(int,int))>();

            foreach (var (_, cell) in cells)
            {
                if (!Cell.IsDryLand(cell.Type)) continue;
                var lc = cell.GeoDataCoordinates;
                if (lc == null) continue;
                int ln = lc.Length - 1;

                foreach (var nbrId in cell.Neighbors)
                {
                    if (!cells.TryGetValue(nbrId, out var nbr) || Cell.IsDryLand(nbr.Type)) continue;
                    if (!seaBodyId.TryGetValue(nbrId, out int nbrBody) || nbrBody != body) continue;

                    var sc = nbr.GeoDataCoordinates;
                    if (sc == null) continue;
                    int sn = sc.Length - 1;

                    var sKeys = new HashSet<(int,int)>(sn);
                    for (int i = 0; i < sn; i++)
                        sKeys.Add((RoundCoord(GeoToPixelX(sc[i][0], p)), RoundCoord(GeoToPixelY(sc[i][1], p))));

                    for (int vi = 0; vi < ln; vi++)
                    {
                        float pxA = GeoToPixelX(lc[vi][0], p),        pyA = GeoToPixelY(lc[vi][1], p);
                        float pxB = GeoToPixelX(lc[(vi+1)%ln][0], p), pyB = GeoToPixelY(lc[(vi+1)%ln][1], p);
                        var kA = (RoundCoord(pxA), RoundCoord(pyA));
                        var kB = (RoundCoord(pxB), RoundCoord(pyB));
                        if (kA == kB || !sKeys.Contains(kA) || !sKeys.Contains(kB)) continue;

                        pixToFloat.TryAdd(kA, (pxA, pyA));
                        pixToFloat.TryAdd(kB, (pxB, pyB));

                        var edgeKey = kA.CompareTo(kB) <= 0 ? (kA, kB) : (kB, kA);
                        if (!coastEdges.Add(edgeKey)) continue;

                        if (!coastAdj.TryGetValue(kA, out var la)) coastAdj[kA] = la = [];
                        if (!coastAdj.TryGetValue(kB, out var lb)) coastAdj[kB] = lb = [];
                        la.Add(kB); lb.Add(kA);
                    }
                }
            }

            if (coastAdj.Count == 0) continue;

            var (paths, visited, deg1, deg3p) = WalkCoastBody(coastAdj);
            if (deg1 > 0 || deg3p > 0)
                Console.WriteLine($"  Sea body {body}: {deg1} boundary endpoints (degree-1), {deg3p} junctions (degree-3+)");

            // Flatten paths → coast nodes + constraints with pre-subdivision
            foreach (var (path, isClosed) in paths)
            {
                int pathStart = coastNodes.Count;
                int n         = path.Count;
                int edgeCount = isClosed ? n : n - 1;

                for (int i = 0; i < edgeCount; i++)
                {
                    var (pxA, pyA) = pixToFloat[path[i]];
                    var (pxB, pyB) = pixToFloat[path[(i + 1) % n]];

                    coastNodes.Add(new CoastNode(pxA, pyA));

                    if (p.MaxCoastEdgePixels > 0)
                    {
                        float dist = MathF.Sqrt((pxA - pxB) * (pxA - pxB) + (pyA - pyB) * (pyA - pyB));
                        if (dist > p.MaxCoastEdgePixels)
                        {
                            int nIns = (int)(dist / p.MaxCoastEdgePixels);
                            for (int k = 1; k <= nIns; k++)
                            {
                                float t = k / (float)(nIns + 1);
                                coastNodes.Add(new CoastNode(pxA + t * (pxB - pxA), pyA + t * (pyB - pyA)));
                                preInserted++;
                            }
                        }
                    }
                }

                if (!isClosed)
                {
                    var (pxLast, pyLast) = pixToFloat[path[n - 1]];
                    coastNodes.Add(new CoastNode(pxLast, pyLast));
                }

                int pathEnd = coastNodes.Count;
                for (int i = pathStart; i < pathEnd - 1; i++)
                    AddSegment(coastNodes[i], coastNodes[i + 1]);
                if (isClosed)
                    AddSegment(coastNodes[pathEnd - 1], coastNodes[pathStart]);
            }

            // Adjacency vertices not reached by any walk become isolated coast nodes
            foreach (var k in coastAdj.Keys)
            {
                if (visited.Contains(k)) continue;
                var (fpx, fpy) = pixToFloat[k];
                coastNodes.Add(new CoastNode(fpx, fpy));
                totalIsolated++;
            }

            totalLoops      += paths.Count(t => t.isClosed);
            totalOpenPaths  += paths.Count(t => !t.isClosed);
            totalWalkVerts  += coastAdj.Count;
        }

        Console.WriteLine($"Coast: {totalLoops} loop(s) + {totalOpenPaths} open path(s)" +
                          $", {totalWalkVerts} walk verts + {preInserted} pre-inserted + {totalIsolated} isolated" +
                          $" = {coastNodes.Count} nodes, {constraintSegs.Count} constraints");
        int originalCoastCount = coastNodes.Count; // CDT Steiner points land at index ≥ this

        // ── 4. Early-out: terrain-only Delaunay rasterization ────────────────
        if (p.BaseOnly)
        {
            var basePoints = terrainNodes.Select(t  => (t.Px, t.Py, t.Height))
                                .Concat(coastNodes.Select(cn => (cn.Px, cn.Py, (float)CK3WaterLevel)));
            var baseCoords = terrainNodes.Select(t  => new Coordinate(t.Px, t.Py))
                                .Concat(coastNodes.Select(cn => new Coordinate(cn.Px, cn.Py)));
            var coordIndex    = BuildCoordIndex(basePoints);
            var triangulation = Triangulate(baseCoords, constraintSegs);
            AbsorbSteinerPoints(triangulation, coordIndex, coastNodes, CK3WaterLevel);
            var cascadingBase = AssertNoSteinerSteinerEdges(triangulation, coastNodes, originalCoastCount, constraintSegs, terrainNodes);
            var connectivity  = CheckCoastConnectivity(triangulation, coastNodes);
            var heightMap     = RasterizeTriangles(triangulation, coordIndex, p);
            ApplyGaussianBlur(heightMap, p);
            return new GenerateResult(ToBytes(heightMap), heightMap, terrainNodes, [], coastNodes, connectivity, originalCoastCount, cascadingBase);
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

        // ── 6. Cell polygons + coast proximity grid ───────────────────────────
        // Voronoi cells are small (6–10 vertices), so per-contributor polygon
        // containment tests are cheaper than building a union.
        // Coast grid: used to enforce the exclusion radius (see step 7).
        var gf = new GeometryFactory();

        SpatialGrid<int>? coastProxGrid = null;
        if (p.CoastExclusionRadius > 0 && coastNodes.Count > 0)
        {
            int ccols = Math.Max(1, (int)Math.Sqrt(coastNodes.Count));
            int crows = Math.Max(1, ccols * p.Height / p.Width);
            coastProxGrid = new SpatialGrid<int>(p.Width, p.Height, ccols, crows);
            for (int ci = 0; ci < coastNodes.Count; ci++)
                coastProxGrid.Add(coastNodes[ci].Px, coastNodes[ci].Py, ci);
        }
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

            if (!insideLand.Value) continue; // sea poly nodes contribute nothing — drop them

            // Coast exclusion zone: drop poly nodes within CoastExclusionRadius of any coast node.
            // Prevents poly nodes from sitting between two coast nodes and blocking a coast-coast
            // Delaunay edge that isn't in the CDT constraint set.
            if (coastProxGrid != null)
            {
                var cn1 = coastProxGrid.NearestN(s.Px, s.Py, 1);
                if (cn1.Count > 0 && cn1[0].dist < p.CoastExclusionRadius) continue;
            }

            nodeHeight = Math.Max(nodeHeight, CK3WaterLevel + 1f); // land: floor above sea level

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
        var triangulation2 = Triangulate(allCoords, constraintSegs);
        AbsorbSteinerPoints(triangulation2, combinedCoordIndex, coastNodes, CK3WaterLevel);
        var cascading = AssertNoSteinerSteinerEdges(triangulation2, coastNodes, originalCoastCount, constraintSegs, terrainNodes);
        var connectivity2  = CheckCoastConnectivity(triangulation2, coastNodes);
        var heightMap2     = RasterizeTriangles(triangulation2, combinedCoordIndex, p);
        ApplyGaussianBlur(heightMap2, p);

        return new GenerateResult(ToBytes(heightMap2), heightMap2, terrainNodes, polyNodes, coastNodes, connectivity2, originalCoastCount, cascading);
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

    static GeometryCollection Triangulate(
        IEnumerable<Coordinate> points,
        IReadOnlyList<NetTopologySuite.Geometries.LineString>? constraints = null)
    {
        var gf = new GeometryFactory();
        var pts = points.ToArray();
        if (constraints != null && constraints.Count > 0)
        {
            var builder = new ConformingDelaunayTriangulationBuilder();
            builder.SetSites(gf.CreateMultiPointFromCoords(pts));
            builder.Constraints = gf.CreateMultiLineString(constraints.ToArray());
            return builder.GetTriangles(gf);
        }
        else
        {
            var builder = new DelaunayTriangulationBuilder();
            builder.SetSites(gf.CreateMultiPointFromCoords(pts));
            return builder.GetTriangles(gf);
        }
    }

    // ConformingDelaunay may insert Steiner points on constraint edges to resolve
    // violations. These points are not in coordIndex, which would cause Lookup()
    // to return 0 and rasterize them as ocean. Since they lie on coast edges they
    // inherit h=CK3WaterLevel. We also add them to coastNodes so the connectivity
    // check counts them correctly.
    static void AbsorbSteinerPoints(
        GeometryCollection triangles,
        Dictionary<(int, int), float> coordIndex,
        List<CoastNode> coastNodes,
        float height)
    {
        int added = 0;
        foreach (var geom in triangles.Geometries)
        foreach (var v in geom.Boundary.Coordinates)
        {
            var key = (RoundCoord((float)v.X), RoundCoord((float)v.Y));
            if (coordIndex.ContainsKey(key)) continue;
            coordIndex[key] = height;
            coastNodes.Add(new CoastNode((float)v.X, (float)v.Y));
            added++;
        }
        if (added > 0)
            Console.WriteLine($"Absorbed {added} Steiner points at h={height}");
    }

    // Asserts that no Steiner point (index >= originalCount in coastNodes) has a
    // Delaunay edge to another Steiner point. Steiner-Steiner edges indicate that
    // ConformingDelaunay inserted a chain of intermediate points, which means a
    // constraint edge was far from any existing site — a sign of a bad constraint
    // (e.g. cross-land or cross-strait pair that slipped through).
    // Returns the list of Steiner nodes involved in Steiner-Steiner edges (empty = pass).
    static List<CoastNode> AssertNoSteinerSteinerEdges(
        GeometryCollection triangles,
        List<CoastNode> coastNodes,
        int originalCount,
        IReadOnlyList<NetTopologySuite.Geometries.LineString>? constraintSegs = null,
        IReadOnlyList<TerrainNode>? terrainNodes = null)
    {
        if (coastNodes.Count <= originalCount)
        {
            Console.WriteLine("Steiner-Steiner OK — no Steiner points inserted");
            return [];
        }

        var steinerByKey = new Dictionary<(int,int), CoastNode>(coastNodes.Count - originalCount);
        for (int i = originalCount; i < coastNodes.Count; i++)
            steinerByKey.TryAdd((RoundCoord(coastNodes[i].Px), RoundCoord(coastNodes[i].Py)), coastNodes[i]);

        var seen     = new HashSet<((int,int),(int,int))>();
        var pairList = new List<((int,int) ka, (int,int) kb)>();

        foreach (var geom in triangles.Geometries)
        {
            var ring = geom.Boundary.Coordinates;
            for (int e = 0; e < 3; e++)
            {
                var ka = (RoundCoord((float)ring[e].X),           RoundCoord((float)ring[e].Y));
                var kb = (RoundCoord((float)ring[(e + 1) % 3].X), RoundCoord((float)ring[(e + 1) % 3].Y));
                if (!steinerByKey.ContainsKey(ka) || !steinerByKey.ContainsKey(kb)) continue;
                var edge = ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka);
                if (!seen.Add(edge)) continue;
                pairList.Add((ka, kb));
            }
        }

        int nSteiner = coastNodes.Count - originalCount;
        if (seen.Count == 0)
        {
            Console.WriteLine($"Bay-shortcut OK — all {nSteiner} Steiner points clean");
            return [];
        }

        // Classify every pair. Bay-shortcuts are the only real artifact:
        //   SAME-SEG     — both Steiners on the same constraint (CDT multi-insertion). Benign;
        //                  reduce MaxCoastEdgePixels to eliminate.
        //   CAPE-CLIP    — adj-seg pair whose midpoint is over land. Benign.
        //   BAY-SHORTCUT — adj-seg pair whose midpoint is over sea. Real artifact: the
        //                  Delaunay edge cuts across open water, rasterising it at
        //                  CK3WaterLevel instead of 0. Assertion only flags these.

        // Build terrain spatial index for midpoint classification
        SpatialGrid<int>? tGrid = null;
        if (terrainNodes != null && terrainNodes.Count > 0)
        {
            int tc = Math.Max(1, (int)Math.Sqrt(terrainNodes.Count));
            float tw = terrainNodes.Max(t => t.Px), th = terrainNodes.Max(t => t.Py);
            tGrid = new SpatialGrid<int>((int)(tw + 1), (int)(th + 1), tc, Math.Max(1, tc / 2));
            for (int ti = 0; ti < terrainNodes.Count; ti++)
                tGrid.Add(terrainNodes[ti].Px, terrainNodes[ti].Py, ti);
        }

        var bayKeys = new HashSet<(int,int)>(); // only BAY-SHORTCUT nodes returned/visualised

        if (constraintSegs != null && constraintSegs.Count > 0)
        {
            int FindConstraint(float px, float py)
            {
                float bestErr = 2f;
                int   bestIdx = -1;
                for (int ci = 0; ci < constraintSegs.Count; ci++)
                {
                    var cs = constraintSegs[ci].Coordinates;
                    float ax = (float)cs[0].X, ay = (float)cs[0].Y;
                    float bx = (float)cs[1].X, by = (float)cs[1].Y;
                    float dx = bx - ax, dy = by - ay;
                    float len2 = dx * dx + dy * dy;
                    if (len2 < 0.01f) continue;
                    float t = ((px - ax) * dx + (py - ay) * dy) / len2;
                    if (t < -0.01f || t > 1.01f) continue;
                    float ex = ax + t * dx - px, ey = ay + t * dy - py;
                    float err = MathF.Sqrt(ex * ex + ey * ey);
                    if (err < bestErr) { bestErr = err; bestIdx = ci; }
                }
                return bestIdx;
            }

            float SegLen(int ci)
            {
                var cs = constraintSegs[ci].Coordinates;
                float dx = (float)(cs[1].X - cs[0].X), dy = (float)(cs[1].Y - cs[0].Y);
                return MathF.Sqrt(dx * dx + dy * dy);
            }

            bool MidpointIsOcean(float px, float py)
            {
                if (tGrid == null || terrainNodes == null) return false;
                var nearest = tGrid.NearestN(px, py, 1);
                return nearest.Count > 0 && !terrainNodes[nearest[0].item].IsLand;
            }

            int nSameSeg = 0, nBay = 0, nCape = 0, nUnknown = 0;
            var bayDists = new List<float>();

            foreach (var (ka, kb) in pairList)
            {
                var sa = steinerByKey[ka];
                var sb = steinerByKey[kb];
                float dist = MathF.Sqrt((sa.Px - sb.Px) * (sa.Px - sb.Px) + (sa.Py - sb.Py) * (sa.Py - sb.Py));
                float midX = (sa.Px + sb.Px) / 2f, midY = (sa.Py + sb.Py) / 2f;
                int ca = FindConstraint(sa.Px, sa.Py);
                int cb = FindConstraint(sb.Px, sb.Py);

                if (ca < 0 || cb < 0)
                {
                    nUnknown++;
                    // Can't classify — include in output to be safe
                    bayKeys.Add(ka); bayKeys.Add(kb);
                    Console.WriteLine($"  [UNKNOWN      ] edge-dist={dist:F1}px  mid=({midX:F0},{midY:F0})  S({sa.Px:F1},{sa.Py:F1}) ↔ S({sb.Px:F1},{sb.Py:F1})");
                }
                else if (ca == cb)
                {
                    nSameSeg++;
                    Console.WriteLine($"  [same-seg     ] edge-dist={dist:F1}px  seg-len={SegLen(ca):F1}px  (benign — reduce MaxCoastEdgePixels)");
                }
                else if (MidpointIsOcean(midX, midY))
                {
                    nBay++;
                    bayDists.Add(dist);
                    bayKeys.Add(ka); bayKeys.Add(kb);
                    Console.WriteLine($"  [BAY-SHORTCUT ] edge-dist={dist:F1}px  mid=({midX:F0},{midY:F0})  seg-a={SegLen(ca):F1}px  seg-b={SegLen(cb):F1}px");
                }
                else
                {
                    nCape++;
                    Console.WriteLine($"  [cape-clip    ] edge-dist={dist:F1}px  mid=({midX:F0},{midY:F0})  (benign — midpoint over land)");
                }
            }

            if (nBay > 0)
            {
                bayDists.Sort();
                float avg = bayDists.Average();
                float med = bayDists[bayDists.Count / 2];
                Console.WriteLine($"  BAY-SHORTCUT SUMMARY: {nBay} artifact(s)  " +
                                  $"min={bayDists[0]:F1}px  med={med:F1}px  avg={avg:F1}px  max={bayDists[^1]:F1}px");
                Console.WriteLine($"  (also: {nSameSeg} same-seg, {nCape} cape-clip — both benign and excluded from visualisation)");
            }
            else
            {
                Console.WriteLine($"  Bay-shortcut OK — {nSameSeg} same-seg + {nCape} cape-clip (all benign)");
            }
        }
        else
        {
            // No constraint info: can't classify, include all pairs as a fallback
            foreach (var (ka, kb) in pairList) { bayKeys.Add(ka); bayKeys.Add(kb); }
            Console.WriteLine($"  {seen.Count} Steiner-Steiner edge(s) — pass constraintSegs for bay/cape classification");
        }

        return bayKeys.Select(k => steinerByKey[k]).ToList();
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

    // Returns per-node connectivity data indexed parallel to coastNodes.
    // Connections[i] = number of unique Delaunay edges from node i to another coast node.
    // IsHullNode[i]  = true if node i has at least one hull edge (appears in only 1 triangle),
    //                  making it a probable valid exception to the ≥2 rule.
    static CoastConnectivity CheckCoastConnectivity(GeometryCollection triangles, List<CoastNode> coastNodes)
    {
        int n = coastNodes.Count;
        var connections = new int[n];
        var isHull      = new bool[n];

        if (n == 0) return new CoastConnectivity(connections, isHull);

        // Build key → index map for O(1) lookup
        var keyToIdx = new Dictionary<(int, int), int>(n);
        for (int i = 0; i < n; i++)
            keyToIdx[(RoundCoord(coastNodes[i].Px), RoundCoord(coastNodes[i].Py))] = i;
        var coastKeys = keyToIdx.Keys.ToHashSet();

        // Pass 1: count triangles per edge (ALL edges, not just coast-coast).
        //         Edges with count == 1 are hull (boundary) edges.
        var edgeCount = new Dictionary<((int,int),(int,int)), int>();
        foreach (var geom in triangles.Geometries)
        {
            var ring = geom.Boundary.Coordinates;
            for (int e = 0; e < 3; e++)
            {
                var a = (RoundCoord((float)ring[e].X),             RoundCoord((float)ring[e].Y));
                var b = (RoundCoord((float)ring[(e + 1) % 3].X),   RoundCoord((float)ring[(e + 1) % 3].Y));
                var edge = (a.CompareTo(b) <= 0) ? (a, b) : (b, a);
                edgeCount.TryGetValue(edge, out int cnt);
                edgeCount[edge] = cnt + 1;
            }
        }

        // Pass 2: for each coast node, check hull status and count coast-coast connections.
        var seenCoastEdges = new HashSet<((int,int),(int,int))>();
        foreach (var (edge, cnt) in edgeCount)
        {
            var (a, b) = edge;
            bool aIsCoast = coastKeys.Contains(a);
            bool bIsCoast = coastKeys.Contains(b);

            // Hull detection: any edge with count==1 marks its coast endpoints as hull nodes
            if (cnt == 1)
            {
                if (aIsCoast && keyToIdx.TryGetValue(a, out int ai)) isHull[ai] = true;
                if (bIsCoast && keyToIdx.TryGetValue(b, out int bi)) isHull[bi] = true;
            }

            // Coast-coast connection count (deduplicated via seenCoastEdges)
            if (aIsCoast && bIsCoast && seenCoastEdges.Add(edge))
            {
                connections[keyToIdx[a]]++;
                connections[keyToIdx[b]]++;
            }
        }

        int degenReal = 0, degenHull = 0;
        for (int i = 0; i < n; i++)
        {
            if (connections[i] < 2)
                (isHull[i] ? ref degenHull : ref degenReal)++;
        }
        if (degenReal + degenHull > 0)
            Console.WriteLine($"WARN: {degenReal + degenHull}/{n} coast nodes <2 coast-coast edges " +
                              $"({degenReal} real problems, {degenHull} probable hull exceptions)");
        else
            Console.WriteLine($"Coast connectivity OK — all {n} nodes ≥2 coast-coast edges");

        return new CoastConnectivity(connections, isHull);
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

    // Walks a coast adjacency graph into ordered paths and closed loops.
    // Pass 1 starts from degree-1 vertices (map-boundary endpoints of open paths).
    // Pass 2 picks up closed loops from any remaining unvisited vertices.
    // Returns the path list, the visited set (for isolated-node detection by caller),
    // and degree-1 / degree-3+ vertex counts for diagnostic logging.
    static (List<(List<(int,int)> nodes, bool isClosed)> paths,
            HashSet<(int,int)> visited,
            int deg1, int deg3p)
    WalkCoastBody(Dictionary<(int,int), List<(int,int)>> adj)
    {
        var visited = new HashSet<(int,int)>();
        var paths   = new List<(List<(int,int)>, bool)>();
        int deg1 = 0, deg3p = 0;

        (int,int)? PickNext((int,int) curr, (int,int)? prev)
        {
            foreach (var n in adj[curr])
                if (n != prev && !visited.Contains(n)) return n;
            return null;
        }

        void WalkFrom((int,int) start)
        {
            if (visited.Contains(start)) return;
            var path = new List<(int,int)>();
            (int,int)? prev = null;
            var curr = start;
            while (curr != default && !visited.Contains(curr))
            {
                path.Add(curr);
                visited.Add(curr);
                var next = PickNext(curr, prev);
                prev = curr;
                curr = next ?? default;
            }
            // A closed loop ends when PickNext would re-enter start (which is already
            // visited). Detect by checking whether the last walked node is adjacent to start.
            bool closed = path.Count >= 3 &&
                          adj.TryGetValue(path[^1], out var lastNbrs) &&
                          lastNbrs.Contains(start);
            if (path.Count >= 2 || (path.Count == 1 && closed)) paths.Add((path, closed));
        }

        foreach (var k in adj.Keys)
        {
            int deg = adj[k].Count;
            if (deg == 1) { deg1++; WalkFrom(k); }
            else if (deg >= 3) deg3p++;
        }
        foreach (var k in adj.Keys)
            WalkFrom(k);

        return (paths, visited, deg1, deg3p);
    }

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
