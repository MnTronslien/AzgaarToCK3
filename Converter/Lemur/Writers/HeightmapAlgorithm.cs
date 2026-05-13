using Converter.Lemur;
using Converter.Lemur.Entities;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace Converter.Lemur.Writers;

public static class HeightmapAlgorithm
{
    public const int CK3WaterLevel = 20;

    public record Params(
        float LonW, float LonT,
        float LatS, float LatT,
        int Width, int Height,
        int Seed = 42,
        float DisplacementStrength = 0.25f,
        int NodesPerCell = 4,
        int PolyNodeSampleCount = 4,
        float RoughnessNorm = 1f,
        int RelaxIterations = 5,
        float TerrainToPolySep = 0.25f,
        float RelaxStep = 0.05f,
        int BlurRadius = 3,
        float RoughnessPower = 2.0f,
        float CoastExclusionRadius = 30f,
        float MaxCoastEdgePixels = 60f,
        float RiverCenterlineDepth = 5f)
    {
        public static Params FromMap(Map map) => new(
            LonW:   map.JsonMap.mapCoordinates.lonW,
            LonT:   map.JsonMap.mapCoordinates.lonT,
            LatS:   map.JsonMap.mapCoordinates.latS,
            LatT:   map.JsonMap.mapCoordinates.latT,
            Width:  Map.MapWidth,
            Height: Map.MapHeight);
    }

    // Carrier for major-river centerline data. Used to seed TerrainNodes along
    // each river so the CDT has a clean interior reference between the two
    // bank coast chains. Kept typed (rather than raw polylines) so additional
    // metadata (per-point depth, width tapering, debug labels) can be threaded
    // through later without touching call sites.
    public record RiverInput(
        int Id,
        float Width,
        float SourceWidth,
        float[][] ControlPoints);

    public record GenerateResult(
        byte[] Pixels,
        float[] HeightmapF,
        IReadOnlyList<TerrainNode> TerrainNodes,
        IReadOnlyList<PolyNode> PolyNodes,
        IReadOnlyList<CoastNode> CoastNodes,
        int OriginalCoastNodeCount,
        IReadOnlyList<LineString> ConstraintSegs,
        IReadOnlyList<int> ConstraintSegToCellId,
        GeometryCollection Triangulation);

    private record struct SpawnedNode(float Px, float Py, float SpawnPx, float SpawnPy, int ParentIdx);

    public static GenerateResult Generate(
        IReadOnlyDictionary<int, Cell> cells,
        Params p,
        IReadOnlyList<RiverInput>? rivers = null)
    {
        // ── 1. Build terrain nodes ────────────────────────────────────────────
        var landCells = cells.Values.Where(c => Cell.IsDryLand(c.Type)).ToList();
        if (landCells.Count == 0)
            return new GenerateResult(new byte[p.Width * p.Height], new float[p.Width * p.Height], [], [], [], 0, [], [], new GeometryCollection([], new GeometryFactory()));

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
        Logger.Info($"Heightmap roughness — p95raw={p95val:F1} autoNorm={autoNorm:F1} avg={roughness.Values.Average():F3}");

        int minH = landCells.Min(c => c.GeoHeight);
        int maxH = landCells.Max(c => c.GeoHeight);
        if (maxH == minH) maxH = minH + 1;

        // Filter out IsRiverCell cells: their height-0 TerrainNodes pull the
        // rasterized heightmap into a "muddy" stripe along every river, and they
        // would also seed unwanted poly-nodes. Coast walk in phase 3 still sees
        // them (they remain in the cells dict) so the banks stay constrained.
        var terrainNodes = cells.Values
            .Where(c => !c.IsRiverCell)
            .Select(c =>
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

        // Seed centerline TerrainNodes from major-river control points. These
        // act as interior anchors so the CDT triangulates a smooth carved
        // channel between the two bank coast chains. Pinned below water level
        // (CK3WaterLevel - RiverCenterlineDepth) to give the channel visible
        // depth in the rasterized heightmap.
        if (rivers != null && rivers.Count > 0)
        {
            float riverHeight = Math.Max(0f, CK3WaterLevel - p.RiverCenterlineDepth);
            int riverNodeCount = 0;
            int nextSyntheticId = (cells.Count > 0 ? cells.Keys.Max() : 0) + 1;
            foreach (var river in rivers)
            {
                if (river.ControlPoints == null || river.ControlPoints.Length < 2) continue;
                foreach (var cp in river.ControlPoints)
                {
                    if (cp == null || cp.Length < 2) continue;
                    terrainNodes.Add(new TerrainNode(
                        Id:        nextSyntheticId++,
                        Px:        GeoToPixelX(cp[0], p),
                        Py:        GeoToPixelY(cp[1], p),
                        Height:    riverHeight,
                        Roughness: 0f,
                        IsLand:    false,
                        Area:      0));
                    riverNodeCount++;
                }
            }
            if (riverNodeCount > 0)
                Logger.Info($"River centerline TerrainNodes seeded: {riverNodeCount} from {rivers.Count} rivers at height {riverHeight:F1}.");
        }

        // ── 2. Terrain spatial grid ───────────────────────────────────────────
        int terrainCols = Math.Max(1, (int)Math.Sqrt(terrainNodes.Count));
        int terrainRows = Math.Max(1, terrainCols * p.Height / p.Width);
        var terrainGrid = new HeightmapSpatialGrid<int>(p.Width, p.Height, terrainCols, terrainRows);
        for (int i = 0; i < terrainNodes.Count; i++)
            terrainGrid.Add(terrainNodes[i].Px, terrainNodes[i].Py, i);

        // ── 3. Coast nodes via per-sea-body coastline walk ────────────────────
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

        var gfConstr   = new GeometryFactory();
        var pixToFloat = new Dictionary<(int,int), (float px, float py)>();
        var coastNodes            = new List<CoastNode>();
        var constraintSegs        = new List<LineString>();
        var constraintSegToCellId = new List<int>();

        void AddSegment(CoastNode a, CoastNode b, int cellId = -1)
        {
            if (RoundCoord(a.Px) == RoundCoord(b.Px) && RoundCoord(a.Py) == RoundCoord(b.Py)) return;
            constraintSegs.Add(gfConstr.CreateLineString([
                new Coordinate(a.Px, a.Py), new Coordinate(b.Px, b.Py)
            ]));
            constraintSegToCellId.Add(cellId);
        }

        for (int body = 0; body < numSeaBodies; body++)
        {
            var coastAdj     = new Dictionary<(int,int), List<(int,int)>>();
            var coastEdges   = new HashSet<((int,int),(int,int))>();
            var edgeToCellId = new Dictionary<((int,int),(int,int)), int>();

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
                        edgeToCellId[edgeKey] = cell.Id;

                        if (!coastAdj.TryGetValue(kA, out var la)) coastAdj[kA] = la = [];
                        if (!coastAdj.TryGetValue(kB, out var lb)) coastAdj[kB] = lb = [];
                        la.Add(kB); lb.Add(kA);
                    }
                }
            }

            if (coastAdj.Count == 0) continue;

            var (paths, visited) = WalkCoastBody(coastAdj);

            foreach (var (path, isClosed) in paths)
            {
                int n         = path.Count;
                int edgeCount = isClosed ? n : n - 1;
                int pathStart = coastNodes.Count;

                CoastNode? prevNode   = null;
                int        prevCellId = -1;

                for (int i = 0; i < edgeCount; i++)
                {
                    var (pxA, pyA) = pixToFloat[path[i]];
                    var (pxB, pyB) = pixToFloat[path[(i + 1) % n]];
                    var kA = (RoundCoord(pxA), RoundCoord(pyA));
                    var kB = (RoundCoord(pxB), RoundCoord(pyB));
                    var edgeKey = kA.CompareTo(kB) <= 0 ? (kA, kB) : (kB, kA);
                    int cellId = edgeToCellId.TryGetValue(edgeKey, out int cid) ? cid : -1;

                    var nodeA = new CoastNode(pxA, pyA);
                    coastNodes.Add(nodeA);

                    if (prevNode != null)
                        AddSegment(prevNode.Value, nodeA, prevCellId);

                    CoastNode lastNode = nodeA;
                    if (p.MaxCoastEdgePixels > 0)
                    {
                        float dist = MathF.Sqrt((pxA - pxB) * (pxA - pxB) + (pyA - pyB) * (pyA - pyB));
                        if (dist > p.MaxCoastEdgePixels)
                        {
                            int nIns = (int)(dist / p.MaxCoastEdgePixels);
                            for (int k = 1; k <= nIns; k++)
                            {
                                float t = k / (float)(nIns + 1);
                                var ins = new CoastNode(pxA + t * (pxB - pxA), pyA + t * (pyB - pyA));
                                coastNodes.Add(ins);
                                AddSegment(lastNode, ins, cellId);
                                lastNode = ins;
                            }
                        }
                    }

                    prevNode   = lastNode;
                    prevCellId = cellId;
                }

                if (!isClosed)
                {
                    var (pxLast, pyLast) = pixToFloat[path[n - 1]];
                    var termNode = new CoastNode(pxLast, pyLast);
                    coastNodes.Add(termNode);
                    if (prevNode != null) AddSegment(prevNode.Value, termNode, prevCellId);
                }
                else if (prevNode != null)
                {
                    AddSegment(prevNode.Value, coastNodes[pathStart], prevCellId);
                }
            }

            foreach (var k in coastAdj.Keys)
            {
                if (visited.Contains(k)) continue;
                var (fpx, fpy) = pixToFloat[k];
                coastNodes.Add(new CoastNode(fpx, fpy));
            }
        }

        int originalCoastCount = coastNodes.Count;

        // ── 4. Poly-node spawning ─────────────────────────────────────────────
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
                float angle = ((i + (float)rng.NextDouble()) / p.NodesPerCell) * 2f * MathF.PI;
                float r     = radius * MathF.Sqrt((float)rng.NextDouble());
                float nx    = Math.Clamp(t.Px + r * MathF.Cos(angle), 0, p.Width  - 1);
                float ny    = Math.Clamp(t.Py + r * MathF.Sin(angle), 0, p.Height - 1);
                spawnedNodes.Add(new SpawnedNode(nx, ny, nx, ny, ti));
            }
        }

        // ── 5. Relaxation pass ────────────────────────────────────────────────
        float avgPixelArea = terrainNodes.Where(t => t.IsLand)
                                         .Average(t => t.Area * scaleX * scaleY);
        float minSep        = MathF.Sqrt(avgPixelArea / MathF.PI) / MathF.Sqrt(p.NodesPerCell) * 0.9f;
        float terrainMinSep = MathF.Sqrt(avgPixelArea) * p.TerrainToPolySep;

        var terrainPositions = terrainNodes.Select(t => (px: t.Px, py: t.Py)).ToList();
        if (p.RelaxIterations > 0)
            spawnedNodes = Relax(spawnedNodes, terrainPositions, p.RelaxIterations, minSep, terrainMinSep, p.RelaxStep, p.Width, p.Height);

        // ── 6. Cell polygons + coast proximity grid ───────────────────────────
        var gf = new GeometryFactory();

        HeightmapSpatialGrid<int>? coastProxGrid = null;
        if (p.CoastExclusionRadius > 0 && coastNodes.Count > 0)
        {
            int ccols = Math.Max(1, (int)Math.Sqrt(coastNodes.Count));
            int crows = Math.Max(1, ccols * p.Height / p.Width);
            coastProxGrid = new HeightmapSpatialGrid<int>(p.Width, p.Height, ccols, crows);
            for (int ci = 0; ci < coastNodes.Count; ci++)
                coastProxGrid.Add(coastNodes[ci].Px, coastNodes[ci].Py, ci);
        }
        var cellPolygons = new Dictionary<int, Polygon>(cells.Count);
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

            var pt = gf.CreatePoint(new Coordinate(s.Px, s.Py));
            bool? insideLand = null;
            for (int k = 0; k < nearest.Count && insideLand == null; k++)
            {
                var tn = terrainNodes[nearest[k].item];
                if (cellPolygons.TryGetValue(tn.Id, out var poly) && poly.Contains(pt))
                    insideLand = tn.IsLand;
            }
            insideLand ??= baseHeight > CK3WaterLevel;

            if (!insideLand.Value) continue;

            if (coastProxGrid != null)
            {
                var cn1 = coastProxGrid.NearestN(s.Px, s.Py, 1);
                if (cn1.Count > 0 && cn1[0].dist < p.CoastExclusionRadius) continue;
            }

            nodeHeight = Math.Max(nodeHeight, CK3WaterLevel + 1f);

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
        var triangulation = Triangulate(allCoords, constraintSegs);
        AbsorbSteinerPoints(triangulation, combinedCoordIndex, coastNodes, CK3WaterLevel);
        var patches = PatchBayShortcuts(triangulation, coastNodes, originalCoastCount, constraintSegs, constraintSegToCellId, terrainNodes, combinedCoordIndex);
        var heightMap = RasterizeTriangles(triangulation, combinedCoordIndex, p, patches: patches);
        ApplyGaussianBlur(heightMap, p);

        return new GenerateResult(ToBytes(heightMap), heightMap, terrainNodes, polyNodes, coastNodes, originalCoastCount, constraintSegs, constraintSegToCellId, triangulation);
    }

    // ── Relaxation ────────────────────────────────────────────────────────────

    static List<SpawnedNode> Relax(
        List<SpawnedNode> nodes,
        List<(float px, float py)> fixedRepulsors,
        int iterations, float minSep, float terrainMinSep, float step, int width, int height)
    {
        var pos = nodes.Select(n => (px: n.Px, py: n.Py)).ToList();

        int fc = Math.Max(1, (int)Math.Sqrt(fixedRepulsors.Count));
        int fr = Math.Max(1, fc * height / width);
        var fixedGrid = new HeightmapSpatialGrid<int>(width, height, fc, fr);
        for (int i = 0; i < fixedRepulsors.Count; i++)
            fixedGrid.Add(fixedRepulsors[i].px, fixedRepulsors[i].py, i);

        for (int iter = 0; iter < iterations; iter++)
        {
            int cols = Math.Max(1, (int)Math.Sqrt(pos.Count));
            int rows = Math.Max(1, cols * height / width);
            var grid = new HeightmapSpatialGrid<int>(width, height, cols, rows);
            for (int i = 0; i < pos.Count; i++)
                grid.Add(pos[i].px, pos[i].py, i);

            var dx = new float[pos.Count];
            var dy = new float[pos.Count];

            for (int i = 0; i < pos.Count; i++)
            {
                var (px, py) = pos[i];

                foreach (var (dist, j) in grid.NearestN(px, py, 8))
                {
                    if (j == i || dist < 0.001f || dist >= minSep) continue;
                    float overlap = (minSep - dist) * 0.5f;
                    float fx = (px - pos[j].px) / dist * overlap;
                    float fy = (py - pos[j].py) / dist * overlap;
                    dx[i] += fx; dy[i] += fy;
                    dx[j] -= fx; dy[j] -= fy;
                }

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
        var temp = new float[w * h];

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
        IReadOnlyList<LineString>? constraints = null)
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

    static void AbsorbSteinerPoints(
        GeometryCollection triangles,
        Dictionary<(int, int), float> coordIndex,
        List<CoastNode> coastNodes,
        float height)
    {
        foreach (var geom in triangles.Geometries)
        foreach (var v in geom.Boundary.Coordinates)
        {
            var key = (RoundCoord((float)v.X), RoundCoord((float)v.Y));
            if (coordIndex.ContainsKey(key)) continue;
            coordIndex[key] = height;
            coastNodes.Add(new CoastNode((float)v.X, (float)v.Y));
        }
    }

    static Dictionary<(int,int,int,int,int,int), List<Polygon>>
    PatchBayShortcuts(
        GeometryCollection triangles,
        List<CoastNode> coastNodes,
        int originalCoastCount,
        IReadOnlyList<LineString> constraintSegs,
        IReadOnlyList<int>? segToCellId,
        IReadOnlyList<TerrainNode> terrainNodes,
        Dictionary<(int,int), float> coordIndex)
    {
        var patches = new Dictionary<(int,int,int,int,int,int), List<Polygon>>();
        if (coastNodes.Count <= originalCoastCount || constraintSegs.Count == 0) return patches;

        var steinerByKey = new Dictionary<(int,int), CoastNode>(coastNodes.Count - originalCoastCount);
        for (int i = originalCoastCount; i < coastNodes.Count; i++)
            steinerByKey.TryAdd((RoundCoord(coastNodes[i].Px), RoundCoord(coastNodes[i].Py)), coastNodes[i]);
        if (steinerByKey.Count == 0) return patches;

        var edgeToTris = new Dictionary<((int,int),(int,int)), List<(Coordinate v0, Coordinate v1, Coordinate v2)>>();
        foreach (var geom in triangles.Geometries)
        {
            var ring = geom.Boundary.Coordinates;
            if (ring.Length < 3) continue;
            var tv0 = ring[0]; var tv1 = ring[1]; var tv2 = ring[2];
            for (int e = 0; e < 3; e++)
            {
                var ka = (RoundCoord((float)ring[e].X),       RoundCoord((float)ring[e].Y));
                var kb = (RoundCoord((float)ring[(e+1)%3].X), RoundCoord((float)ring[(e+1)%3].Y));
                var edge = ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka);
                if (!edgeToTris.TryGetValue(edge, out var lst)) edgeToTris[edge] = lst = [];
                lst.Add((tv0, tv1, tv2));
            }
        }

        HeightmapSpatialGrid<int>? tGrid = null;
        if (terrainNodes.Count > 0)
        {
            int tc = Math.Max(1, (int)Math.Sqrt(terrainNodes.Count));
            float tw = terrainNodes.Max(t => t.Px), th = terrainNodes.Max(t => t.Py);
            tGrid = new HeightmapSpatialGrid<int>((int)(tw + 1), (int)(th + 1), tc, Math.Max(1, tc / 2));
            for (int ti = 0; ti < terrainNodes.Count; ti++)
                tGrid.Add(terrainNodes[ti].Px, terrainNodes[ti].Py, ti);
        }

        int FindConstraint(float px, float py)
        {
            float bestErr = 2f; int bestIdx = -1;
            for (int ci = 0; ci < constraintSegs.Count; ci++)
            {
                var cs  = constraintSegs[ci].Coordinates;
                float ax = (float)cs[0].X, ay = (float)cs[0].Y;
                float bx = (float)cs[1].X, by = (float)cs[1].Y;
                float dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                if (len2 < 0.01f) continue;
                float t = ((px - ax) * dx + (py - ay) * dy) / len2;
                if (t < -0.01f || t > 1.01f) continue;
                float ex = ax + t * dx - px, ey = ay + t * dy - py;
                float err = MathF.Sqrt(ex * ex + ey * ey);
                if (err < bestErr) { bestErr = err; bestIdx = ci; }
            }
            return bestIdx;
        }

        bool MidpointIsOcean(float px, float py)
        {
            if (tGrid == null) return false;
            var nearest = tGrid.NearestN(px, py, 1);
            return nearest.Count > 0 && !terrainNodes[nearest[0].item].IsLand;
        }

        var gf         = new GeometryFactory();
        int nBayPairs  = 0, nTriPatched = 0;

        foreach (var (edge, trisForEdge) in edgeToTris)
        {
            var (ka, kb) = edge;
            if (!steinerByKey.ContainsKey(ka) || !steinerByKey.ContainsKey(kb)) continue;

            var sa = steinerByKey[ka];
            var sb = steinerByKey[kb];
            int ca = FindConstraint(sa.Px, sa.Py);
            int cb = FindConstraint(sb.Px, sb.Py);
            if (ca < 0 || cb < 0 || ca == cb) continue;

            bool sameCellId = segToCellId != null &&
                              ca < segToCellId.Count && cb < segToCellId.Count &&
                              segToCellId[ca] >= 0 && segToCellId[ca] == segToCellId[cb];
            if (sameCellId) continue;

            float midX = (sa.Px + sb.Px) / 2f, midY = (sa.Py + sb.Py) / 2f;
            if (!MidpointIsOcean(midX, midY)) continue;

            nBayPairs++;
            var mCoord = new Coordinate(midX, midY);
            coordIndex.TryAdd((RoundCoord(midX), RoundCoord(midY)), 0f);

            var coordA = new Coordinate(sa.Px, sa.Py);
            var coordB = new Coordinate(sb.Px, sb.Py);

            foreach (var (tv0, tv1, tv2) in trisForEdge)
            {
                Coordinate? third = null;
                foreach (var v in new[] { tv0, tv1, tv2 })
                {
                    var vk = (RoundCoord((float)v.X), RoundCoord((float)v.Y));
                    if (vk != ka && vk != kb) { third = v; break; }
                }
                if (third == null) continue;

                var origKey = TriangleKey(tv0, tv1, tv2);
                try
                {
                    var t1 = gf.CreatePolygon([coordA, mCoord, third, coordA]);
                    var t2 = gf.CreatePolygon([mCoord, coordB, third, mCoord]);
                    patches[origKey] = [t1, t2];
                    nTriPatched++;
                }
                catch { /* degenerate — skip */ }
            }
        }

        if (nBayPairs > 0)
            Logger.Info($"Heightmap: patched {nBayPairs} bay-shortcut(s), {nTriPatched} triangle(s) → {nTriPatched * 2} fan triangle(s)");

        return patches;
    }

    static float[] RasterizeTriangles(
        GeometryCollection triangles,
        Dictionary<(int, int), float> coordIndex,
        Params p,
        float clampMax = 255f,
        Dictionary<(int,int,int,int,int,int), List<Polygon>>? patches = null)
    {
        var heightMap = new float[p.Width * p.Height];
        foreach (var geom in triangles.Geometries)
        {
            var ring = geom.Boundary.Coordinates;
            if (ring.Length < 3) continue;
            var v0 = ring[0]; var v1 = ring[1]; var v2 = ring[2];

            if (patches != null)
            {
                var key = TriangleKey(v0, v1, v2);
                if (patches.TryGetValue(key, out var patchTris))
                {
                    foreach (var pt in patchTris)
                    {
                        var pr = pt.Boundary.Coordinates;
                        if (pr.Length < 3) continue;
                        RasterizeTriangle(pr[0], pr[1], pr[2],
                            Lookup(coordIndex, pr[0]), Lookup(coordIndex, pr[1]), Lookup(coordIndex, pr[2]),
                            heightMap, p.Width, p.Height, clampMax);
                    }
                    continue;
                }
            }

            RasterizeTriangle(v0, v1, v2,
                Lookup(coordIndex, v0), Lookup(coordIndex, v1), Lookup(coordIndex, v2),
                heightMap, p.Width, p.Height, clampMax);
        }
        return heightMap;
    }

    // ── Core rasterization ────────────────────────────────────────────────────

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

    static (int,int,int,int,int,int) TriangleKey(Coordinate v0, Coordinate v1, Coordinate v2)
    {
        var pts = new (int x, int y)[]
        {
            (RoundCoord((float)v0.X), RoundCoord((float)v0.Y)),
            (RoundCoord((float)v1.X), RoundCoord((float)v1.Y)),
            (RoundCoord((float)v2.X), RoundCoord((float)v2.Y))
        };
        Array.Sort(pts, (a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return (pts[0].x, pts[0].y, pts[1].x, pts[1].y, pts[2].x, pts[2].y);
    }

    static float GeoToPixelX(float lon, Params p) => (lon - p.LonW) / p.LonT * p.Width;
    static float GeoToPixelY(float lat, Params p) => p.Height - (lat - p.LatS) / p.LatT * p.Height;

    static (List<(List<(int,int)> nodes, bool isClosed)> paths, HashSet<(int,int)> visited)
    WalkCoastBody(Dictionary<(int,int), List<(int,int)>> adj)
    {
        var visited = new HashSet<(int,int)>();
        var paths   = new List<(List<(int,int)>, bool)>();

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
            bool closed = path.Count >= 3 &&
                          adj.TryGetValue(path[^1], out var lastNbrs) &&
                          lastNbrs.Contains(start);
            if (path.Count >= 2 || (path.Count == 1 && closed)) paths.Add((path, closed));
        }

        foreach (var k in adj.Keys)
        {
            if (adj[k].Count == 1) WalkFrom(k);
        }
        foreach (var k in adj.Keys)
            WalkFrom(k);

        return (paths, visited);
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
