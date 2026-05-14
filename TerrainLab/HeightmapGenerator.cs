using Converter.Lemur.Entities;
using Converter.Lemur.Writers;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace TerrainLab;

// Thin Lab shim: delegates algorithm to HeightmapAlgorithm in the Converter, then
// runs Lab-only diagnostics on the result. Program.cs accesses algorithm output via
// result.Core; diagnostic outputs (Connectivity, CascadingNodes) are on LabResult directly.
static class HeightmapGenerator
{
    public const int CK3WaterLevel = HeightmapAlgorithm.CK3WaterLevel;

    public record CoastConnectivity(
        IReadOnlyList<int>  Connections,
        IReadOnlyList<bool> IsHullNode);

    public record LabResult(
        HeightmapAlgorithm.GenerateResult Core,
        CoastConnectivity Connectivity,
        IReadOnlyList<CoastNode> CascadingNodes);

    public static LabResult Generate(
        IReadOnlyDictionary<int, Cell> cells,
        HeightmapAlgorithm.Params p,
        IReadOnlyList<HeightmapAlgorithm.RiverInput>? rivers = null)
    {
        var core        = HeightmapAlgorithm.Generate(cells, p, rivers);
        var cascading   = AssertNoSteinerSteinerEdges(core.Triangulation, core.CoastNodes, core.OriginalCoastNodeCount, core.ConstraintSegs, core.ConstraintSegToCellId, core.TerrainNodes);
        var connectivity = CheckCoastConnectivity(core.Triangulation, core.CoastNodes);
        return new LabResult(core, connectivity, cascading);
    }

    // ── Lab-only: bay-shortcut / Steiner diagnostic ───────────────────────────
    // Returns the list of Steiner nodes involved in BAY-SHORTCUT edges for visualization.
    static List<CoastNode> AssertNoSteinerSteinerEdges(
        GeometryCollection triangles,
        IReadOnlyList<CoastNode> coastNodes,
        int originalCount,
        IReadOnlyList<LineString>? constraintSegs = null,
        IReadOnlyList<int>? segToCellId = null,
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

        SpatialGrid<int>? tGrid = null;
        if (terrainNodes != null && terrainNodes.Count > 0)
        {
            int tc = Math.Max(1, (int)Math.Sqrt(terrainNodes.Count));
            float tw = terrainNodes.Max(t => t.Px), th = terrainNodes.Max(t => t.Py);
            tGrid = new SpatialGrid<int>((int)(tw + 1), (int)(th + 1), tc, Math.Max(1, tc / 2));
            for (int ti = 0; ti < terrainNodes.Count; ti++)
                tGrid.Add(terrainNodes[ti].Px, terrainNodes[ti].Py, ti);
        }

        var bayKeys = new HashSet<(int,int)>();

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

            bool SameCellId(int ca, int cb) =>
                segToCellId != null &&
                ca >= 0 && cb >= 0 &&
                ca < segToCellId.Count && cb < segToCellId.Count &&
                segToCellId[ca] >= 0 && segToCellId[ca] == segToCellId[cb];

            int nSameSeg = 0, nBay = 0, nCape = 0, nUnknown = 0, nSameCell = 0;
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
                    bayKeys.Add(ka); bayKeys.Add(kb);
                    Console.WriteLine($"  [UNKNOWN      ] edge-dist={dist:F1}px  mid=({midX:F0},{midY:F0})");
                }
                else if (ca == cb)
                {
                    nSameSeg++;
                    Console.WriteLine($"  [same-seg     ] edge-dist={dist:F1}px  seg-len={SegLen(ca):F1}px  (benign)");
                }
                else if (SameCellId(ca, cb))
                {
                    nSameCell++;
                    Console.WriteLine($"  [same-cell    ] edge-dist={dist:F1}px  mid=({midX:F0},{midY:F0})  (benign)");
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
                    Console.WriteLine($"  [cape-clip    ] edge-dist={dist:F1}px  mid=({midX:F0},{midY:F0})  (benign)");
                }
            }

            if (nBay > 0)
            {
                bayDists.Sort();
                Console.WriteLine($"  BAY-SHORTCUT SUMMARY: {nBay} artifact(s)  min={bayDists[0]:F1}px  avg={bayDists.Average():F1}px  max={bayDists[^1]:F1}px");
                Console.WriteLine($"  (also: {nSameSeg} same-seg, {nSameCell} same-cell, {nCape} cape-clip — all benign)");
            }
            else
            {
                Console.WriteLine($"  Bay-shortcut OK — {nSameSeg} same-seg + {nSameCell} same-cell + {nCape} cape-clip (all benign)");
            }
        }
        else
        {
            foreach (var (ka, kb) in pairList) { bayKeys.Add(ka); bayKeys.Add(kb); }
            Console.WriteLine($"  {seen.Count} Steiner-Steiner edge(s) — pass constraintSegs for bay/cape classification");
        }

        return bayKeys.Select(k => steinerByKey[k]).ToList();
    }

    // ── Lab-only: coast connectivity check ───────────────────────────────────
    static CoastConnectivity CheckCoastConnectivity(GeometryCollection triangles, IReadOnlyList<CoastNode> coastNodes)
    {
        int n = coastNodes.Count;
        var connections = new int[n];
        var isHull      = new bool[n];

        if (n == 0) return new CoastConnectivity(connections, isHull);

        var keyToIdx = new Dictionary<(int, int), int>(n);
        for (int i = 0; i < n; i++)
            keyToIdx[(RoundCoord(coastNodes[i].Px), RoundCoord(coastNodes[i].Py))] = i;
        var coastKeys = keyToIdx.Keys.ToHashSet();

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

        var seenCoastEdges = new HashSet<((int,int),(int,int))>();
        foreach (var (edge, cnt) in edgeCount)
        {
            var (a, b) = edge;
            bool aIsCoast = coastKeys.Contains(a);
            bool bIsCoast = coastKeys.Contains(b);

            if (cnt == 1)
            {
                if (aIsCoast && keyToIdx.TryGetValue(a, out int ai)) isHull[ai] = true;
                if (bIsCoast && keyToIdx.TryGetValue(b, out int bi)) isHull[bi] = true;
            }

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
            Console.WriteLine($"Coast connectivity OK - all {n} nodes =2 coast-coast edges");

        return new CoastConnectivity(connections, isHull);
    }

    // ── Lab-only: roughness diagnostic raster ────────────────────────────────
    public static float[] RasterizeRoughness(IReadOnlyList<TerrainNode> terrainNodes, HeightmapAlgorithm.Params p)
    {
        var coordIndex = BuildCoordIndex(terrainNodes.Select(t => (t.Px, t.Py, t.Roughness)));
        return Rasterize(coordIndex, terrainNodes.Select(t => new Coordinate(t.Px, t.Py)), p, clampMax: 1f);
    }

    static float[] Rasterize(
        Dictionary<(int, int), float> coordIndex,
        IEnumerable<Coordinate> points,
        HeightmapAlgorithm.Params p,
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

    static int RoundCoord(float v) => (int)Math.Round(v);
}
