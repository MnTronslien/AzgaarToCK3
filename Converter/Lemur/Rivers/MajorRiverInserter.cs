using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Distance;
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

            Logger.Section("Inserting major rivers");
            Logger.Info($"{majorRivers.Count} rivers above discharge threshold {majorThreshold}.");

            // Avg cell diameter in CK3 pixels — O(1) from canvas dimensions, no cell scan needed.
            // Cell density varies by map (user-chosen cell count + canvas size), so this is map-specific
            // but instantaneous: total_canvas_area / cell_count gives avg area per cell.
            float canvasToCk3    = (float)Map.MapWidth / map.JsonMap.info.width;
            float avgCanvasArea  = (float)(map.JsonMap.info.width * map.JsonMap.info.height) / map.Cells.Count;
            float avgDiameterCk3 = 2f * (float)Math.Sqrt(avgCanvasArea * canvasToCk3 * canvasToCk3 / Math.PI);
            float floorWidthCk3  = avgDiameterCk3 * 0.35f;
            Logger.Info($"Avg cell diameter: {avgDiameterCk3:F1} CK3 px | width floor (35%): {floorWidthCk3:F1} px at threshold, {floorWidthCk3 * (float)Math.Pow(majorRivers.Max(r => r.Discharge) / (double)majorThreshold, 0.25):F1} px at max discharge.");

            // Sort: tributaries before the rivers they flow into.
            // This ensures each river's Phase 1 can carve into cells already built by its tributaries.
            var sortedRivers = SortRiversTributariesFirst(majorRivers);

            // Phases 1+2 run per river in tributary-first order.
            // UpdateAllNeighborReferences is deferred until all rivers are processed.
            var allReplacements = new Dictionary<int, List<int>>();
            var riverCellIds    = new Dictionary<River, List<int>>();
            int totalLandSplits  = 0;
            int totalRiverCarved = 0;

            foreach (var river in sortedRivers)
            {
                var (landSplits, riverCarved) = CarveRibbonFromCells(map, river, allReplacements, majorThreshold, avgDiameterCk3);
                totalLandSplits  += landSplits;
                totalRiverCarved += riverCarved;
                riverCellIds[river] = BuildRiverCellsForRiver(map, river, majorThreshold, avgDiameterCk3);
            }

            UpdateAllNeighborReferences(map, allReplacements);
            int burgNudges = NudgeBurgsOutOfRiver(map);
            Logger.Info($"Phase 1 complete: {totalLandSplits} land cells carved, {totalRiverCarved} tributary river cells trimmed, {burgNudges} burgs nudged.");

            int totalRiverCells = riverCellIds.Values.Sum(l => l.Count);
            Logger.Info($"Phase 2 complete: {totalRiverCells} river cells created.");
            AssertNoRiverCellOverlap(map, riverCellIds);
            AssertRiverCellsCoverRibbon(map, riverCellIds, majorThreshold, avgDiameterCk3);

            // Phase 3: wire river cell neighbors
            WireRiverNeighbors(map, riverCellIds);
            Logger.Info("Phase 3 complete: river neighbors wired.");

            // Phase 4: build MajorRiverProvince objects
            BuildRiverProvinces(map, riverCellIds);
            Logger.Info($"Phase 4 complete: {map.MajorRiverProvinces.Count} river provinces built.");
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// After all carving for a river is complete, merge each deferred tiny piece into
        /// the land neighbor with the most surface contact. Runs after the main carving loop
        /// so all split results (e.g. 800/801 from a subsequently carved neighbor) are stable.
        /// </summary>
        private static int MergeTinyPieces(
            Map map,
            List<(int tinyCellId, int originalCellId)> tinyPieces,
            Dictionary<int, List<int>> cellReplacements)
        {
            int merged = 0;
            foreach (var (tinyCellId, originalCellId) in tinyPieces)
            {
                if (!map.Cells.TryGetValue(tinyCellId, out var tinyCell)) continue;
                var tinyPoly = CellToPolygon(tinyCell);
                if (tinyPoly == null) continue;

                // Gather candidate neighbor IDs: original neighbors + follow cellReplacements
                // for any that were themselves carved during this pass.
                var candidateIds = new HashSet<int>();
                foreach (var nId in tinyCell.Neighbors)
                {
                    if (map.Cells.ContainsKey(nId))
                        candidateIds.Add(nId);
                    if (cellReplacements.TryGetValue(nId, out var replacements))
                        foreach (var rId in replacements)
                            candidateIds.Add(rId);
                }
                candidateIds.Remove(tinyCellId);

                // Find the land neighbor with the most surface contact
                Cell?    bestCell   = null;
                Polygon? bestPoly   = null;
                double   bestShared = 0;

                foreach (var nId in candidateIds)
                {
                    if (!map.Cells.TryGetValue(nId, out var neighborCell)) continue;
                    if (neighborCell.IsRiverCell) continue;
                    var neighborPoly = CellToPolygon(neighborCell);
                    if (neighborPoly == null) continue;
                    var shared = neighborPoly.Intersection(tinyPoly);
                    if (shared.IsEmpty) continue;
                    double measure = shared.Area > 0 ? shared.Area : shared.Length;
                    if (measure > bestShared)
                    {
                        bestShared = measure;
                        bestCell   = neighborCell;
                        bestPoly   = neighborPoly;
                    }
                }

                if (bestCell == null || bestPoly == null)
                {
                    Logger.Error($"  Tiny piece {tinyCellId} (from original {originalCellId}): no touching land neighbor found — leaving as cell.");
                    continue;
                }

                var unionResult = bestPoly.Union(tinyPoly);
                if (unionResult is MultiPolygon)
                    Logger.Error($"  Tiny piece {tinyCellId}: union with {bestCell.Id} produced MultiPolygon — geometry may be lost.");
                bestCell.GeoDataCoordinates = GeomToCoordinates(unionResult);

                map.Cells.Remove(tinyCellId);
                if (cellReplacements.TryGetValue(originalCellId, out var repList))
                    repList.Remove(tinyCellId);

                Logger.Verbose($"  Tiny piece {tinyCellId} (area={tinyPoly.Area:F2}, from {originalCellId}) merged into {bestCell.Id}.");
                merged++;
            }
            return merged;
        }

        /// <summary>
        /// For every land cell whose burg position now lies outside the cell polygon (displaced
        /// by river carving), snaps the burg to the nearest point on the cell boundary then
        /// nudges it inward toward the centroid by min(0.5, half the boundary-to-centroid distance).
        /// Returns the number of burgs relocated.
        /// </summary>
        private static int NudgeBurgsOutOfRiver(Map map)
        {
            int count = 0;
            foreach (var cell in map.Cells!.Values)
            {
                if (cell.Burg == null || cell.IsRiverCell) continue;

                var poly = CellToPolygon(cell);
                if (poly == null) continue;

                // Burg positions are canvas pixels; the cell polygon is geo. Compare in geo space.
                var burgGeo = Helper.CanvasToGeo(cell.Burg.Position, map);
                var burgPt = GeoFactory.CreatePoint(new Coordinate(burgGeo.Lon, burgGeo.Lat));

                if (poly.Contains(burgPt)) continue; // still inside — nothing to do

                // Nearest point on the cell boundary (geo space)
                var nearest = DistanceOp.NearestPoints(burgPt, poly.ExteriorRing);
                var boundaryPt = nearest[1];

                var centroid = poly.Centroid.Coordinate;
                double dx = centroid.X - boundaryPt.X;
                double dy = centroid.Y - boundaryPt.Y;
                double distToCentroid = Math.Sqrt(dx * dx + dy * dy);

                // Compute the nudged point in geo space, then convert back to canvas for storage.
                double geoX, geoY;
                if (distToCentroid < 1e-10)
                {
                    geoX = centroid.X; geoY = centroid.Y; // degenerate cell — move straight to centroid
                }
                else
                {
                    double epsilon = Math.Min(0.5, distToCentroid * 0.5);
                    geoX = boundaryPt.X + dx / distToCentroid * epsilon;
                    geoY = boundaryPt.Y + dy / distToCentroid * epsilon;
                }
                cell.Burg.Position = Helper.GeoToCanvas(new GeoPoint(geoX, geoY), map);

                Logger.Debug($"  Burg '{cell.Burg.Name}' nudged into cell {cell.Id} after river carving.");
                count++;
            }
            return count;
        }

        /// <summary>
        /// Topological sort: tributaries before the rivers they flow into.
        /// Post-order DFS from roots (major rivers whose parent is not itself a major river).
        /// </summary>
        private static List<River> SortRiversTributariesFirst(List<River> rivers)
        {
            var majorIds = new HashSet<int>(rivers.Select(r => r.Id));
            var result   = new List<River>();
            var visited  = new HashSet<int>();

            void Visit(River r)
            {
                if (!visited.Add(r.Id)) return;
                foreach (var trib in rivers.Where(t => t.ParentId == r.Id))
                    Visit(trib);
                result.Add(r);
            }

            foreach (var r in rivers.Where(r => !majorIds.Contains(r.ParentId)))
                Visit(r);

            return result;
        }

        // ─── Phase 1 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Carves a single river's ribbon from all intersecting cells:
        ///   • Land cells — burg-safe split/clip logic (fully engulfed = keep as land).
        ///   • River cells (from previously processed tributaries) — simple clip/delete, no burg.
        /// Returns (landSplits, riverCellsCarved).
        /// </summary>
        private static (int landSplits, int riverCarved) CarveRibbonFromCells(
            Map map, River river, Dictionary<int, List<int>> cellReplacements,
            float majorThreshold, float avgDiameterCk3)
        {
            if (river.ControlPoints == null || river.ControlPoints.Count < 2)
            {
                Logger.Warning($"River '{river.Name}' skipped: missing control points.");
                return (0, 0);
            }

            var (sourceGeoHW, mouthGeoHW) = ComputeRiverWidths(river, map, majorThreshold, avgDiameterCk3);
            var ribbon = BuildTaperedRibbon(river.ControlPoints, sourceGeoHW, mouthGeoHW);
            int nextCellId = map.Cells!.Keys.Max() + 1;

            // ── Land cells ────────────────────────────────────────────────────────
            var landCandidates = map.Cells.Values
                .Where(c => Cell.IsDryLand(c.Type) && !c.IsRiverCell)
                .Select(c => (cell: c, poly: CellToPolygon(c)))
                .Where(t => t.poly != null && ribbon.Intersects(t.poly))
                .ToList();

            int landSplits = 0;
            // Tiny pieces deferred for post-carve merging: (tinyCellId, originalCellId)
            var tinyPiecesToMerge = new List<(int tinyCellId, int originalCellId)>();

            foreach (var (cell, cellPoly) in landCandidates)
            {
                if (!map.Cells.ContainsKey(cell.Id)) continue;

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
                    cell.GeoDataCoordinates = GeomToCoordinates(pieces[0]);
                    landSplits++;
                }
                else
                {
                    var keepPiece = pieces.OrderByDescending(p => p.Area).First();
                    var tinyPiece = pieces.OrderBy(p => p.Area).First();

                    bool burgInTiny = false;
                    if (cell.Burg != null && pieces.Count == 2)
                    {
                        var bGeo = Helper.CanvasToGeo(cell.Burg.Position, map);
                        var burgPt = GeoFactory.CreatePoint(new Coordinate(bGeo.Lon, bGeo.Lat));
                        burgInTiny = tinyPiece.Contains(burgPt) || tinyPiece.Distance(burgPt) < 1e-6;
                    }

                    bool deferTinyMerge = pieces.Count == 2
                        && !burgInTiny
                        && tinyPiece.Area / keepPiece.Area < 0.30;

                    if (deferTinyMerge)
                    {
                        // Create both cells now; tiny piece will be merged in the post-carve pass
                        // once all carving results for this river are stable.
                        int keepId = nextCellId++;
                        int tinyId = nextCellId++;
                        var keepCell = CloneLandCell(cell, keepPiece, keepId);
                        var tinyCell = CloneLandCell(cell, tinyPiece, tinyId);

                        if (cell.Burg != null)
                        {
                            keepCell.Burg      = cell.Burg;
                            keepCell.Burg.Cell = keepCell;
                        }

                        map.Cells[keepId] = keepCell;
                        map.Cells[tinyId] = tinyCell;
                        tinyPiecesToMerge.Add((tinyId, cell.Id));

                        Logger.Verbose($"  Cell {cell.Id} (AzProvince {cell.AzProvince}) split into [{keepId}] + deferred tiny [{tinyId}]");
                        map.Cells.Remove(cell.Id);
                        cellReplacements[cell.Id] = new List<int> { keepId, tinyId };
                        landSplits++;
                    }
                    else
                    {
                        // Normal split: create a cell for each piece
                        var newIds = new List<int>();
                        bool burgAssigned = false;

                        for (int k = 0; k < pieces.Count; k++)
                        {
                            int newId   = nextCellId++;
                            var newCell = CloneLandCell(cell, pieces[k], newId);

                            if (cell.Burg != null && !burgAssigned)
                            {
                                var bGeo = Helper.CanvasToGeo(cell.Burg.Position, map);
                                var burgPt = GeoFactory.CreatePoint(new Coordinate(bGeo.Lon, bGeo.Lat));
                                if (pieces[k].Contains(burgPt) || pieces[k].Distance(burgPt) < 1e-6)
                                {
                                    newCell.Burg      = cell.Burg;
                                    newCell.Burg.Cell = newCell;
                                    burgAssigned      = true;
                                }
                            }

                            map.Cells[newId] = newCell;
                            newIds.Add(newId);
                        }

                        if (cell.Burg != null && !burgAssigned)
                        {
                            var largestId = newIds
                                .OrderByDescending(id => CellToPolygon(map.Cells[id])?.Area ?? 0)
                                .First();
                            map.Cells[largestId].Burg      = cell.Burg;
                            map.Cells[largestId].Burg!.Cell = map.Cells[largestId];
                            Logger.Debug($"  Burg '{cell.Burg.Name}' fallback-assigned to largest split piece {largestId}.");
                        }

                        Logger.Verbose($"  Cell {cell.Id} (AzProvince {cell.AzProvince}) split into [{string.Join(", ", newIds)}]");
                        map.Cells.Remove(cell.Id);
                        cellReplacements[cell.Id] = newIds;
                        landSplits++;
                    }
                }
            }

            // Post-carve pass: merge deferred tiny pieces now that all carving results are stable
            int tinyMerged = MergeTinyPieces(map, tinyPiecesToMerge, cellReplacements);
            Logger.Debug($"  River '{river.Name}': {landSplits} land cells carved, {tinyMerged} tiny pieces merged.");

            // ── River cells from previously processed tributaries ─────────────────
            var riverCandidates = map.Cells.Values
                .Where(c => c.IsRiverCell)
                .Select(c => (cell: c, poly: CellToPolygon(c)))
                .Where(t => t.poly != null && ribbon.Intersects(t.poly))
                .ToList();

            int riverCarved = 0;
            foreach (var (cell, cellPoly) in riverCandidates)
            {
                if (!map.Cells.ContainsKey(cell.Id)) continue;

                var remainder = cellPoly!.Difference(ribbon);
                if (remainder == null || remainder.IsEmpty)
                {
                    // Fully engulfed by main river — remove tributary cell entirely
                    map.Cells.Remove(cell.Id);
                    cellReplacements[cell.Id] = new List<int>();
                    riverCarved++;
                    continue;
                }

                var pieces = remainder is MultiPolygon mp
                    ? mp.Geometries.OfType<Polygon>().ToList()
                    : remainder is Polygon p ? new List<Polygon> { p }
                    : null;
                if (pieces == null || pieces.Count == 0)
                {
                    map.Cells.Remove(cell.Id);
                    cellReplacements[cell.Id] = new List<int>();
                    riverCarved++;
                    continue;
                }

                if (pieces.Count == 1)
                {
                    cell.GeoDataCoordinates = GeomToCoordinates(pieces[0]);
                    riverCarved++;
                }
                else
                {
                    var newIds = new List<int>();
                    foreach (var piece in pieces)
                    {
                        int newId = nextCellId++;
                        map.Cells[newId] = new Cell
                        {
                            Id = newId,
                            GeoDataCoordinates = GeomToCoordinates(piece),
                            Neighbors = cell.Neighbors.ToArray(),
                            IsRiverCell = true,
                            Culture = 0, Religion = 0, Biome = 0, Area = 0,
                            DistanceToCoast = 0, State = 0, AzProvince = 0,
                        };
                        newIds.Add(newId);
                    }
                    map.Cells.Remove(cell.Id);
                    cellReplacements[cell.Id] = newIds;
                    riverCarved++;
                }
            }

            if (riverCarved > 0)
                Logger.Debug($"  River '{river.Name}': {riverCarved} tributary river cells trimmed.");

            return (landSplits, riverCarved);
        }

        // ─── Phase 2 ──────────────────────────────────────────────────────────────

        private static List<int> BuildRiverCellsForRiver(Map map, River river,
            float majorThreshold, float avgDiameterCk3)
        {
            if (river.ControlPoints == null || river.ControlPoints.Count < 2)
                return new List<int>();

            var cps = river.ControlPoints;
            int n   = cps.Count;
            var (sourceGeoHW, mouthGeoHW) = ComputeRiverWidths(river, map, majorThreshold, avgDiameterCk3);
            var fullRibbon = BuildTaperedRibbon(cps, sourceGeoHW, mouthGeoHW);
            var ids        = new List<int>();
            int nextCellId = map.Cells!.Keys.Max() + 1;

            int step = ControlPointsPerRiverCell - 1;
            for (int start = 0; start + 1 < cps.Count; start += step)
            {
                var window = cps.Skip(start).Take(ControlPointsPerRiverCell).ToList();
                if (window.Count < 2) continue;

                float tStart      = n > 1 ? (float)start / (n - 1) : 0f;
                float tEnd        = n > 1 ? (float)(start + window.Count - 1) / (n - 1) : 1f;
                float sliceSrcHW  = sourceGeoHW + tStart * (mouthGeoHW - sourceGeoHW);
                float sliceMthHW  = sourceGeoHW + tEnd   * (mouthGeoHW - sourceGeoHW);
                var sliceRibbon = BuildTaperedRibbon(window, sliceSrcHW, sliceMthHW);
                Geometry sliceGeom = sliceRibbon.Intersection(fullRibbon);
                if (sliceGeom == null || sliceGeom.IsEmpty) continue;

                // Leading cut: trim the backward-facing round cap at this slice's start junction.
                // Not applied to the first slice — its upstream cap is a natural river terminus.
                int leadJ = start;
                if (leadJ > 0)
                {
                    float hwAtLeadJ = sourceGeoHW + (n > 1 ? (float)leadJ / (n - 1) : 0f) * (mouthGeoHW - sourceGeoHW);
                    var cutBox = BuildJunctionCutBox(cps, leadJ, hwAtLeadJ * 2f, forward: false);
                    if (cutBox != null)
                    {
                        sliceGeom = sliceGeom.Difference(cutBox);
                        if (sliceGeom == null || sliceGeom.IsEmpty) continue;
                    }
                }

                // Trailing cut: trim the forward-facing round cap at this slice's end junction.
                // Not applied to the last slice — its downstream cap is a natural river terminus.
                int trailJ = start + window.Count - 1;
                if (trailJ < cps.Count - 1)
                {
                    float hwAtTrailJ = sourceGeoHW + (n > 1 ? (float)trailJ / (n - 1) : 0f) * (mouthGeoHW - sourceGeoHW);
                    var cutBox = BuildJunctionCutBox(cps, trailJ, hwAtTrailJ * 2f, forward: true);
                    if (cutBox != null)
                    {
                        sliceGeom = sliceGeom.Difference(cutBox);
                        if (sliceGeom == null || sliceGeom.IsEmpty) continue;
                    }
                }

                var slicePoly = sliceGeom is Polygon sp ? sp
                    : sliceGeom is MultiPolygon smp
                        ? (Polygon)smp.Geometries.OrderByDescending(g => g.Area).First()
                        : null;
                if (slicePoly == null) continue;

                ids.Add(CreateRiverCell(map, ref nextCellId, slicePoly));
            }

            Logger.Debug($"  River '{river.Name}': {ids.Count} river cells built from {cps.Count} control points.");
            return ids;
        }

        /// <summary>
        /// Builds a half-plane cutting box at interior junction cps[j].
        /// The cut line passes through cps[j], perpendicular to the true angular bisector of
        /// the incoming segment (prev→cur) and outgoing segment (cur→next).
        /// Using normalized unit vectors ensures the bisector is angularly equidistant from both
        /// segment perpendiculars regardless of segment length.
        /// <paramref name="forward"/> = true  → box covers the half-plane toward cps[j+1]
        ///                                       (trims the trailing round cap of slice A).
        /// <paramref name="forward"/> = false → box covers the half-plane toward cps[j-1]
        ///                                       (trims the leading round cap of slice B).
        /// </summary>
        private static Geometry? BuildJunctionCutBox(List<float[]> cps, int j, float width, bool forward)
        {
            if (j <= 0 || j >= cps.Count - 1) return null;

            var prev = cps[j - 1];
            var cur  = cps[j];
            var next = cps[j + 1];

            // Normalize each segment direction independently so segment length does not bias the bisector.
            double abx = (double)cur[0]  - (double)prev[0];
            double aby = (double)cur[1]  - (double)prev[1];
            double abLen = Math.Sqrt(abx * abx + aby * aby);
            if (abLen < 1e-10) return null;
            abx /= abLen; aby /= abLen;  // d_AB: unit vector A→B

            double bcx = (double)next[0] - (double)cur[0];
            double bcy = (double)next[1] - (double)cur[1];
            double bcLen = Math.Sqrt(bcx * bcx + bcy * bcy);
            if (bcLen < 1e-10) return null;
            bcx /= bcLen; bcy /= bcLen;  // d_BC: unit vector B→C

            // CL normal = normalize(d_AB + d_BC) — true angular bisector of the two travel directions.
            double cdx = abx + bcx;
            double cdy = aby + bcy;
            double len = Math.Sqrt(cdx * cdx + cdy * cdy);
            if (len < 1e-10) return null;  // 180° reversal — degenerate
            cdx /= len;
            cdy /= len;

            // CL direction: perpendicular to the bisector normal
            double nx = -cdy;
            double ny =  cdx;

            double px = (double)cur[0];
            double py = (double)cur[1];

            // halfN: how far the box extends along the cut line (covers full ribbon + margin)
            double halfN = width * 2.0;
            // extent: how far the box extends into the half-plane to remove (covers the full round cap)
            double extent = width * 4.0;
            double sign = forward ? 1.0 : -1.0;

            var c1 = new Coordinate(px + nx * halfN,                        py + ny * halfN);
            var c2 = new Coordinate(px - nx * halfN,                        py - ny * halfN);
            var c3 = new Coordinate(px - nx * halfN + sign * cdx * extent,  py - ny * halfN + sign * cdy * extent);
            var c4 = new Coordinate(px + nx * halfN + sign * cdx * extent,  py + ny * halfN + sign * cdy * extent);

            var ring = GeoFactory.CreateLinearRing(new[] { c1, c2, c3, c4, c1 });
            var box  = GeoFactory.CreatePolygon(ring);
            return box.IsValid ? box : box.Buffer(0);
        }

        private static int CreateRiverCell(Map map, ref int nextCellId, Polygon poly)
        {
            int id = nextCellId++;
            map.Cells[id] = new Cell
            {
                Id = id,
                GeoDataCoordinates = GeomToCoordinates(poly),
                Neighbors = Array.Empty<int>(),
                IsRiverCell = true,
                Culture = 0, Religion = 0, Biome = 0, Area = 0,
                DistanceToCoast = 0, State = 0, AzProvince = 0,
            };
            return id;
        }

        // ─── Phase 3 ──────────────────────────────────────────────────────────────

        private static void WireRiverNeighbors(Map map, Dictionary<River, List<int>> riverCellIds)
        {
            // Build polygon cache once — CellToPolygon is the expensive step
            var polyCache = map.Cells.ToDictionary(
                kv => kv.Key,
                kv => CellToPolygon(kv.Value));

            // Build spatial index — replaces O(N²) inner loop with O(log N + k) queries
            var index = new STRtree<int>();
            foreach (var (id, poly) in polyCache)
                if (poly != null)
                    index.Insert(poly.EnvelopeInternal, id);

            foreach (var (river, ids) in riverCellIds)
            {
                if (ids.Count == 0) continue;
                var sameRiverSet = new HashSet<int>(ids);

                foreach (int rcId in ids)
                {
                    if (!map.Cells.TryGetValue(rcId, out var rc)) continue;
                    if (!polyCache.TryGetValue(rcId, out var rcPoly) || rcPoly == null) continue;

                    // Shared filter: given a candidate otherId, does it pass all wiring criteria?
                    bool Qualifies(int otherId)
                    {
                        if (otherId == rcId) return false;
                        if (!polyCache.TryGetValue(otherId, out var otherPoly) || otherPoly == null) return false;
                        // Skip river cells from other rivers — don't cross-wire different rivers
                        if (map.Cells.TryGetValue(otherId, out var other) && other.IsRiverCell && !sameRiverSet.Contains(otherId)) return false;
                        if (!rcPoly.Intersects(otherPoly)) return false;
                        var shared = rcPoly.Intersection(otherPoly);
                        return shared != null && !shared.IsEmpty && shared.Dimension >= Dimension.Curve;
                    }

                    var candidates = index.Query(rcPoly.EnvelopeInternal)
                        .Where(Qualifies)
                        .ToHashSet();

                    foreach (var otherId in candidates)
                    {
                        map.Cells.TryGetValue(otherId, out var other);
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
                    var province = new MajorRiverProvince(nextProvinceId++, bucket, river.Name, river.Id);
                    map.MajorRiverProvinces.Add(province);
                    Logger.Debug($"  Province '{river.Name}' {i / step + 1}: {bucket.Count} cells, id {province.Id}");
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

        /// <summary>
        /// Checks that the union of all river cells for each river covers the full ribbon area.
        /// Reports coverage % per river; warns if any river falls below 99%.
        /// </summary>
        private static void AssertRiverCellsCoverRibbon(Map map, Dictionary<River, List<int>> riverCellIds,
            float majorThreshold, float avgDiameterCk3)
        {
            foreach (var (river, ids) in riverCellIds)
            {
                if (ids.Count == 0) continue;

                var (sourceGeoHW, mouthGeoHW) = ComputeRiverWidths(river, map, majorThreshold, avgDiameterCk3);
                var ribbon = BuildTaperedRibbon(river.ControlPoints!, sourceGeoHW, mouthGeoHW);
                var ribbonArea = ribbon.Area;
                if (ribbonArea <= 0) continue;

                Geometry? cellUnion = null;
                foreach (int id in ids)
                {
                    var poly = map.Cells.TryGetValue(id, out var cell) ? CellToPolygon(cell) : null;
                    if (poly == null) continue;
                    cellUnion = cellUnion == null ? (Geometry)poly : cellUnion.Union(poly);
                }

                if (cellUnion == null)
                {
                    Logger.Warning($"  [Assert] River '{river.Name}': no cell polygons to check coverage.");
                    continue;
                }

                double coverageArea = cellUnion.Intersection(ribbon).Area;
                double coveragePct = coverageArea / ribbonArea * 100.0;
                double gapPct = 100.0 - coveragePct;

                double gapArea = ribbonArea * gapPct / 100.0;
                if (coveragePct >= 99.0)
                    Logger.Info($"Phase 2 coverage OK: '{river.Name}' covers {coveragePct:F2}% of ribbon (gap {gapArea:F6} units²).");
                else
                    Logger.Warning($"  [Assert] River '{river.Name}' covers only {coveragePct:F2}% of ribbon (gap {gapArea:F6} units², ribbon total {ribbonArea:F4} units²).");
            }
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

        private static Geometry BuildTaperedRibbon(List<float[]> controlPoints, float sourceHalfWidth, float mouthHalfWidth)
        {
            int n = controlPoints.Count;
            var left  = new Coordinate[n];
            var right = new Coordinate[n];

            for (int i = 0; i < n; i++)
            {
                double t  = n > 1 ? (double)i / (n - 1) : 0.0;
                double hw = sourceHalfWidth + t * (mouthHalfWidth - sourceHalfWidth);

                double px = controlPoints[i][0], py = controlPoints[i][1];
                double tx, ty;
                if (i == 0) {
                    tx = controlPoints[1][0] - controlPoints[0][0];
                    ty = controlPoints[1][1] - controlPoints[0][1];
                } else if (i == n - 1) {
                    tx = controlPoints[n-1][0] - controlPoints[n-2][0];
                    ty = controlPoints[n-1][1] - controlPoints[n-2][1];
                } else {
                    tx = controlPoints[i+1][0] - controlPoints[i-1][0];
                    ty = controlPoints[i+1][1] - controlPoints[i-1][1];
                }

                double len = Math.Sqrt(tx * tx + ty * ty);
                if (len < 1e-10) { tx = 1; ty = 0; } else { tx /= len; ty /= len; }
                double perpX = -ty, perpY = tx;

                left[i]  = new Coordinate(px + perpX * hw, py + perpY * hw);
                right[i] = new Coordinate(px - perpX * hw, py - perpY * hw);
            }

            // Ring: left side forward, right side backward, close at source
            var ring = new Coordinate[n * 2 + 1];
            for (int i = 0; i < n; i++) ring[i]     = left[i];
            for (int i = 0; i < n; i++) ring[n + i] = right[n - 1 - i];
            ring[n * 2] = left[0];

            try
            {
                var lr   = GeoFactory.CreateLinearRing(ring);
                var poly = GeoFactory.CreatePolygon(lr);
                if (poly.IsValid) return poly;
                var fixed_ = poly.Buffer(0);
                if (!fixed_.IsEmpty) return fixed_;
            }
            catch { }

            // Fallback: uniform buffer at average half-width
            var coords = controlPoints.Select(p => new Coordinate((double)p[0], (double)p[1])).ToArray();
            return GeoFactory.CreateLineString(coords).Buffer((sourceHalfWidth + mouthHalfWidth) / 2.0);
        }

        /// <summary>
        /// Computes geographic half-widths (in lon/lat degrees) for a river's source and mouth,
        /// scaled by discharge relative to the major-river threshold and expressed as a fraction
        /// of the average land cell diameter. Preserves Azgaar's own source/mouth taper ratio.
        /// </summary>
        private static (float sourceGeoHW, float mouthGeoHW) ComputeRiverWidths(
            River river, Map map, float majorThreshold, float avgDiameterCk3)
        {
            // Scale width by (discharge/threshold)^0.25 — gives ~2× range over a 20× discharge spread
            float dischargeScale = (float)Math.Pow(Math.Max(1.0, river.Discharge / (double)majorThreshold), 0.25);
            float mouthCk3Px = avgDiameterCk3 * 0.35f * dischargeScale;

            // Preserve Azgaar's source/mouth ratio; fall back to 30% if data is missing
            float taperRatio = river.SourceWidth > 0 && river.Width > 0
                ? Math.Clamp(river.SourceWidth / river.Width, 0.1f, 0.7f)
                : 0.3f;
            float sourceCk3Px = mouthCk3Px * taperRatio;

            float degPerCk3Px = map.JsonMap.mapCoordinates.lonT / Map.MapWidth;
            return (
                sourceGeoHW: Math.Max(sourceCk3Px * degPerCk3Px / 2f, 1e-6f),
                mouthGeoHW:  Math.Max(mouthCk3Px  * degPerCk3Px / 2f, 1e-6f)
            );
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

            // New pieces inherit the pre-split cell's full Neighbors array verbatim
            // (see CloneLandCell). Without re-verifying, each piece falsely claims
            // every original neighbour even though only one piece actually borders
            // each. The reciprocal side (the original neighbour's Neighbors) IS
            // verified geometrically below, producing one-way edges where a piece
            // declares a neighbour that doesn't declare it back. To close the loop
            // we also re-verify new pieces' inherited neighbours against geometry.
            var newPieceIds = new HashSet<int>(replacements.Values.SelectMany(v => v));

            foreach (var cell in map.Cells.Values)
            {
                bool isNewPiece            = newPieceIds.Contains(cell.Id);
                bool hasReplacedNeighbour  = cell.Neighbors.Any(n => replacements.ContainsKey(n));
                if (!isNewPiece && !hasReplacedNeighbour) continue;

                var cellPoly = GetPolygon(cell.Id);
                if (cellPoly == null) continue;

                var newNeighbors = new List<int>();
                foreach (int nId in cell.Neighbors)
                {
                    if (replacements.TryGetValue(nId, out var replacingIds))
                    {
                        // Old neighbour was split — keep only pieces that still share an edge.
                        foreach (int replacingId in replacingIds)
                        {
                            var replacingPoly = GetPolygon(replacingId);
                            if (replacingPoly == null) continue;
                            var shared = cellPoly.Intersection(replacingPoly);
                            if (!shared.IsEmpty && (int)shared.Dimension >= (int)Dimension.Curve)
                                newNeighbors.Add(replacingId);
                        }
                    }
                    else if (isNewPiece)
                    {
                        // Inherited neighbour from the pre-split parent — verify it still borders this piece.
                        var nbrPoly = GetPolygon(nId);
                        if (nbrPoly == null) continue;
                        var shared = cellPoly.Intersection(nbrPoly);
                        if (!shared.IsEmpty && (int)shared.Dimension >= (int)Dimension.Curve)
                            newNeighbors.Add(nId);
                    }
                    else
                    {
                        // Original cell, original neighbour, neither was split — unchanged.
                        newNeighbors.Add(nId);
                    }
                }
                cell.Neighbors = newNeighbors.Distinct().ToArray();
            }
        }

        // [SnapSharedBoundaries removed] We tried mutually inserting vertices between
        // adjacent land/river polygons so the coast walk's vertex-matching would find
        // them. It didn't move the audit metric. The right fix turned out to be in the
        // heightmap algorithm itself: bypass land/sea vertex matching for river cells
        // and emit their polygon edges directly as constraint segments. See
        // HeightmapAlgorithm.cs `Phase 3': river polygon edges as direct constraints`.
    }
}
