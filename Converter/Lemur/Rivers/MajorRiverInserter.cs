using NetTopologySuite.Geometries;
using Converter.Lemur.Entities;
using static Converter.Lemur.Entities.Cell;

namespace Converter.Lemur.Rivers
{
    /// <summary>
    /// Inserts major river cells into the cell grid before barony formation.
    /// For each major river, builds a ribbon polygon from control points and
    /// subtracts it from affected cells, splitting them into bank halves and
    /// a river province cell. This makes rivers act as physical barriers for
    /// the barony growth algorithm.
    /// </summary>
    public static class MajorRiverInserter
    {
        private static readonly GeometryFactory GeoFactory = new GeometryFactory();

        public static void InsertMajorRiverCells(Map map, float majorThreshold)
        {
            var majorRivers = map.Rivers!
                .Where(r => r.IsMajor(majorThreshold) && !r.IsTributary)
                .ToList();

            if (majorRivers.Count == 0)
            {
                Logger.Info("No major rivers to process (check MajorRiverThreshold in settings).");
                return;
            }

            Logger.Info($"Inserting cells for {majorRivers.Count} major rivers...");

            int nextCellId = map.Cells!.Keys.Max() + 1;
            int nextProvinceId = 1;
            int totalRiverCells = 0;

            // Maps replaced cell IDs to the new cell IDs that replaced them,
            // so the final pass can update Neighbors arrays throughout the map.
            var cellReplacements = new Dictionary<int, List<int>>();
            // Maps new land cell IDs → NTS area of the original cell, for tiny-cell detection.
            var splitLandCells = new Dictionary<int, double>();

            foreach (var river in majorRivers)
            {
                if (river.ControlPoints == null || river.ControlPoints.Count < 2 || river.Width <= 0)
                {
                    Logger.Warning($"River '{river.Name}' skipped: missing control points or zero width.");
                    continue;
                }

                var ribbon = BuildRibbon(river);
                var riverCellIds = new List<int>();

                // Snapshot candidate cells (with pre-computed polygons) before any modification.
                // Segments discover candidates spatially — no CellIds list needed.
                var candidates = map.Cells.Values
                    .Where(c => c.Type != FeatureType.river)
                    .Select(c => (cell: c, poly: CellToPolygon(c)))
                    .Where(t => t.poly != null)
                    .ToList();

                var processedCellIds = new HashSet<int>();
                const int SegmentSize = 3; // new segment every ~3 control points

                for (int seg = 0; seg + 1 < river.ControlPoints!.Count; seg += SegmentSize - 1)
                {
                    var segPoints = river.ControlPoints.Skip(seg).Take(SegmentSize).ToList();
                    if (segPoints.Count < 2) continue;
                    var segRibbon = BuildRibbon(segPoints, river.Width);

                    foreach (var (cell, cellPoly) in candidates!)
                    {
                        if (processedCellIds.Contains(cell.Id)) continue;
                        if (!map.Cells.ContainsKey(cell.Id)) continue; // replaced by an earlier river
                        if (!segRibbon.Intersects(cellPoly)) continue;

                        processedCellIds.Add(cell.Id);

                    var ribbonInCell = ribbon.Intersection(cellPoly);
                    if (ribbonInCell.IsEmpty)
                        continue;

                    var remainder = cellPoly.Difference(ribbon);
                    int riverCellId = nextCellId++;

                    if (remainder.IsEmpty || remainder.Area < cellPoly.Area * 0.05)
                    {
                        // Engulfed: the ribbon covers the whole cell.
                        // Skip cells with burgs — destroying a settlement is wrong; leave the cell as land.
                        if (cell.Burg != null)
                        {
                            Logger.Debug($"  Skipping engulfed cell {cell.Id} — has burg '{cell.Burg.Name}'");
                            continue;
                        }
                        var riverCell = CreateRiverCell(cell, cellPoly, riverCellId);
                        map.Cells.Remove(cell.Id);
                        map.Cells[riverCellId] = riverCell;
                        cellReplacements[cell.Id] = new List<int> { riverCellId };
                        riverCellIds.Add(riverCellId);
                        totalRiverCells++;
                    }
                    else if (remainder is MultiPolygon multi && multi.NumGeometries == 2)
                    {
                        // Split: the ribbon bisects the cell into exactly two land halves
                        var poly0 = (Polygon)multi.GetGeometryN(0);
                        var poly1 = (Polygon)multi.GetGeometryN(1);

                        Polygon burgPoly, otherPoly;
                        if (cell.Burg != null)
                        {
                            var burgPt = GeoFactory.CreatePoint(new Coordinate(cell.Burg.Position.X, cell.Burg.Position.Y));
                            burgPoly = poly0.Distance(burgPt) <= poly1.Distance(burgPt) ? poly0 : poly1;
                            otherPoly = burgPoly == poly0 ? poly1 : poly0;
                        }
                        else
                        {
                            burgPoly = poly0.Area >= poly1.Area ? poly0 : poly1;
                            otherPoly = burgPoly == poly0 ? poly1 : poly0;
                        }

                        int cellAId = nextCellId++;
                        int cellBId = nextCellId++;

                        var cellA = CreateLandCell(cell, burgPoly, cellAId, inheritBurg: true);
                        var cellB = CreateLandCell(cell, otherPoly, cellBId, inheritBurg: false);
                        var riverCell = CreateRiverCell(cell, ribbonInCell, riverCellId);

                        // Update burg's back-reference to point at the new land cell
                        if (cellA.Burg != null) cellA.Burg.Cell = cellA;

                        map.Cells.Remove(cell.Id);
                        map.Cells[cellAId] = cellA;
                        map.Cells[cellBId] = cellB;
                        map.Cells[riverCellId] = riverCell;

                        WireNeighborsSplit(cell, cellA, burgPoly, cellB, otherPoly, riverCell, ribbon, map);

                        cellReplacements[cell.Id] = new List<int> { cellAId, cellBId, riverCellId };
                        splitLandCells[cellAId] = cellPoly.Area;
                        splitLandCells[cellBId] = cellPoly.Area;
                        riverCellIds.Add(riverCellId);
                        totalRiverCells++;
                    }
                    else
                    {
                        // Clipped: river clips a corner/edge; the cell is still contiguous
                        // Take the largest polygon piece as the surviving land cell
                        Polygon? landPoly = remainder is Polygon rp ? rp
                            : remainder is MultiPolygon mp
                                ? (Polygon?)mp.Geometries.OrderByDescending(g => g.Area).First()
                                : null;

                        if (landPoly == null) continue;

                        int landCellId = nextCellId++;
                        var landCell = CreateLandCell(cell, landPoly, landCellId, inheritBurg: true);
                        var riverCell = CreateRiverCell(cell, ribbonInCell, riverCellId);

                        // Update burg's back-reference to point at the new land cell
                        if (landCell.Burg != null) landCell.Burg.Cell = landCell;

                        map.Cells.Remove(cell.Id);
                        map.Cells[landCellId] = landCell;
                        map.Cells[riverCellId] = riverCell;

                        cellReplacements[cell.Id] = new List<int> { landCellId, riverCellId };
                        splitLandCells[landCellId] = cellPoly.Area;
                        riverCellIds.Add(riverCellId);
                        totalRiverCells++;
                    }
                    } // end foreach candidate
                } // end segment loop

                // Sort river cells upstream→downstream by proximity to control points.
                var riverCells = riverCellIds
                    .Where(id => map.Cells.ContainsKey(id))
                    .Select(id => map.Cells[id])
                    .OrderBy(c => ClosestControlPointIndex(c, river.ControlPoints!))
                    .ToList();

                // Wire river cells that share an edge as neighbors (upstream ↔ downstream).
                for (int i = 0; i < riverCells.Count; i++)
                {
                    var polyI = CellToPolygon(riverCells[i]);
                    if (polyI == null) continue;
                    for (int j = i + 1; j < riverCells.Count; j++)
                    {
                        var polyJ = CellToPolygon(riverCells[j]);
                        if (polyJ == null) continue;
                        if (!polyI.Intersects(polyJ)) continue;
                        var shared = polyI.Intersection(polyJ);
                        if (shared.IsEmpty || shared.Dimension < Dimension.Curve) continue;

                        if (!riverCells[i].Neighbors.Contains(riverCells[j].Id))
                            riverCells[i].Neighbors = riverCells[i].Neighbors.Append(riverCells[j].Id).ToArray();
                        if (!riverCells[j].Neighbors.Contains(riverCells[i].Id))
                            riverCells[j].Neighbors = riverCells[j].Neighbors.Append(riverCells[i].Id).ToArray();
                    }
                }

                // Bucket into sub-provinces of RiverProvinceCellCount cells each.
                if (riverCells.Count > 0)
                {
                    var buckets = new List<List<Cell>>();
                    int step = Math.Max(1, Settings.Instance.RiverProvinceCellCount);
                    for (int i = 0; i < riverCells.Count; i += step)
                        buckets.Add(riverCells.Skip(i).Take(step).ToList());

                    // Absorb an undersized tail bucket into the preceding one.
                    if (buckets.Count >= 2 && buckets[^1].Count < Settings.Instance.RiverProvinceMinCells)
                    {
                        buckets[^2].AddRange(buckets[^1]);
                        buckets.RemoveAt(buckets.Count - 1);
                    }

                    for (int b = 0; b < buckets.Count; b++)
                    {
                        var provinceName = buckets.Count == 1 ? river.Name : $"{river.Name} {b + 1}";
                        var province = new MajorRiverProvince(nextProvinceId++, buckets[b], provinceName, river.Id);
                        map.MajorRiverProvinces.Add(province);
                        Logger.Debug($"  River '{river.Name}' sub-province {b + 1}/{buckets.Count}: {buckets[b].Count} cells, id {province.Id}");
                    }
                }
            }

            UpdateAllNeighborReferences(map, cellReplacements);
            MergeTinyCells(map, splitLandCells);

            Logger.Info($"Major rivers complete: {totalRiverCells} river cells, {map.MajorRiverProvinces.Count} provinces.");
        }

        // Absorb a split land cell if its NTS area is below this fraction of the original cell area.
        private const double TinyLandCellFraction = 0.05;

        private static void MergeTinyCells(Map map, Dictionary<int, double> splitLandCells)
        {
            int mergedCount = 0;

            foreach (var (tinyId, originalArea) in splitLandCells)
            {
                if (!map.Cells.TryGetValue(tinyId, out var tinyCell)) continue;
                if (tinyCell.Type == FeatureType.river) continue;

                var tinyPoly = CellToPolygon(tinyCell);
                if (tinyPoly == null) continue;
                if (tinyPoly.Area >= originalArea * TinyLandCellFraction) continue; // not tiny

                // Find best absorbing neighbor: longest shared boundary, not a river cell
                Cell? best = null;
                Polygon? bestPoly = null;
                double bestLen = 0;

                foreach (int nId in tinyCell.Neighbors)
                {
                    if (!map.Cells.TryGetValue(nId, out var n)) continue;
                    if (n.Type == FeatureType.river) continue;
                    var nPoly = CellToPolygon(n);
                    if (nPoly == null) continue;
                    var sharedLen = tinyPoly.Intersection(nPoly).Length;
                    if (sharedLen > bestLen) { best = n; bestPoly = nPoly; bestLen = sharedLen; }
                }

                if (best == null)
                {
                    Logger.Warning($"Tiny cell {tinyId} has no eligible land neighbor — kept as-is.");
                    continue;
                }

                // Union the two polygons; prefer the result as a single Polygon
                var merged = bestPoly!.Union(tinyPoly);
                var mergedPoly = merged is Polygon p ? p
                    : merged is MultiPolygon mp
                        ? (Polygon)mp.Geometries.OrderByDescending(g => g.Area).First()
                        : null;
                if (mergedPoly == null) continue;

                best.GeoDataCoordinates = GeomToCoordinates(mergedPoly);

                // Absorbing cell inherits all of tiny's neighbors (except tiny itself and self)
                best.Neighbors = best.Neighbors
                    .Union(tinyCell.Neighbors)
                    .Where(id => id != tinyId && id != best.Id)
                    .ToArray();

                // Replace tinyId with best.Id in every former neighbor of the tiny cell
                foreach (int nId in tinyCell.Neighbors)
                {
                    if (nId == best.Id) continue;
                    if (!map.Cells.TryGetValue(nId, out var neighbor)) continue;
                    neighbor.Neighbors = neighbor.Neighbors
                        .Select(id => id == tinyId ? best.Id : id)
                        .Distinct().ToArray();
                }

                map.Cells.Remove(tinyId);
                mergedCount++;
                Logger.Debug($"  Merged tiny cell {tinyId} into {best.Id} " +
                             $"(area ratio {tinyPoly.Area / originalArea:P0})");
            }

            if (mergedCount > 0)
                Logger.Info($"Tiny cell merge: absorbed {mergedCount} degenerate land cells.");
        }

        /// <summary>
        /// Returns the index of the control point closest to the cell's centroid.
        /// Used to sort river cells into upstream→downstream order.
        /// </summary>
        private static int ClosestControlPointIndex(Cell cell, List<double[]> cps)
        {
            var cx = cell.GeoDataCoordinates!.Average(p => p[0]);
            var cy = cell.GeoDataCoordinates!.Average(p => p[1]);
            return cps
                .Select((p, i) => (dist: Math.Pow(p[0] - cx, 2) + Math.Pow(p[1] - cy, 2), i))
                .MinBy(t => t.dist).i;
        }

        private static Geometry BuildRibbon(River river) =>
            BuildRibbon(river.ControlPoints!, river.Width);

        private static Geometry BuildRibbon(IList<double[]> controlPoints, float width)
        {
            var coords = controlPoints
                .Select(p => new Coordinate(p[0], p[1]))
                .ToArray();
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
                // Buffer(0) fixes self-intersections; returns valid polygon
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

        private static Cell CreateLandCell(Cell original, Polygon polygon, int newId, bool inheritBurg)
        {
            return new Cell
            {
                Id = newId,
                GeoDataCoordinates = GeomToCoordinates(polygon),
                Neighbors = original.Neighbors, // placeholder — updated by WireNeighbors / global pass
                Culture = original.Culture,
                Religion = original.Religion,
                Biome = original.Biome,
                Area = original.Area,
                DistanceToCoast = original.DistanceToCoast,
                Type = original.Type,
                State = original.State,
                AzProvince = original.AzProvince,
                Burg = inheritBurg ? original.Burg : null,
            };
        }

        private static Cell CreateRiverCell(Cell original, Geometry riverGeom, int newId)
        {
            return new Cell
            {
                Id = newId,
                GeoDataCoordinates = GeomToCoordinates(riverGeom),
                Neighbors = Array.Empty<int>(), // set by WireNeighborsSplit or global pass
                Culture = 0,
                Religion = 0,
                Biome = 0,
                Area = 0,
                DistanceToCoast = 0,
                Type = FeatureType.river,
                State = 0,
                AzProvince = 0,
            };
        }

        private static void WireNeighborsSplit(
            Cell original,
            Cell cellA, Polygon polyA,
            Cell cellB, Polygon polyB,
            Cell riverCell,
            Geometry ribbon, Map map)
        {
            var neighborsA = new List<int> { riverCell.Id };
            var neighborsB = new List<int> { riverCell.Id };
            var neighborsRiver = new List<int> { cellA.Id, cellB.Id };

            foreach (int neighborId in original.Neighbors)
            {
                if (!map.Cells.TryGetValue(neighborId, out var neighbor))
                    continue;
                var neighborPoly = CellToPolygon(neighbor);
                if (neighborPoly == null)
                    continue;

                if (TouchesOutsideRibbon(polyA, neighborPoly, ribbon))
                    neighborsA.Add(neighborId);
                if (TouchesOutsideRibbon(polyB, neighborPoly, ribbon))
                    neighborsB.Add(neighborId);
            }

            cellA.Neighbors = neighborsA.ToArray();
            cellB.Neighbors = neighborsB.ToArray();
            riverCell.Neighbors = neighborsRiver.ToArray();
        }

        /// <summary>
        /// Two cells touch "outside the ribbon" if their shared boundary
        /// has at least some extent beyond the river's footprint.
        /// </summary>
        private static bool TouchesOutsideRibbon(Geometry a, Geometry b, Geometry ribbon)
        {
            var shared = a.Intersection(b);
            if (shared.IsEmpty) return false;
            var outside = shared.Difference(ribbon);
            return !outside.IsEmpty;
        }

        /// <summary>
        /// Final pass: for every cell still in the map that lists a removed cell ID
        /// in its Neighbors, replace that ID with whatever geometrically adjacent
        /// replacement cells now exist.
        /// </summary>
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

                        // Share a non-trivial boundary (edge, not just a point)
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
