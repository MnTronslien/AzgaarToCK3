using Converter.Lemur.Entities;

namespace Converter.Lemur.Straits;

/// <summary>The four tuning knobs (PLAN_straits.md). All distances are in CK3 image-pixel units.</summary>
public sealed record StraitParams(
    double MinimumSelfSeparation, // overland path-cost in pixels (density-independent), not hops
    double MaxDistance,
    double MinimumClearance,
    int OceanMinimumArea,
    int SampleCount = 0); // 0 ⇒ auto from crossing length

/// <summary>
/// Decides where sea straits go (PLAN_straits.md). Works in projected CK3 image-pixel space via a
/// caller-supplied geo→pixel projection, so the same code serves the full converter (with a Map)
/// and the TerrainLab --strait-map tuning harness (cells only).
///
/// Barony-independent core: landmass + water floodfills, candidate enumeration, the over-ocean
/// validity gate, rules 1/2/3. The barony tail (rule 0 + collapse per barony pair) only ever
/// *removes* straits and runs only when <c>collapseByBarony</c> is set.
/// </summary>
public static class StraitGenerator
{
    public enum CellClass { Land, Ocean, Lake, River }

    private static bool IsLandCell(Cell c) => Cell.IsDryLand(c.Type) && !c.IsRiverCell;
    private static bool IsWaterCell(Cell c) => !Cell.IsDryLand(c.Type) && !c.IsRiverCell;

    /// <summary>
    /// Classify every cell as Land / Ocean / Lake / River. Ocean vs lake is decided by summed
    /// water-body area against <paramref name="oceanMinimumArea"/> — Azgaar's one-ocean limit makes
    /// the authored feature type unreliable, so we floodfill and measure (PLAN_straits.md).
    /// </summary>
    public static Dictionary<int, CellClass> Classify(IReadOnlyDictionary<int, Cell> cells, int oceanMinimumArea)
    {
        var water = CellGraph.ConnectedComponents(cells, IsWaterCell);
        var compArea = new Dictionary<int, long>();
        foreach (var (id, comp) in water)
            compArea[comp] = compArea.GetValueOrDefault(comp) + Math.Max(0, cells[id].Area);
        var oceanComps = new HashSet<int>(compArea.Where(kv => kv.Value >= oceanMinimumArea).Select(kv => kv.Key));

        var result = new Dictionary<int, CellClass>(cells.Count);
        foreach (var (id, c) in cells)
        {
            if (c.IsRiverCell) result[id] = CellClass.River;
            else if (IsLandCell(c)) result[id] = CellClass.Land;
            else if (water.TryGetValue(id, out var comp)) result[id] = oceanComps.Contains(comp) ? CellClass.Ocean : CellClass.Lake;
            else result[id] = CellClass.Land; // unreachable: every non-land non-river cell is in a water component
        }
        return result;
    }

    public static List<Strait> Generate(
        IReadOnlyDictionary<int, Cell> cells,
        Func<GeoPoint, ImagePixel> project,
        StraitParams p,
        bool collapseByBarony)
    {
        // ── Projected centroids + bounding box ──────────────────────────────
        var px = new Dictionary<int, double>(cells.Count);
        var py = new Dictionary<int, double>(cells.Count);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (id, cell) in cells)
        {
            var pt = project(CentroidGeo(cell));
            px[id] = pt.X; py[id] = pt.Y;
            if (pt.X < minX) minX = pt.X;
            if (pt.X > maxX) maxX = pt.X;
            if (pt.Y < minY) minY = pt.Y;
            if (pt.Y > maxY) maxY = pt.Y;
        }
        double pitch = Math.Sqrt(Math.Max(1.0, (maxX - minX) * (maxY - minY)) / Math.Max(1, cells.Count));

        // ── Classification + landmass labelling ─────────────────────────────
        var cls = Classify(cells, p.OceanMinimumArea);
        bool IsOcean(int id) => cls.TryGetValue(id, out var k) && k == CellClass.Ocean;
        var landmass = CellGraph.ConnectedComponents(cells, IsLandCell);

        // ── Coastal land cells (land touching ocean) ────────────────────────
        var coastal = new List<Cell>();
        foreach (var id in cells.Keys.OrderBy(x => x))
        {
            var c = cells[id];
            if (!IsLandCell(c)) continue;
            if (c.Neighbors.Any(IsOcean)) coastal.Add(c);
        }

        // ── Spatial grids ───────────────────────────────────────────────────
        var coastalGrid = new StraitSpatialGrid<Cell>(minX, minY, maxX, maxY, Math.Max(pitch, p.MaxDistance));
        foreach (var c in coastal) coastalGrid.Add(px[c.Id], py[c.Id], c);
        var allGrid = new StraitSpatialGrid<Cell>(minX, minY, maxX, maxY, pitch);
        foreach (var (id, c) in cells) allGrid.Add(px[id], py[id], c);

        // ── Enumerate + filter candidates ───────────────────────────────────
        var seen = new HashSet<(int, int)>();
        var candidates = new List<Strait>();
        foreach (var a in coastal)
        {
            double ax = px[a.Id], ay = py[a.Id];
            foreach (var b in coastalGrid.Within(ax, ay, p.MaxDistance))
            {
                if (b.Id == a.Id) continue;
                var key = a.Id < b.Id ? (a.Id, b.Id) : (b.Id, a.Id);
                if (!seen.Add(key)) continue;

                // Rule 0 — converter mode: both endpoints must be distinct baronies. Drops crossings
                // to non-barony land (wasteland provinces, which have no holdable barony) and same-barony
                // pairs, so map.Straits matches exactly what AdjacenciesCsvWriter can emit. Cells-only
                // mode (harness) has null Province and skips this — it shows every geometric candidate.
                if (collapseByBarony)
                {
                    if (a.Province is not Barony || b.Province is not Barony) continue;
                    if (ReferenceEquals(a.Province, b.Province)) continue;
                }

                double bx = px[b.Id], by = py[b.Id];
                double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                if (len > p.MaxDistance) continue; // Rule 2

                // Validity gate — segment must lie over ocean, never clip a third landmass.
                if (!TryValidate(a, b, ax, ay, bx, by, len, pitch, p, landmass, IsOcean, cls, allGrid, out var through))
                    continue;

                int la = landmass.GetValueOrDefault(a.Id, -1);
                int lb = landmass.GetValueOrDefault(b.Id, -2);

                // Rule 1 — same-landmass pairs joined by a short overland path (pixel cost, density-
                // independent) are too close to warrant a strait. Different landmasses → unreachable → kept.
                if (la == lb && CellGraph.ReachableWithinCost(
                        cells, a.Id, b.Id, p.MinimumSelfSeparation, id => (px[id], py[id]), IsLandCell))
                    continue;

                candidates.Add(new Strait
                {
                    FromCell = a,
                    ToCell = b,
                    ThroughCell = through,
                    Length = len,
                    MidX = (ax + bx) / 2.0,
                    MidY = (ay + by) / 2.0,
                    LandmassA = Math.Min(la, lb),
                    LandmassB = Math.Max(la, lb),
                });
            }
        }

        // ── Barony collapse — one (shortest) strait per barony pair ──────────
        IEnumerable<Strait> survivors = candidates;
        if (collapseByBarony)
        {
            survivors = candidates
                .GroupBy(BaronyPairKey)
                .Select(g => g.OrderBy(s => s.Length).ThenBy(s => s.FromCell.Id).ThenBy(s => s.ToCell.Id).First());
        }

        // ── Rule 3 — shortest-first, with midpoint clearance per landmass pair ──
        var ordered = survivors
            .OrderBy(s => s.Length).ThenBy(s => s.FromCell.Id).ThenBy(s => s.ToCell.Id)
            .ToList();
        var accepted = new List<Strait>();
        var acceptedByPair = new Dictionary<(int, int), List<Strait>>();
        double clr2 = p.MinimumClearance * p.MinimumClearance;
        foreach (var s in ordered)
        {
            var lp = (s.LandmassA, s.LandmassB);
            if (acceptedByPair.TryGetValue(lp, out var list))
            {
                bool tooClose = false;
                foreach (var o in list)
                {
                    double dx = o.MidX - s.MidX, dy = o.MidY - s.MidY;
                    if (dx * dx + dy * dy < clr2) { tooClose = true; break; }
                }
                if (tooClose) continue;
            }
            accepted.Add(s);
            if (!acceptedByPair.TryGetValue(lp, out var existing)) { existing = new List<Strait>(); acceptedByPair[lp] = existing; }
            existing.Add(s);
        }
        return accepted;
    }

    /// <summary>
    /// Sample interior points of the A→B segment. Every sample's containing cell (nearest centroid,
    /// exact on the Voronoi mesh) must be ocean, or our own shore (landmass A/B); a lake, river, or
    /// third-landmass cell fails. At least one sample must be ocean. Through = ocean cell nearest the midpoint.
    /// </summary>
    private static bool TryValidate(
        Cell a, Cell b, double ax, double ay, double bx, double by, double len, double pitch,
        StraitParams p, Dictionary<int, int> landmass, Func<int, bool> isOcean,
        Dictionary<int, CellClass> cls, StraitSpatialGrid<Cell> allGrid, out Cell through)
    {
        through = null!;
        int la = landmass.GetValueOrDefault(a.Id, -1);
        int lb = landmass.GetValueOrDefault(b.Id, -2);
        int samples = p.SampleCount > 0 ? p.SampleCount : Math.Max(4, (int)(len / Math.Max(1e-6, pitch)) * 2);

        double mx = (ax + bx) / 2.0, my = (ay + by) / 2.0;
        bool anyOcean = false;
        Cell? bestOcean = null;
        double bestOceanMidD2 = double.MaxValue;

        for (int i = 1; i < samples; i++)
        {
            double t = (double)i / samples;
            double sx = ax + (bx - ax) * t, sy = ay + (by - ay) * t;
            var nearest = allGrid.Nearest(sx, sy);
            if (nearest == null) return false;

            var kind = cls.GetValueOrDefault(nearest.Id, CellClass.Land);
            if (kind == CellClass.Ocean)
            {
                anyOcean = true;
                double dx = sx - mx, dy = sy - my, d2 = dx * dx + dy * dy;
                if (d2 < bestOceanMidD2) { bestOceanMidD2 = d2; bestOcean = nearest; }
            }
            else if (kind == CellClass.Land)
            {
                int lm = landmass.GetValueOrDefault(nearest.Id, -3);
                if (lm != la && lm != lb) return false; // third landmass clipped
                // else: our own shore near an endpoint — allowed
            }
            else
            {
                return false; // lake or river — not a sea strait
            }
        }

        if (!anyOcean || bestOcean == null) return false;
        through = bestOcean;
        return true;
    }

    private static (int, int) BaronyPairKey(Strait s)
    {
        int a = (s.FromCell.Province as Barony)?.Id ?? s.FromCell.Id;
        int b = (s.ToCell.Province as Barony)?.Id ?? s.ToCell.Id;
        return a <= b ? (a, b) : (b, a);
    }

    internal static GeoPoint CentroidGeo(Cell c)
    {
        var ring = c.GeoDataCoordinates;
        double sx = 0, sy = 0;
        for (int i = 0; i < ring.Length; i++) { sx += ring[i][0]; sy += ring[i][1]; }
        int n = Math.Max(1, ring.Length);
        return new GeoPoint(sx / n, sy / n);
    }
}
