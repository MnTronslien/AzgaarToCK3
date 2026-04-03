using NetTopologySuite.Geometries;
using Converter.Lemur.Entities;

namespace Converter.Lemur.Rivers
{
    /// <summary>
    /// Inserts major river cells and provinces into the map using a clean 4-phase pipeline:
    ///   1. Carve  — subtract ribbon from land cells; update neighbor references
    ///   2. Build  — construct river cells from ribbon slices (independent of land cells)
    ///   3. Wire   — bidirectionally link river cells ↔ land cells and each other
    ///   4. Provinces — group ordered river cells into MajorRiverProvince objects
    /// </summary>
    public static class MajorRiverInserter
    {
        private static readonly GeometryFactory GeoFactory = new GeometryFactory();

        /// <summary>Number of control points per river cell slice (overlap by 1 for continuity).</summary>
        private const int ControlPointsPerRiverCell = 4;

        public static void InsertMajorRivers(Map map, float majorThreshold)
        {
            var majorRivers = map.Rivers!
                .Where(r => r.IsMajor(majorThreshold))
                .ToList();

            if (majorRivers.Count == 0)
            {
                Logger.Info("No major rivers to process (check MajorRiverThreshold in settings).");
                return;
            }

            Logger.Info($"Inserting major rivers: {majorRivers.Count} rivers above discharge threshold {majorThreshold}.");

            // Phase 1: carve ribbon geometry from land cells
            CarveRibbonsFromLandCells(map, majorRivers, out var cellReplacements);
            Logger.Info($"Phase 1 complete: {cellReplacements.Count} cells split.");

            // Phase 2: build river cells from ribbon slices
            var riverCellIds = BuildRiverCells(map, majorRivers);
            int totalRiverCells = riverCellIds.Values.Sum(l => l.Count);
            Logger.Info($"Phase 2 complete: {totalRiverCells} river cells created.");
            AssertNoRiverCellOverlap(map, riverCellIds);

            // Phase 3: wire river cell neighbors
            WireRiverNeighbors(map, riverCellIds);
            Logger.Info("Phase 3 complete: river neighbors wired.");

            // Phase 4: build MajorRiverProvince objects
            BuildRiverProvinces(map, riverCellIds);
            Logger.Info($"Phase 4 complete: {map.MajorRiverProvinces.Count} river provinces built.");
        }

        // ─── Phase 1 ──────────────────────────────────────────────────────────────

        private static void CarveRibbonsFromLandCells(
            Map map,
            List<River> rivers,
            out Dictionary<int, List<int>> cellReplacements)
        {
            cellReplacements = new Dictionary<int, List<int>>();
            int nextCellId = map.Cells!.Keys.Max() + 1;

            foreach (var river in rivers)
            {
                if (river.ControlPoints == null || river.ControlPoints.Count < 2 || river.Width <= 0)
                {
                    Logger.Warning($"River '{river.Name}' skipped: missing control points or zero width.");
                    continue;
                }

                var ribbon = BuildRibbon(river.ControlPoints, river.Width);

                // Snapshot before modifying — only dry-land non-river cells
                var candidates = map.Cells.Values
                    .Where(c => Cell.IsDryLand(c.Type) && !c.IsRiverCell)
                    .Select(c => (cell: c, poly: CellToPolygon(c)))
                    .Where(t => t.poly != null && ribbon.Intersects(t.poly))
                    .ToList();

                int hitCount = 0;
                foreach (var (cell, cellPoly) in candidates)
                {
                    if (!map.Cells.ContainsKey(cell.Id)) continue; // already replaced by earlier river

                    var remainder = cellPoly!.Difference(ribbon);
                    if (remainder == null || remainder.IsEmpty)
                    {
                        // Fully engulfed — leave as land (protects burgs, avoids geometry loss)
                        Logger.Debug($"  Cell {cell.Id} fully engulfed by '{river.Name}' ribbon — kept as land.");
                        continue;
                    }

                    var pieces = remainder is MultiPolygon mp
                        ? mp.Geometries.OfType<Polygon>().ToList()
                        : remainder is Polygon p ? new List<Polygon> { p }
                        : null;

                    if (pieces == null || pieces.Count == 0) continue;

                    if (pieces.Count == 1)
                    {
                        // Clipped: cell geometry trimmed in-place; no ID change, no neighbor update needed
                        cell.GeoDataCoordinates = GeomToCoordinates(pieces[0]);
                        hitCount++;
                    }
                    else
                    {
                        // Split: create one new cell per piece, remove original
                        var newIds = new List<int>();
                        bool burgAssigned = false;

                        for (int k = 0; k < pieces.Count; k++)
                        {
                            int newId = nextCellId++;
                            var newCell = CloneLandCell(cell, pieces[k], newId);

                            // Burg goes to the piece whose polygon contains the burg position
                            if (cell.Burg != null && !burgAssigned)
                            {
                                var burgPt = GeoFactory.CreatePoint(
                                    new Coordinate(cell.Burg.Position.X, cell.Burg.Position.Y));
                                if (pieces[k].Contains(burgPt) || pieces[k].Distance(burgPt) < 1e-6)
                                {
                                    newCell.Burg = cell.Burg;
                                    newCell.Burg.Cell = newCell;
                                    burgAssigned = true;
                                }
                            }

                            map.Cells[newId] = newCell;
                            newIds.Add(newId);
                        }

                        // If no piece contained the burg (floating-point edge case), give it to the largest
                        if (cell.Burg != null && !burgAssigned)
                        {
                            var largestId = newIds
                                .OrderByDescending(id => CellToPolygon(map.Cells[id])?.Area ?? 0)
                                .First();
                            map.Cells[largestId].Burg = cell.Burg;
                            map.Cells[largestId].Burg!.Cell = map.Cells[largestId];
                            Logger.Debug($"  Burg '{cell.Burg.Name}' fallback-assigned to largest split piece {largestId}.");
                        }

                        map.Cells.Remove(cell.Id);
                        cellReplacements[cell.Id] = newIds;
                        hitCount++;
                    }
                }

                Logger.Debug($"  River '{river.Name}': {hitCount} land cells carved.");
            }

            UpdateAllNeighborReferences(map, cellReplacements);
        }

        // ─── Phase 2 ──────────────────────────────────────────────────────────────

        private static Dictionary<River, List<int>> BuildRiverCells(Map map, List<River> rivers)
        {
            var result = new Dictionary<River, List<int>>();
            int nextCellId = map.Cells!.Keys.Max() + 1;

            foreach (var river in rivers)
            {
                if (river.ControlPoints == null || river.ControlPoints.Count < 2) continue;

                var cps = river.ControlPoints;
                var fullRibbon = BuildRibbon(cps, river.Width);
                var ids = new List<int>();
                Geometry? claimedArea = null; // union of all accepted slice polygons so far

                for (int start = 0; start + 1 < cps.Count; start += ControlPointsPerRiverCell - 1)
                {
                    var window = cps.Skip(start).Take(ControlPointsPerRiverCell).ToList();
                    if (window.Count < 2) continue;

                    var sliceRibbon = BuildRibbon(window, river.Width);
                    // Clip to the full ribbon so slices don't bleed outside the river footprint
                    Geometry sliceGeom = sliceRibbon.Intersection(fullRibbon);
                    if (sliceGeom == null || sliceGeom.IsEmpty) continue;

                    // Subtract already-claimed area to guarantee zero overlap with previous slices
                    if (claimedArea != null)
                    {
                        sliceGeom = sliceGeom.Difference(claimedArea);
                        if (sliceGeom == null || sliceGeom.IsEmpty) continue;
                    }

                    var slicePoly = sliceGeom is Polygon sp ? sp
                        : sliceGeom is MultiPolygon smp
                            ? (Polygon)smp.Geometries.OrderByDescending(g => g.Area).First()
                            : null;
                    if (slicePoly == null) continue;

                    claimedArea = claimedArea == null ? slicePoly : claimedArea.Union(slicePoly);

                    int id = nextCellId++;
                    map.Cells[id] = new Cell
                    {
                        Id = id,
                        GeoDataCoordinates = GeomToCoordinates(slicePoly),
                        Neighbors = Array.Empty<int>(),
                        IsRiverCell = true,
                        Culture = 0,
                        Religion = 0,
                        Biome = 0,
                        Area = 0,
                        DistanceToCoast = 0,
                        State = 0,
                        AzProvince = 0,
                    };
                    ids.Add(id);
                }

                result[river] = ids;
                Logger.Debug($"  River '{river.Name}': {ids.Count} river cells built from {cps.Count} control points.");
            }

            return result;
        }

        // ─── Phase 3 ──────────────────────────────────────────────────────────────

        private static void WireRiverNeighbors(Map map, Dictionary<River, List<int>> riverCellIds)
        {
            // Build polygon cache once — CellToPolygon is the expensive step
            var polyCache = map.Cells.ToDictionary(
                kv => kv.Key,
                kv => CellToPolygon(kv.Value));

            foreach (var (river, ids) in riverCellIds)
            {
                if (ids.Count == 0) continue;
                var sameRiverSet = new HashSet<int>(ids);

                foreach (int rcId in ids)
                {
                    if (!map.Cells.TryGetValue(rcId, out var rc)) continue;
                    if (!polyCache.TryGetValue(rcId, out var rcPoly) || rcPoly == null) continue;

                    foreach (var (otherId, otherPoly) in polyCache)
                    {
                        if (otherId == rcId || otherPoly == null) continue;
                        // Skip river cells from other rivers — don't cross-wire different rivers
                        if (map.Cells.TryGetValue(otherId, out var other) &&
                            other.IsRiverCell && !sameRiverSet.Contains(otherId))
                            continue;

                        if (!rcPoly.Intersects(otherPoly)) continue;
                        var shared = rcPoly.Intersection(otherPoly);
                        if (shared == null || shared.IsEmpty || shared.Dimension < Dimension.Curve) continue;

                        if (!rc.Neighbors.Contains(otherId))
                            rc.Neighbors = rc.Neighbors.Append(otherId).ToArray();
                        if (other != null && !other.Neighbors.Contains(rcId))
                            other.Neighbors = other.Neighbors.Append(rcId).ToArray();
                    }
                }
            }
        }

        // ─── Phase 4 ──────────────────────────────────────────────────────────────

        private static void BuildRiverProvinces(Map map, Dictionary<River, List<int>> riverCellIds)
        {
            int nextProvinceId = 1;
            int step = Math.Max(1, Settings.Instance.RiverProvinceCellCount);

            foreach (var (river, ids) in riverCellIds)
            {
                if (ids.Count == 0) continue;

                // Sort cells upstream→downstream by proximity to control points
                var ordered = ids
                    .Where(id => map.Cells.ContainsKey(id))
                    .Select(id => map.Cells[id])
                    .OrderBy(c => ClosestControlPointIndex(c, river.ControlPoints!))
                    .ToList();

                for (int i = 0; i < ordered.Count; i += step)
                {
                    var bucket = ordered.Skip(i).Take(step).ToList();
                    var provinceName = $"{river.Name} {i / step + 1}";
                    var province = new MajorRiverProvince(nextProvinceId++, bucket, provinceName, river.Id);
                    map.MajorRiverProvinces.Add(province);
                    Logger.Debug($"  Province '{provinceName}': {bucket.Count} cells, id {province.Id}");
                }
            }
        }

        // ─── Assertions ───────────────────────────────────────────────────────────

        /// <summary>
        /// Checks that no two river cells overlap in geometry (area intersection &gt; epsilon).
        /// Logs a warning per violation; does not throw.
        /// </summary>
        private static void AssertNoRiverCellOverlap(Map map, Dictionary<River, List<int>> riverCellIds)
        {
            const double epsilon = 1e-4;
            int violations = 0;

            // Gather all river cells with their polygons
            var riverCells = riverCellIds
                .SelectMany(kv => kv.Value.Select(id => (river: kv.Key, id)))
                .Where(t => map.Cells.ContainsKey(t.id))
                .Select(t => (t.river, t.id, poly: CellToPolygon(map.Cells[t.id])))
                .Where(t => t.poly != null)
                .ToList();

            for (int i = 0; i < riverCells.Count; i++)
            {
                for (int j = i + 1; j < riverCells.Count; j++)
                {
                    var (riverA, idA, polyA) = riverCells[i];
                    var (riverB, idB, polyB) = riverCells[j];
                    if (!polyA!.Intersects(polyB)) continue;
                    var overlap = polyA.Intersection(polyB!);
                    if (overlap == null || overlap.IsEmpty || overlap.Area <= epsilon) continue;

                    violations++;
                    Logger.Warning($"  [Assert] River cell overlap: cell {idA} ('{riverA.Name}') ∩ cell {idB} ('{riverB.Name}') area={overlap.Area:F4}");
                }
            }

            if (violations == 0)
                Logger.Info("Phase 2 assertion OK: no river cell geometry overlaps.");
            else
                Logger.Warning($"Phase 2 assertion FAILED: {violations} river cell overlap(s) detected.");
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static int ClosestControlPointIndex(Cell cell, List<float[]> cps)
        {
            var cx = cell.GeoDataCoordinates!.Average(p => (double)p[0]);
            var cy = cell.GeoDataCoordinates!.Average(p => (double)p[1]);
            return cps
                .Select((p, i) => (dist: Math.Pow((double)p[0] - cx, 2) + Math.Pow((double)p[1] - cy, 2), i))
                .MinBy(t => t.dist).i;
        }

        private static Geometry BuildRibbon(List<float[]> controlPoints, float width)
        {
            var coords = controlPoints.Select(p => new Coordinate((double)p[0], (double)p[1])).ToArray();
            var line = GeoFactory.CreateLineString(coords);
            return line.Buffer(width / 2.0);
        }

        private static Polygon? CellToPolygon(Cell cell)
        {
            if (cell.GeoDataCoordinates == null || cell.GeoDataCoordinates.Length < 3)
                return null;
            try
            {
                var coords = cell.GeoDataCoordinates
                    .Select(p => new Coordinate(p[0], p[1]))
                    .ToList();
                if (!coords[0].Equals2D(coords[^1]))
                    coords.Add(coords[0]);
                var ring = GeoFactory.CreateLinearRing(coords.ToArray());
                var poly = GeoFactory.CreatePolygon(ring);
                return poly.IsValid ? poly : (Polygon)poly.Buffer(0);
            }
            catch
            {
                return null;
            }
        }

        private static float[][] GeomToCoordinates(Geometry geom)
        {
            var poly = geom is Polygon p ? p
                : geom is MultiPolygon mp ? (Polygon)mp.GetGeometryN(0)
                : null;
            if (poly == null) return Array.Empty<float[]>();
            return poly.ExteriorRing.Coordinates
                .Select(c => new float[] { (float)c.X, (float)c.Y })
                .ToArray();
        }

        private static Cell CloneLandCell(Cell original, Polygon polygon, int newId)
        {
            return new Cell
            {
                Id = newId,
                GeoDataCoordinates = GeomToCoordinates(polygon),
                Neighbors = original.Neighbors.ToArray(), // placeholder — updated by UpdateAllNeighborReferences
                Culture = original.Culture,
                Religion = original.Religion,
                Biome = original.Biome,
                Area = original.Area,
                DistanceToCoast = original.DistanceToCoast,
                Type = original.Type,
                State = original.State,
                AzProvince = original.AzProvince,
                IsRiverCell = false,
            };
        }

        private static void UpdateAllNeighborReferences(Map map, Dictionary<int, List<int>> replacements)
        {
            if (replacements.Count == 0) return;

            var polyCache = new Dictionary<int, Polygon?>();
            Polygon? GetPolygon(int id)
            {
                if (!polyCache.TryGetValue(id, out var poly))
                {
                    poly = map.Cells.TryGetValue(id, out var c) ? CellToPolygon(c) : null;
                    polyCache[id] = poly;
                }
                return poly;
            }

            foreach (var cell in map.Cells.Values)
            {
                if (!cell.Neighbors.Any(n => replacements.ContainsKey(n)))
                    continue;

                var cellPoly = GetPolygon(cell.Id);
                if (cellPoly == null) continue;

                var newNeighbors = new List<int>();
                foreach (int nId in cell.Neighbors)
                {
                    if (!replacements.TryGetValue(nId, out var replacingIds))
                    {
                        newNeighbors.Add(nId);
                        continue;
                    }
                    foreach (int replacingId in replacingIds)
                    {
                        var replacingPoly = GetPolygon(replacingId);
                        if (replacingPoly == null) continue;
                        var shared = cellPoly.Intersection(replacingPoly);
                        if (!shared.IsEmpty && (int)shared.Dimension >= (int)Dimension.Curve)
                            newNeighbors.Add(replacingId);
                    }
                }
                cell.Neighbors = newNeighbors.Distinct().ToArray();
            }
        }
    }
}
