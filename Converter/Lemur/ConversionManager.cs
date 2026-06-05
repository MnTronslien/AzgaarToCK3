namespace Converter.Lemur
{
    using System.Diagnostics;
    using Converter.Lemur.Entities;
    using Converter.Lemur.Deserialization;
    using Converter.Lemur.Fields;
    using Converter.Lemur.Governments;
    using Converter.Lemur.Graphs;
    using Converter.Lemur.Rivers;
    using Converter.Lemur.Provinces;
    using Converter.Lemur.Writers;
    using ImageMagick;
    using static Converter.Lemur.Entities.Cell;

    public class ConversionManager
    {


        // Class members and methods go here

        public static async Task DumpCellsAfterRivers(string outputPath)
        {
            var map = await InitializeMapWithAzgaarData();
            Logger.Info($"{map} has been loaded.");

            LinkCellsToBurgs(map);

            var skipReason = GetRiversSkipReason(noRiversFlag: false);
            if (skipReason != null)
            {
                Logger.Info($"Rivers skipped: {skipReason} — dumping cells without river modification.");
            }
            else
            {
                map.Rivers = RiverLoader.LoadRivers(map.JsonMap, Settings.Instance.MajorRiverThreshold);
                MajorRiverInserter.InsertMajorRivers(map, Settings.Instance.MajorRiverThreshold);
                int majorCount = map.Rivers.Count(r => r.IsMajor(Settings.Instance.MajorRiverThreshold));
                Logger.Info($"Major rivers inserted ({majorCount} rivers).");
            }

            var mc = map.JsonMap.mapCoordinates;
            var coords = new Writers.CellDump.MapCoords(mc.lonW, mc.lonT, mc.latS, mc.latT);
            Writers.CellDump.Write(outputPath, map.Cells!, coords);
            Logger.Info($"Dumped {map.Cells!.Count} cells → {outputPath}");
        }

        public async static Task Run(bool noRivers = false)
        {
            var map = await InitializeMapWithAzgaarData();
            Logger.Info($"{map} has been loaded.");

            // Resolve global seed once — stored back into settings so any run can be reproduced
            if (!Settings.Instance.Seed.HasValue)
                Settings.Instance.Seed = Random.Shared.Next();
            Logger.Info($"Converter seed: {Settings.Instance.Seed.Value} (use --seed to reproduce)");

            // Derive land-cell counts per religion/culture from the GeoJSON cell graph.
            // FaithManager uses the religion counts to prune zero-cell faiths (replacing the
            // unreliable Azgaar JSON `r.cells` field). Culture counts are logged for parity
            // but not used as a filter — CultureManager does not currently prune zero-cell
            // cultures (see analysis 2026-05-26: no orphan-culture symptom observed).
            CellDistribution.Result cellDist;
            using (var _ = OperationTimer.Start("Counting cells per religion/culture"))
                cellDist = await CellDistribution.ComputeAsync(map.Cells!);
            Logger.Info(
                $"Cell distribution: {cellDist.ReligionCellCounts.Count} religions and " +
                $"{cellDist.CultureCellCounts.Count} cultures have at least one land cell.");

            map.Faiths = FaithManager.Build(map.JsonMap.pack.religions, cellDist.ReligionCellCounts);
            map.Cultures = CultureManager.Build(map.JsonMap.pack.cultures, Settings.Instance.Seed!.Value);

            // ✅ Visualization checkpoint 1: Raw cells
            await ImageUtility.DrawCells(map.Cells!.Values.ToList(), map);
            await ImageUtility.DrawCellsWithNeighborLines(map.Cells!.Values.ToList(), map);

            LinkCellsToBurgs(map);

            // Load and process rivers — always writes rivers.png (blank if rivers are unavailable)
            var riversSkipReason = GetRiversSkipReason(noRivers);
            if (riversSkipReason != null)
            {
                Logger.Info($"Rivers skipped: {riversSkipReason}");
                await RiverImageGenerator.DrawBlankRiversImage(map);
            }
            else
            {
                map.Rivers = RiverLoader.LoadRivers(map.JsonMap, Settings.Instance.MajorRiverThreshold);

                // Phase 1: Draw minor rivers to rivers.png using pure A* approach
                using (var _riverMinor = OperationTimer.Start("Total minor rivers A*"))
                {
                    await RiverImageGenerator.DrawRiversImage(
                        map.Rivers,
                        Settings.Instance.MajorRiverThreshold,
                        map);
                }

                // Phase 2: Insert major river cells (must run before barony formation)
                using (var _riverMajor = OperationTimer.Start("Total major river insertion (phases 1–4)"))
                {
                    MajorRiverInserter.InsertMajorRivers(map, Settings.Instance.MajorRiverThreshold);
                }

                // Debug: visualize cells after river insertion (plain + neighbors + control points)
                // Gated to Verbose: these 3 images cost 4–5 min on large maps
                if (Settings.Instance.LogLevel <= LogLevel.Verbose)
                {
                    var majorRivers = map.Rivers!.Where(r => r.IsMajor(Settings.Instance.MajorRiverThreshold)).ToList();
                    await Task.WhenAll(
                        ImageUtility.DrawCells(map.Cells!.Values.ToList(), map, "1b_cells_post_rivers.png"),
                        ImageUtility.DrawCellsWithNeighborLines(map.Cells!.Values.ToList(), map, "1b_cells_neighbors_post_rivers.png"),
                        ImageUtility.DrawMajorRiverControlPoints(majorRivers, map));
                }
            }

            using (var _ = OperationTimer.Start("GenerateDuchies")) GenerateDuchies(map);
            using (var _ = OperationTimer.Start("GenerateBaronies")) GenerateBaronies(map);

            using (var _ = OperationTimer.Start("AssignCellsToBaronies")) AssignCellsToBaronies(map);

            GenerateWastelandProvinces(map);
            AssertEveryLandCellIsAssignedToABurg(map);

            ComputeDistanceToCoast(map);
            using (var _ = OperationTimer.Start("GenerateSeaZones")) GenerateSeaZones(map);

            CreateFarSeaZones(map);

            // ✅ Visualization checkpoint 2: Sea zones + Baronies
            AssignProvinceColors(map);
            await ShowSeaZones(map);
            using (var _ = OperationTimer.Start("DrawProvincesImage")) await ShowBaronies(map);

            var w = Settings.Instance.Writers;

            if (w.TerrainMasks && !w.Heightmap)
            {
                Logger.Warning("TerrainMasks writer is enabled but Heightmap writer is disabled. " +
                               "TerrainMasks depends on map.HeightmapPixels populated by HeightmapWriter; " +
                               "without it, the splatmap will silently fall back to biome-only output " +
                               "(no steepness-based hills/mountain weighting). Enable the Heightmap writer.");
            }

            if (w.DefinitionCsv)
            {
                using var _ = OperationTimer.Start("Writing definition.csv");
                await DefinitionCsvWriter.Write(map.AllProvinces!, Settings.OutputDirectory);
            }

            var seaZoneIndices = map.SeaZones!
                .Concat(map.FarSeaZones!)
                .Select(sz => map.AllProvinces!.IndexOf(sz) + 1);
            var wastelandIndices = map.Wastelands!
                .Select(w2 => map.AllProvinces!.IndexOf(w2) + 1);
            var farSeaZoneIndices = map.FarSeaZones!
                .Select(fz => map.AllProvinces!.IndexOf(fz) + 1);
            var riverProvinceIndices = map.MajorRiverProvinces
                .Select(rp => map.AllProvinces!.IndexOf(rp) + 1);
            if (w.DefaultMap)
            {
                using var _ = OperationTimer.Start("Writing default.map");
                await DefaultMapWriter.Write(seaZoneIndices, wastelandIndices, farSeaZoneIndices, riverProvinceIndices, Settings.OutputDirectory);
            }

            GenerateBaronyAdjacency(map);
            GenerateCounties(map);

            using (var _ = OperationTimer.Start("HolySiteFactory.Build")) map.HolySites = HolySiteFactory.Build(map);
            Logger.Info($"Built {map.HolySites.Count} holy sites.");

            // ✅ Visualization checkpoint 3: Counties
            if (Settings.Instance.LogLevel <= LogLevel.Debug)
                await ShowCounties(map);

            using (var _ = OperationTimer.Start("GenerateEmpires")) GenerateEmpires(map);
            using (var _ = OperationTimer.Start("GenerateKingdoms")) GenerateKingdoms(map);

            MergeTinyKingdoms(map); //Adjust Kingdoms
            MergeTinyEmpires(map); //Adjust Empires

            // Cull any kingdoms/empires that survived the merge passes with no children.
            // These are isolated landless titles that would create orphan CK3 titles.
            int culledKingdoms = map.Kingdoms.RemoveAll(k => !k.Duchies.Any());
            if (culledKingdoms > 0)
                Logger.Info($"Culled {culledKingdoms} kingdom(s) with 0 duchies after merge.");

            int culledEmpires = map.Empires.RemoveAll(e => !e.Kingdoms.Any());
            if (culledEmpires > 0)
                Logger.Info($"Culled {culledEmpires} empire(s) with 0 kingdoms after merge.");

            // Resolve CK3 governments on Kingdom + Duchy in two independent passes — must run after
            // MergeTinyKingdoms + cull so map.Kingdoms is final (don't resolve for kingdoms that
            // will be dissolved).
            GovernmentResolver.Resolve(map);

            AssignCapitals(map);

            // ✅ Visualization checkpoint 4: Final hierarchy
            if (Settings.Instance.LogLevel <= LogLevel.Debug)
            {
                await ShowDuchies(map);
                await ShowKingdoms(map);
                await ShowEmpires(map);
            }

            DeFactoHierarchyBuilder.Build(map);
            CharacterFactory.CreateAndAssignAll(map);

            TitleTreeDebugger.PrintTrees(map);
            TitleTreeDebugger.PrintDeJureTrees(map);
            TitleTreeDebugger.PrintCharacterDomains(map);

            // Pre-writer derived-data pass. Both steps populate fields on existing entities
            // (Cell.Roughness, Barony.Ck3Terrain) so downstream writers can read them without
            // depending on each other's run order or computing the same signal twice.
            using (var _ = OperationTimer.Start("Computing cell roughness"))
            {
                var roughness = CellRoughnessField.Compute(map.Cells!);
                foreach (var (cellId, value) in roughness)
                    if (map.Cells!.TryGetValue(cellId, out var c)) c.Roughness = value;
                if (roughness.Count > 0)
                    Logger.Info($"Cell roughness — avg={roughness.Values.Average():F3} over {roughness.Count} land cells");
            }
            using (var _ = OperationTimer.Start("Assigning province terrain"))
                BaronyTerrainAssigner.Assign(map);
            await TerrainDebugImage.Write(map);

            // Traditions are assigned here (not in CultureManager.Build) so they can gate on the
            // canonical Barony.Ck3Terrain just computed above.
            using (var _ = OperationTimer.Start("Assigning culture traditions"))
            {
                var cultureTerrain = CultureTerrainProfiler.Compute(map);
                CultureTraditionAssigner.Assign(map, cultureTerrain, Settings.Instance.Seed!.Value);
            }

            // Faith tenets are deferred here (not in FaithManager.Build) for the same reason as
            // traditions: they gate on the canonical Barony.Ck3Terrain just computed above.
            using (var _ = OperationTimer.Start("Assigning faith tenets"))
            {
                var faithTerrain = FaithTerrainProfiler.Compute(map);
                FaithTenetAssigner.Assign(map, faithTerrain, Settings.Instance.Seed!.Value);
            }

            Logger.Section("Writing CK3 mod files");

            if (w.Adjacencies)
            {
                using var _ = OperationTimer.Start("Writing adjacencies.csv");
                await AdjacenciesCsvWriter.Write(Settings.OutputDirectory);
            }
            if (w.LandedTitles)
                await Task.WhenAll(
                    LandedTitlesWriter.Write(map, Settings.OutputDirectory),
                    TitleLocalizationWriter.Write(map, Settings.OutputDirectory));
            using (var _ = OperationTimer.Start("Writing mod descriptor")) await ModDescriptorWriter.Write(Settings.Instance.ModName, Settings.Instance.ModsDirectory, Settings.OutputDirectory);
            await LandlessTitleStubsWriter.Write(Settings.OutputDirectory);
            await VanillaEventOverridesWriter.Write(Settings.OutputDirectory);
            if (w.MapDefines)
            {
                using var _ = OperationTimer.Start("Writing map defines");
                await MapDefinesWriter.Write(Settings.OutputDirectory);
                await BenchmarkDefinesWriter.Write(map, Settings.OutputDirectory);
            }
            await MapTableWriter.Write(Settings.OutputDirectory);
            // re-supply the layer/map-table defs masked by our gfx/map/map_object_data replace_path
            await MapObjectDataWriter.Write(Settings.Instance.Ck3Directory, Settings.OutputDirectory);
            // hide the bookmark portrait; its render crashes the main menu on a generated map
            await FrontendGuiWriter.Write(Settings.OutputDirectory);
            // surround_map / water / vegetation, so the standalone map shows no vanilla geography
            await MapRenderAssetsWriter.Write(Settings.Instance.Ck3Directory, Settings.OutputDirectory);
            if (w.ProvinceTerrain)
            {
                using var _ = OperationTimer.Start("Writing province terrain");
                await ProvinceTerrainWriter.Write(map, Settings.OutputDirectory);
            }
            // Heightmap MUST run before TerrainMasks: HeightmapWriter populates map.HeightmapPixels
            // and map.HeightmapF, which TerrainMaskWriter feeds into SplatmapBuilder so that
            // steepness-based materials (hills, mountain) can compute their weights.
            if (w.Heightmap)
                await HeightmapWriter.Write(map, Settings.OutputDirectory);
            if (w.TerrainMasks)
            {
                var terrainMasks = TerrainMaskPreparer.Prepare(map);
                await TerrainMaskWriter.Write(terrainMasks, map, Settings.Instance.Ck3Directory, Settings.OutputDirectory);
            }
            if (w.Flatmap)
                await FlatmapWriter.Write(map, Settings.Instance.AzgaarSvgPath, Settings.OutputDirectory);
            if (w.MapStaticFiles)
                await StaticFilesWriter.Write(Settings.OutputDirectory);
            if (w.Religion)
            {
                using var _ = OperationTimer.Start("Writing religion files (copy from CK3)");
                await ReligionWriter.Write(Settings.Instance.Ck3Directory, Settings.OutputDirectory);
            }
            if (w.Faiths)
                await FaithWriter.Write(map, Settings.OutputDirectory);
            if (w.Cultures)
                await CultureWriter.Write(map, Settings.OutputDirectory);
            if (w.GeographicalRegions)
                await GeographicalRegionWriter.Write(map, Settings.OutputDirectory);
            if (w.ProvinceHistory)
            {
                await ProvinceHistoryWriter.Write(map, Settings.OutputDirectory);
                ProvinceHistoryValidator.Validate(
                    Helper.GetPath(Settings.OutputDirectory, "history", "provinces"),
                    out var provinceHistoryErrors);
                foreach (var e in provinceHistoryErrors)
                    Logger.Warning(e);
            }
            if (w.Locators)
                await LocatorWriter.Write(map, Settings.OutputDirectory);
            if (w.Characters)
            {
                using var _ = OperationTimer.Start("Writing characters");
                await CharacterWriter.Write(map, Settings.OutputDirectory);
            }
            if (w.TitleHistory)
            {
                using var _ = OperationTimer.Start("Writing title history");
                await TitleHistoryWriter.Write(map, Settings.OutputDirectory);
            }
            // after TitleHistory, so kingdom holders exist
            if (w.Bookmark)
                await BookmarkWriter.Write(map, Settings.OutputDirectory);

            Logger.Success();

            if (Settings.Instance.GenerateDebugImages)
            {
                ImageUtility.OpenAllImages();
            }
        }



        private static void GenerateWastelandProvinces(Map map)
        {
            //For each province flagged as not having a burg, generate a wasteland province

        }

        private static void ComputeDistanceToCoast(Map map)
        {
            // BFS outward from all land cells.
            // Sea cells adjacent to a land cell get distance 1, their sea neighbours get 2, etc.
            var queue = new Queue<Cell>();

            // Seed: all sea cells directly neighbouring a land cell get distance 1
            foreach (var cell in map.Cells!.Values)
            {
                if (Cell.IsDryLand(cell.Type)) continue;

                foreach (var neighborId in cell.Neighbors)
                {
                    if (map.Cells.TryGetValue(neighborId, out var neighbor) && Cell.IsDryLand(neighbor.Type))
                    {
                        cell.DistanceToCoast = 1;
                        queue.Enqueue(cell);
                        break;
                    }
                }
            }

            // BFS expansion
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighborId in current.Neighbors)
                {
                    if (!map.Cells.TryGetValue(neighborId, out var neighbor)) continue;
                    if (Cell.IsDryLand(neighbor.Type)) continue;
                    if (neighbor.DistanceToCoast != 0) continue; // already visited

                    neighbor.DistanceToCoast = current.DistanceToCoast + 1;
                    queue.Enqueue(neighbor);
                }
            }
        }

        private static void GenerateSeaZones(Map map)
        {

            Logger.Section("Generating sea zones");
            var seaCellsById = map.Cells!.Values
                .Where(c => !Cell.IsDryLand(c.Type) && !c.IsRiverCell)
                .ToDictionary(c => c.Id);

            var unassigned = new HashSet<int>(seaCellsById.Keys);
            var seaZones = new List<SeaZone>();
            int nextId = 1;

            while (unassigned.Count > 0)
            {
                int seedId = unassigned.Min();
                var seed = seaCellsById[seedId];

                var zoneCells = new List<Cell> { seed };
                seed.Province = null; // will be set by SeaZone constructor
                unassigned.Remove(seedId);
                int totalArea = seed.Area;
                var zoneIds = new HashSet<int> { seedId };

                while (totalArea < Settings.Instance.SeaZoneTargetArea)
                {
                    var candidates = zoneCells
                        .SelectMany(c => c.Neighbors)
                        .Distinct()
                        .Where(id => unassigned.Contains(id))
                        .Select(id => seaCellsById[id])
                        .ToList();

                    if (candidates.Count == 0) break;

                    // Pick cell with most neighbors already in zone (cohesion-first); distance to seed is tiebreaker
                    var best = candidates
                        .OrderByDescending(c => c.Neighbors.Count(n => zoneIds.Contains(n)))
                        .ThenBy(c => c.DistanceSquared(seed))
                        .First();

                    zoneCells.Add(best);
                    zoneIds.Add(best.Id);
                    unassigned.Remove(best.Id);
                    totalArea += best.Area;
                }

                var zone = new SeaZone(nextId++, zoneCells);
                zone.TotalArea = totalArea;
                seaZones.Add(zone);
            }

            // Post-process: merge undersized zones
            var undersized = seaZones.Where(z => z.TotalArea < Settings.Instance.SeaZoneMinimumArea).ToList();
            foreach (var zone in undersized)
            {
                if (!seaZones.Contains(zone)) continue; // already merged away

                var borderCounts = new Dictionary<SeaZone, int>();
                foreach (var cell in zone.Cells)
                {
                    foreach (var neighborId in cell.Neighbors)
                    {
                        if (map.Cells.TryGetValue(neighborId, out var neighbor) &&
                            neighbor.Province is SeaZone sz && sz != zone)
                        {
                            borderCounts.TryGetValue(sz, out int count);
                            borderCounts[sz] = count + 1;
                        }
                    }
                }

                if (borderCounts.Count > 0)
                {
                    var mergeTarget = borderCounts
                        .OrderByDescending(kv => kv.Value)
                        .ThenByDescending(kv => kv.Key.TotalArea)
                        .First().Key;

                    foreach (var cell in zone.Cells)
                    {
                        cell.Province = mergeTarget;
                        mergeTarget.Cells.Add(cell);
                    }
                    mergeTarget.TotalArea += zone.TotalArea;
                    seaZones.Remove(zone);
                }
                else
                {
                    zone.IsImpassable = true;
                    Logger.Debug($"Sea zone {zone.Name} is isolated and marked impassable (area={zone.TotalArea})"); //BUG: zone.Name is not set at this point, so it will print as empty. Consider assigning temporary IDs to zones earlier for better logging.
                }
            }

            // Assign placeholder names
            for (int i = 0; i < seaZones.Count; i++)
                seaZones[i].Name = $"sea_{i + 1}";

            map.SeaZones = seaZones;
            Logger.Info($"Generated {map.SeaZones.Count} sea zones after merging undersized zones.");
        }
        private static void AssertEveryLandCellIsAssignedToABurg(Map map)
        {
            //Every cell should at this point either be assigned to a barony or be assigned to the wastelands
            var landCells = map.Cells!.Where(c => IsDryLand(c.Value.Type)).ToList();

            Logger.Debug("Asserting that every land cell is assigned to a barony or wasteland");
            Logger.Debug($"There are {landCells.Count} land cells");

            bool listPassable = true;

            //Now run through every land cell and see if it is assigned to a barony or wasteland
            foreach (var cell in landCells)
            {
                var cellPassable = false;
                if (cell.Value.Province is Barony)
                {
                    cellPassable = true;
                }
                // Check if the cell is in one of the wastelands
                else if (cell.Value.Province is Wasteland)
                {
                    cellPassable = true;
                }
                if (!cellPassable)
                {
                    listPassable = false;
                    Logger.Info($"Failed: Cell {cell.Value.Id}, AzProvince {cell.Value.AzProvince}, State {cell.Value.State}");
                }
            }

            //If any cell is not assigned to a barony or wasteland, throw an exception
            if (!listPassable)
            {
                throw new Exception("Not all land cells are assigned to a barony or wasteland");
            }
            else
            {
                Logger.Debug("All land cells are assigned to a barony or wasteland");
            }

        }

        private static void AssignProvinceColors(Map map)
        {
            List<IProvince> allProvinces = new();
            allProvinces.AddRange(map.Baronies!);
            allProvinces.AddRange(map.Wastelands!);
            allProvinces.AddRange(map.MajorRiverProvinces);
            allProvinces.AddRange(map.SeaZones!);
            allProvinces.AddRange(map.FarSeaZones!);

            // Start at 1: index 0 is black (reserved/undefined in CK3)
            for (int i = 0; i < allProvinces.Count; i++)
                allProvinces[i].Color = Helper.GetColor(i + 1, allProvinces.Count + 1);

            map.AllProvinces = allProvinces;
        }
        /// <summary>
        /// Far Sea Zones are a special category of sea zones that are manually created to cover any area of teh province map that azgaar data does not cover.
        /// </summary> 
        private static void CreateFarSeaZones(Map map)
        {
            int n = Settings.Instance.FarSeaZoneCount;
            map.FarSeaZones = new List<SeaZone>(n);
            for (int i = 0; i < n; i++)
            {
                var zone = new SeaZone(i + 1, new List<Cell>())
                {
                    Name = $"far_sea_{i + 1}",
                    IsImpassable = true
                };
                map.FarSeaZones.Add(zone);
            }
            Logger.Info($"Created {n} far sea zones to make sure that everything is covered.");
        }
        private static void AssignUniqueColorsToCounties(Map map)
        {
            //We start at 10 because we want to avoid black
            for (int i = 0; i < map.Counties!.Count; i++)
            {
                map.Counties[i].Color = Helper.GetColor(i + 10, map.Counties.Count + 20); //pad it just in case
            }
        }

        private static void AssignCellsToBaronies(Map map)
        {
            Logger.Section("Assigning cells to baronies");
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
            Parallel.ForEach(map.Duchies!, parallelOptions, duchy =>
            {
                var duchySw = System.Diagnostics.Stopwatch.StartNew();
                var allCells = duchy.GetAllCells();
                Logger.Verbose($"Processing Cells for Duchy {duchy.Name} with {allCells.Count} cells and {duchy.Baronies.Count} baronies");

                var baronies = allCells
                    .Select(c => c.Burg).Where(b => b != null)
                    .Select(b => b!.Barony).Distinct()
                    .OrderByDescending(b => b!.burg!.Population)
                    .ToList();

                // O(1) duchy-local cell lookup — prevents cross-duchy frontier expansion
                var duchyCells = allCells.ToDictionary(c => c.Id);

                // Seed: one cell per barony (the burg's own cell)
                foreach (var barony in baronies)
                {
                    if (barony.burg.Cell == null)
                        throw new Exception($"Burg {barony.burg.Name} has no cell");
                    barony.Cells.Add(barony.burg.Cell);
                    barony.burg.Cell.Province = barony;
                }

                // Unassigned countryside cells (no burg)
                var unassigned = new HashSet<int>(allCells.Where(c => c.Burg == null).Select(c => c.Id));

                // Per-barony priority queue seeded with burg cell's unassigned duchy neighbours
                // Priority = distance² to burg (min-heap → nearest cell first)
                var frontiers = baronies.Select(b =>
                {
                    var pq = new PriorityQueue<Cell, float>();
                    foreach (var nId in b.burg.Cell!.Neighbors)
                        if (unassigned.Contains(nId) && duchyCells.TryGetValue(nId, out var nc))
                            pq.Enqueue(nc, b.burg.Cell.DistanceSquared(nc));
                    return (barony: b, frontier: pq);
                }).ToList();

                // Multi-source Voronoi expansion: each barony claims one nearest cell per round
                while (unassigned.Count > 0)
                {
                    bool anyProgress = false;
                    foreach (var (barony, frontier) in frontiers)
                    {
                        // Pop nearest unassigned cell (lazy-delete stale entries)
                        Cell? cell = null;
                        while (frontier.Count > 0)
                        {
                            var candidate = frontier.Peek();
                            if (unassigned.Contains(candidate.Id)) { cell = frontier.Dequeue(); break; }
                            frontier.Dequeue(); // already assigned by another barony
                        }
                        if (cell == null) continue;

                        unassigned.Remove(cell.Id);
                        barony.Cells.Add(cell);
                        cell.Province = barony;
                        anyProgress = true;

                        // Expand frontier with this cell's unassigned duchy neighbours
                        foreach (var nId in cell.Neighbors)
                            if (unassigned.Contains(nId) && duchyCells.TryGetValue(nId, out var nc))
                                frontier.Enqueue(nc, barony.burg.Cell!.DistanceSquared(nc));
                    }

                    if (!anyProgress && unassigned.Count > 0)
                    {
                        // Island fallback: find the closest (barony, isolated-cell) pair and seed BFS there
                        var shortestDistancePair = unassigned
                            .Select(id => duchyCells[id])
                            .Select(island => new
                            {
                                Island = island,
                                ClosestBarony = baronies
                                    .Select(b => new { Barony = b, Distance = b.burg.Cell!.DistanceSquared(island) })
                                    .OrderBy(b => b.Distance)
                                    .First()
                            })
                            .OrderBy(pair => pair.ClosestBarony.Distance)
                            .First();

                        var island = shortestDistancePair.Island;
                        var closestBarony = shortestDistancePair.ClosestBarony.Barony;

                        unassigned.Remove(island.Id);
                        closestBarony.Cells.Add(island);
                        island.Province = closestBarony;

                        // Seed the barony's frontier so BFS can propagate from this island
                        var closestFrontier = frontiers.First(f => f.barony == closestBarony).frontier;
                        foreach (var nId in island.Neighbors)
                            if (unassigned.Contains(nId) && duchyCells.TryGetValue(nId, out var nc))
                                closestFrontier.Enqueue(nc, closestBarony.burg.Cell!.DistanceSquared(nc));
                    }
                }

                if (Settings.Instance.LogLevel <= LogLevel.Info && unassigned.Count > 0)
                    Logger.Info($"{unassigned.Count} countryside cells in Duchy {duchy.Name} could not be assigned to a barony.");

                duchySw.Stop();
                if (duchySw.Elapsed.TotalMilliseconds > 500)
                    Logger.Info($"[Timer] Cell assignment > {duchy.Name}: {duchySw.Elapsed.TotalSeconds:F3}s (slow)");
                Logger.Info($"Completed Cell assignment for Duchy {duchy.Name}");
            });
        }

        private static void LinkCellsToBurgs(Map map)
        {
            Logger.Section("Linking cells to burgs");

            // map.Burgs contains only real burgs (ids 1..N) — no dummy at 0, no Skip needed
            foreach (var burg in map.Burgs!)
            {
                if (burg.Value.Removed)
                {
                    continue;
                }
                burg.Value.Cell = map.Cells![burg.Value.Cell_id];
                //and reverse
                map.Cells![burg.Value.Cell_id].Burg = burg.Value;

                Logger.Verbose($"Burg {burg.Value.Name} <<=>> {burg.Value.Cell_id} Cell");
            }
            Logger.Info($"Cell linking complete: {map.Burgs.Count} burgs linked to cells");
        }

        private static async Task<Map> InitializeMapWithAzgaarData()
        {
            Logger.Section("Loading Azgaar data");

            // Load directly into Lemur DTOs (no upstream types!)
            var jsonMap = await AzgaarLoader.LoadJsonAsync(Settings.Instance.InputJsonPath);
            var geoMap = await AzgaarLoader.LoadGeoJsonAsync(Settings.Instance.InputGeojsonPath);

            var map = new Map
            {
                GeoMap = geoMap,
                JsonMap = jsonMap,
                Settings = Settings.Instance
            };

            // Build cells and burgs immediately
            map.Cells = AzgaarLoader.BuildCells(geoMap, jsonMap);
            map.Burgs = AzgaarLoader.BuildBurgs(jsonMap, map.Cells);

            return map;
        }


        /// <summary>
        /// Generate a list of baronies with the bare minimum of information
        /// </summary>
        /// <param name="burgs">Burgs to base the baronies on</param>
        /// <returns>A list of baronies</returns>
        private static void GenerateBaronies(Map map)
        {
            Logger.Section("Generating baronies");
            // Next we instanciate a list of baronies. Since we know the final size of the list we can pre allocate the memory
            List<Barony> baronies = new(map.Burgs!.Count);
            // map.Burgs contains only real burgs (ids 1..N) — no dummy at 0, no filter needed
            foreach (var burg in map.Burgs)
            {
                if (burg.Value.Removed)
                {
                    Logger.Info($"Skipping barony {burg.Value.Name} as it has been marked as removed");
                    continue;
                }
                if (burg.Value.Cell!.AzProvince == 0 && burg.Value.Cell!.State == 0)
                {
                    Logger.Info($"Skipping barony {burg.Value.Name} as it is in wastelands");
                    continue;
                }

                var barony = new Barony(burg.Value);
                barony.burg.Cell!.Duchy!.Baronies.Add(barony);
                // if the burg is a province capital, flagg it as such
                baronies.Add(barony);


            }
            map.Baronies = baronies;

            foreach (var barony in baronies)
                Logger.Verbose($"Barony {barony.Id} {barony.Name}");

            Logger.Info($"Generated {baronies.Count} baronies");
        }

        /// <summary>
        /// Adds <paramref name="cells"/> to <paramref name="map"/> as a Duchy if at least one
        /// cell has a burg; otherwise treats the cells as a Wasteland. Single source of truth
        /// for the "valid Duchy" invariant — both code paths in <see cref="GenerateDuchies"/>
        /// route through here so a future duchy-creating path cannot silently skip the check.
        /// </summary>
        private static void AddDuchyOrWasteland(
            List<Duchy> duchies, Map map, int id, List<Cell> cells, string name)
        {
            if (!cells.Any(c => c.Burg != null))
            {
                Logger.Info($"Skipping {name} (id={id}) — no burgs in cells; treating as Wasteland.");
                map.Wastelands?.Add(new Wasteland(id, cells, name));
                return;
            }
            duchies.Add(new Duchy(id, cells, name));
        }

        private static void GenerateDuchies(Map map)
        {
            //Duchies are based on Azgaar Provinces. Except in the case of the wastelands where parts of a state can be assigned to the wastelands province (0) and we must generate a new from the state.
            Logger.Section("Generating duchies");
            //First we group the cells by province
            var cellsByProvince = map.Cells!.GroupBy(c => c.Value.AzProvince).OrderBy(g => g.Key);

            List<Duchy> duchies = new(cellsByProvince.Count() + 10); // We allocate a bit more than we need to avoid resizing the list in case we end up splitting wastelands

            foreach (var province in cellsByProvince)
            {
                //look up the province in the json data
                var provinceData = map.JsonMap.pack.provinces.First(p => p.i == province.Key);

                //sanity check that the first cell in the province matches the province data
                if (province.First().Value.AzProvince != province.Key)
                {
                    throw new Exception($"Province {province.Key} does not match the first cell in the province: {province.First().Value.AzProvince}");
                }

                //if Province has no cells, skip it
                if (!province.Any())
                {
                    //log the name of the province skipped
                    Logger.Info($"Skipping province {provinceData.name} as it has no cells");
                    continue;
                }

                // Burg-presence check now lives in AddDuchyOrWasteland (called below for the
                // normal path and inside the wastelands-province state loop). A province / state
                // with no burg-bearing cells falls back to a Wasteland with that province / state's name.
                if (province.Key == 0)
                {
                    // Handle the wastelands province, it might actually contain cells assigned to states
                    provinceData = new AzgaarProvince(i: 0, name: "Wastelands", burg: 0, state: 0);
                    var WastelandCellsByState = province.GroupBy(c => c.Value.State).OrderBy(g => g.Key).OrderBy(g => g.Key);
                    //now for each state in the wastelands province, generate a duchy

                    foreach (var state in WastelandCellsByState)
                    {
                        // Handle the first group because that is actually the wastelands province
                        if (state.Key == 0)
                        {
                            map.Wastelands?.Add(new Wasteland(0, state.Select(c => c.Value).ToList(), "Wastelands"));
                            continue;
                        }

                        //look up the state in the json data, we will reuse the state name as the duchy name
                        var stateData = map.JsonMap.pack.states.First(s => s.i == state.Key);
                        AddDuchyOrWasteland(duchies, map, stateData.i,
                            state.Select(c => c.Value).ToList(), stateData.name);
                    }
                    Logger.Info($"Some cells in the azgaar wastelands province are assigned to states, we generated {duchies.Count} duchies from these states to preserve the state structure as much as possible. The rest of the cells are assigned to the Wastelands province.");
                    continue;
                }


                List<Cell> cells = province.Select(c => c.Value).ToList();
                AddDuchyOrWasteland(duchies, map, provinceData.i, cells, provinceData.name);
            }

            map.Duchies = duchies;

            // Assign duchy to each cell directly
            foreach (var duchy in duchies)
            {
                foreach (var cell in duchy.GetAllCells())
                {
                    cell.Duchy = duchy;
                }
            }

            foreach (var duchy in duchies)
                Logger.Verbose($"Duchy {duchy.Id} {duchy.Name} has {duchy.GetAllCells().Count} cells");

            Logger.Info($"Generated {duchies.Count} duchies");
        }

        private static void GenerateCounties(Map map)
        {
            Logger.Section("Generating counties");
            using var _ = OperationTimer.Start("Generating counties");
            //For each duchy, generate a graph of the duchy
            foreach (var duchy in map.Duchies!)
            {
                //Create a graph of the duchy
                Graph graph = new(null!);
                List<BaronyNode> baronyNodes = new();

                //for each barony create a BaronyNode and populate it with the barony and the node
                foreach (var barony in duchy.Baronies)
                {
                    Node node = new() { Name = barony.Name, Population = (int)barony.burg.Population };
                    baronyNodes.Add(new BaronyNode(node, barony));

                    // add the node to the graph
                    graph.AddNode(node);
                }

                //Add the edges to the graph. This is done by looking at the adjacencies of the baronies
                foreach (var baronyNode in baronyNodes)
                {
                    //Resolve what baronies this barony is adjacent to
                    var adjacentBaronies = baronyNode.Barony.Neighbors;

                    //if the adjacent baronies is null, then this barony has no adjacent baronies
                    if (adjacentBaronies == null)
                    {
                        continue;
                    }
                    //then work out what nodes these baronies are represented by
                    var adjacentNodes = baronyNodes.Where(bn => adjacentBaronies.Contains(bn.Barony)).Select(bn => bn.Node).ToList();

                    //add the edge to the graph
                    graph.AddEdge(baronyNode.Node, adjacentNodes);
                }
                //partition the graph into connected components
                var partitions = Graph.PartitionGraph(graph);

                //Each partition is a county, so nearly there. First we translate back from graphs to baronies
                var counties = new List<County>();
                foreach (var partition in partitions)
                {
                    //work out what baronies are in this partition from the BaronyNode
                    var baroniesInPartition = baronyNodes.Where(bn => partition.adjacencyList.ContainsKey(bn.Node)).Select(bn => bn.Barony).ToList();
                    //Create a county from the baronies:

                    // Pick county capital:
                    // Priority 1 — barony whose burg is the Azgaar province capital (province.burg)
                    // Priority 2 — most populous barony
                    var provinces = map.JsonMap.pack.provinces;
                    var provCapBurgId = duchy.Id > 0 && duchy.Id < provinces.Length ? provinces[duchy.Id].burg : 0;
                    var capital = (provCapBurgId > 0
                        ? baroniesInPartition.FirstOrDefault(b => b.burg.id == provCapBurgId)
                        : null)
                        ?? baroniesInPartition.OrderByDescending(b => b.burg.Population).First();

                    var county = new County(IdManager.Instance.GetNextId(), capital.Name, baronies: baroniesInPartition, duchy: duchy, capital: capital);
                    Logger.Debug($"County {county.Name} has {baroniesInPartition.Count} baronies");
                    counties.Add(county);
                }

                //add the counties to the map's list of counties
                map.Counties ??= new();
                map.Counties.AddRange(counties);
                Logger.Debug($"Duchy {duchy.Name} has {counties.Count} counties");
            }
            Logger.Info($"Generated {map.Counties!.Count} counties");


        }


        private static void GenerateEmpires(Map map)
        {
            //Empires will follow culture, unless the option to follow religion is enabled.
            //If the option to follow religion is enabled, the empire will follow religion.

            Logger.Section("Generating empires");
            if (Settings.Instance.EmpireFromCulture)
            {
                map.Empires = ByCulture();
            }
            else
            {
                map.Empires = ByReligion();
            }

            foreach (var empire in map.Empires!)
            {
                Logger.Debug($"Empire {empire.Id} {empire.Name}");
            }



            List<Empire> ByCulture()
            {
                // The culture way:
                // Get every culture in the map, they are packed in the json map
                var cultures = map.JsonMap.pack.cultures.Skip(1).ToArray(); //skip the 0'eth entry, that is wildlands
                                                                            //print each
                Logger.Debug($"Empire From Culture: True — {cultures.Length} cultures");
                foreach (var culture in cultures)
                    Logger.Debug($"  Culture {culture.i}: {culture.name}");

                // For each culture, form an empire
                List<Empire> empires = new();
                foreach (var culture in cultures)
                {
                    Empire empire = new Empire(culture.i, culture.name, null)
                    {
                        Culture = culture
                    };
                    empires.Add(empire);
                    // Kingdoms will self add when created

                }

                return empires;
            }

            List<Empire> ByReligion()
            {
                // The religion way;
                // Get every religion in the map, they are packed in the json map

                // Todo: Religions are interesting because they can have children and parents, 
                // so some religions could be said to belong to the same "family".
                // This is relevant because it could be used to determine which religions are more likely to 
                // form an empire together if they are close to each other and their size alone would make them a 
                // poor candidate for an empire.

                var religions = map.JsonMap.pack.religions.Skip(1).ToArray(); // 0'eth entry is "No religion"
                Logger.Info($"Empire From Culture: False, there are {religions.Length} religions in the map");
                foreach (var religion in religions)
                {
                    Logger.Info($"Religion: {religion}");
                }

                // For each religion, form an empire
                List<Empire> empires = new();
                foreach (var religion in religions)
                {
                    Empire empire = new Empire(religion.i, religion.name, null)
                    {
                        Religion = religion
                    };
                    empires.Add(empire);
                    // Kingdoms will self add when created
                }
                return empires;
            }
        }


        private static void GenerateKingdoms(Map map)
        {
            // Get all the duchies, order them by state, get the state data from the json data and create a kingdom from the state data
            // The kingdom will be named after the state
            Logger.Section("Generating kingdoms");
            var duchiesByState = map.Duchies!.GroupBy(d => d.AzgaarStateId).OrderBy(g => g.Key);
            List<Kingdom> kingdoms = new(duchiesByState.Count()); // preallocate memory for the kingdoms

            foreach (var state in duchiesByState)
            {
                // Skip state 0 (wastelands)
                if (state.Key == 0)
                {
                    Logger.Info($"Skipping state 0 (Wastelands) for kingdom generation");
                    continue;
                }

                // Get the state data from the json data
                var stateData = map.JsonMap.pack.states.FirstOrDefault(s => s.i == state.Key);
                if (stateData == null)
                {
                    Logger.Warning($"Warning: Could not find state data for state {state.Key}, skipping");
                    continue;
                }

                var kingdom = new Kingdom(stateData.i, stateData.name, null, state.ToList());
                kingdoms.Add(kingdom);
                //And depending on if the rule sais to use culture or religion hwne forming empires, add the kingdom to the correct empire
                if (Settings.Instance.EmpireFromCulture)
                {
                    // Get the dominant culture across ALL duchies in the kingdom, not just the first duchy
                    var culture = ((ITitle)kingdom).GetDominantCulture(map);
                    var empire = map.Empires!.FirstOrDefault(e => e.Culture == culture);
                    if (empire == null)
                    {
                        Logger.Warning($"Warning: No empire found for culture {culture.name}, creating orphan kingdom {kingdom.Name}");
                        continue;
                    }
                    empire.Kingdoms.Add(kingdom);
                    kingdom.DeJureParent = empire;
                }
                else
                {
                    // Get the dominant religion across ALL duchies in the kingdom, not just the first duchy
                    var religion = ((ITitle)kingdom).GetDominantReligion(map);
                    var empire = map.Empires!.FirstOrDefault(e => e.Religion == religion);
                    if (empire == null)
                    {
                        Logger.Warning($"Warning: No empire found for religion {religion.name}, creating orphan kingdom {kingdom.Name}");
                        continue;
                    }
                    empire.Kingdoms.Add(kingdom);
                    kingdom.DeJureParent = empire;
                }

            }

            foreach (var kingdom in kingdoms)
            {
                Logger.Debug($"Kingdom {kingdom.Id} {kingdom.Name} has {kingdom.Duchies.Count} duchies");
            }
            // Assign the kingdoms to the map
            map.Kingdoms = kingdoms;

        }

        private static void AssignCapitals(Map map)
        {
            using var _ = OperationTimer.Start("Assigning capitals");
            var provinces = map.JsonMap.pack.provinces;

            // Duchy capitals: province capital burg (province.burg), fallback to most populous barony
            foreach (var duchy in map.Duchies!)
            {
                if (!duchy.Baronies.Any()) continue;
                var provCapBurgId = duchy.Id > 0 && duchy.Id < provinces.Length ? provinces[duchy.Id].burg : 0;
                duchy.Capital = (provCapBurgId > 0
                    ? duchy.Baronies.FirstOrDefault(b => b.burg.id == provCapBurgId)
                    : null)
                    ?? duchy.Baronies.OrderByDescending(b => b.burg.Population).FirstOrDefault();
            }

            // Kingdom capitals: the one burg in the state with burg.Capital == true (state capital)
            foreach (var kingdom in map.Kingdoms!)
            {
                kingdom.Capital = map.Baronies!
                    .FirstOrDefault(b => b.burg.Capital && b.burg.State == kingdom.Id);
                // Fallback: capital of most populous duchy
                if (kingdom.Capital == null)
                    kingdom.Capital = kingdom.Duchies
                        .OrderByDescending(d => d.Baronies.Sum(b => b.burg.Population))
                        .FirstOrDefault()?.Capital;
            }

            // Empire capitals: capital of the most populous child kingdom
            foreach (var empire in map.Empires!)
            {
                empire.Capital = empire.Kingdoms
                    .OrderByDescending(k => k.Duchies
                        .SelectMany(d => d.Baronies)
                        .Sum(b => b.burg.Population))
                    .FirstOrDefault()?.Capital;
            }

            Logger.Info("Assigned capitals. Sample duchy capitals:");
            foreach (var duchy in map.Duchies.Take(5))
                Logger.Info($"  {duchy.Name} → {duchy.Capital?.Name ?? "null"}");
        }

        private static void MergeTinyKingdoms(Map map)
        {

            Logger.Section("Merging tiny kingdoms");
            Logger.Info($"Merging kingdoms that are less than {Settings.Instance.MinimumDuchiesPerKingdom} duchies");


            // Add all kingdoms to the dictionary so we can keep track of if they are mergable or not
            Dictionary<Kingdom, bool> unmergableKingdoms = map.Kingdoms.ToDictionary(k => k, k => false);

            // Now next step we do per empire
            bool mergerOccurred;
            do
            {
                mergerOccurred = false; // Reset the flag at the beginning of each iteration

                foreach (var empire in map.Empires!)
                {
                    var kingdomsToMerge = empire.Kingdoms
                        .Where(k => k.Duchies.Count < Settings.Instance.MinimumDuchiesPerKingdom)
                        .OrderBy(k => k.Duchies.Count)
                        .Where(k => unmergableKingdoms.ContainsKey(k) && unmergableKingdoms[k] == false)
                        .ToList();

                    if (!kingdomsToMerge.Any())
                    {
                        continue;
                    }

                    var TinyKingdom = kingdomsToMerge.First();
                    var adjacentKingdoms = TinyKingdom.GetNeighbours();

                    Logger.Verbose($"[MergeTiny] Evaluating {TinyKingdom.Name} ({TinyKingdom.Duchies.Count} duchies). All raw neighbours:");
                    foreach (var (k, v) in adjacentKingdoms.OrderByDescending(x => x.Value))
                    {
                        bool inEmpire = empire.Kingdoms.Contains(k);
                        bool sharedAncestry = SharesAncestry(TinyKingdom, (Kingdom)k, map);
                        Logger.Verbose($"  {((Kingdom)k).Name}: border={v} inEmpire={inEmpire} sharedAncestry={sharedAncestry}");
                    }

                    if (!adjacentKingdoms.Any())
                    {
                        unmergableKingdoms[TinyKingdom] = true;
                        continue;
                    }

                    // Include same-empire kingdoms and cross-empire kingdoms that share cultural/religious ancestry.
                    // Primary sort: border count (descending). Tiebreaker: shared ancestry (0) before none (1).
                    var sortedAdjacentKingdoms = adjacentKingdoms
                        .Where(k => empire.Kingdoms.Contains(k.Key) || SharesAncestry(TinyKingdom, (Kingdom)k.Key, map))
                        .OrderByDescending(k => k.Value)
                        .ThenBy(k => SharesAncestry(TinyKingdom, (Kingdom)k.Key, map) ? 0 : 1)
                        .Select(k => k.Key)
                        .ToList();

                    if (!sortedAdjacentKingdoms.Any())
                    {
                        unmergableKingdoms[TinyKingdom] = true;
                        continue;
                    }

                    Kingdom mergeTarget = sortedAdjacentKingdoms.First() as Kingdom;
                    Logger.Debug($"[MergeTiny] {TinyKingdom.Name} → {mergeTarget.Name} (border={adjacentKingdoms[mergeTarget]});" +
                        $" candidates: {string.Join(", ", sortedAdjacentKingdoms.Select(k => $"{k.Name}={adjacentKingdoms[k]}[{(empire.Kingdoms.Contains(k) ? "same" : "cross")}]"))})");
                    mergeTarget.Duchies.AddRange(TinyKingdom.Duchies);
                    foreach (var duchy in TinyKingdom.Duchies)
                    {
                        duchy.DeJureParent = mergeTarget;
                    }
                    // Remove the tiny kingdom from the empire
                    empire.Kingdoms.Remove(TinyKingdom);
                    map.Kingdoms.Remove(TinyKingdom);

                    // Since the merge target changed shape, if it was unmergable before, it might be mergable now
                    unmergableKingdoms[mergeTarget] = false;
                    mergerOccurred = true; // A merger was successful, so set the flag to true
                    Logger.Info($"Merged {TinyKingdom.Name} into {mergeTarget.Name}");
                }
            } while (mergerOccurred); // Continue looping as long as a merger occurred in the last iteration

            Logger.Debug($"Kingdoms after merging:");
            foreach (var kingdom in map.Kingdoms)
            {
                Logger.Debug($" - {kingdom.Name} has {kingdom.Duchies.Count} duchies");
            }
        }

        private static bool SharesAncestry(Kingdom a, Kingdom b, Map map)
        {
            if (Settings.Instance.EmpireFromCulture)
            {
                var cultureA = map.Cultures.GetValueOrDefault(((ITitle)a).GetDominantCulture(map).i);
                var cultureB = map.Cultures.GetValueOrDefault(((ITitle)b).GetDominantCulture(map).i);
                if (cultureA == null || cultureB == null) return false;
                return GetCultureAncestors(cultureA).Overlaps(GetCultureAncestors(cultureB));
            }
            else
            {
                var faithA = map.Faiths.GetValueOrDefault(((ITitle)a).GetDominantReligion(map).i);
                var faithB = map.Faiths.GetValueOrDefault(((ITitle)b).GetDominantReligion(map).i);
                if (faithA == null || faithB == null) return false;
                return GetFaithAncestors(faithA).Overlaps(GetFaithAncestors(faithB));
            }
        }

        private static HashSet<Culture> GetCultureAncestors(Culture c, HashSet<Culture>? visited = null)
        {
            visited ??= new HashSet<Culture>();
            if (!visited.Add(c)) return visited; // cycle guard
            foreach (var parent in c.Parents)
                GetCultureAncestors(parent, visited);
            return visited;
        }

        private static HashSet<Faith> GetFaithAncestors(Faith f)
        {
            var visited = new HashSet<Faith>();
            var current = f;
            while (current != null && visited.Add(current))
                current = current.Parent;
            return visited;
        }

        private static void MergeTinyEmpires(Map map)
        {
            //Much like with Kingdoms, empires can be to small to be considered an empire. In that case we join it with a neighbour.
            // - We could consider religion and or culture when merging empires, but for now we will just merge them based on shared borders


            Logger.Section("Merging tiny empires");
            Logger.Info($"Merging empires that are less than {Settings.Instance.MinimumKingdomsPerEmpire} kingdoms");
            var MergeTinyEmpires = map.Empires.Where(e => e.Kingdoms.Count < Settings.Instance.MinimumKingdomsPerEmpire).ToList();

            foreach (var empire in MergeTinyEmpires)
            {
                var adjacentEmpires = empire.GetNeighbours();

                if (!adjacentEmpires.Any())
                {
                    continue;
                }

                var sortedAdjacentEmpires = adjacentEmpires
                     .OrderByDescending(e => e.Value)
                     .Select(e => e.Key)
                     .ToList();

                if (!sortedAdjacentEmpires.Any())
                {
                    continue;
                }

                Empire mergeTarget = sortedAdjacentEmpires.First() as Empire;
                mergeTarget.Kingdoms.AddRange(empire.Kingdoms);
                foreach (var kingdom in empire.Kingdoms)
                {
                    kingdom.DeJureParent = mergeTarget;
                }
                // Remove the tiny empire from the map
                map.Empires.Remove(empire);

                Logger.Info($"Merged {empire.Name} into {mergeTarget.Name}");
            }

            Logger.Debug($"Empires after merging:");
            foreach (var empire in map.Empires)
            {
                Logger.Debug($" - {empire.Name} has {empire.Kingdoms.Count} kingdoms");
            }
        }

        struct BaronyNode(Node node, Barony barony)
        {
            public Node Node { get; } = node;
            public Barony Barony { get; } = barony;
        }

        private static string? GetRiversSkipReason(bool noRiversFlag)
        {
            if (noRiversFlag)
                return "the --no-rivers flag was provided.";
            if (!Settings.Instance.EnableRivers)
                return "EnableRivers is false in settings.json. Set it to true to enable river drawing.";
            if (string.IsNullOrEmpty(Settings.Instance.InputRiversGeojsonPath))
                return "no rivers GeoJSON path is configured. Provide --rivers-geojson <path> or set InputRiversGeojsonPath in settings.json.";
            if (!File.Exists(Settings.Instance.InputRiversGeojsonPath))
                return $"rivers GeoJSON file not found at '{Settings.Instance.InputRiversGeojsonPath}'. Check the path in settings or via --rivers-geojson.";
            return null;
        }

        /// <summary> Pure Debugging method to show the sea zones on the map </summary>
        private static async Task ShowSeaZones(Map map)
        {
            await ImageUtility.DrawSeaZonesImage(map);
        }

        /// <summary>
        /// Debugging method to show the baronies on the map
        /// </summary>
        private static async Task ShowBaronies(Map map)
        {
            await ImageUtility.DrawProvincesImage(map);
        }

        /// <summary>
        /// Debugging method to show the counties on the map
        /// </summary>
        /// <param name="map"></param>
        /// <returns></returns>
        private static async Task ShowCounties(Map map)
        {
            Dictionary<MagickColor, List<Cell>> countyCellsByColour = new();
            foreach (var county in map.Counties!)
            {
                countyCellsByColour.Add(county.GetColor(), county.GetAllCells());
            }
            await ImageUtility.DrawCellsWithColourImage(countyCellsByColour, map, "counties"); // Use default blue ocean
        }

        private static async Task ShowDuchies(Map map)
        {
            // Like with Counties, but for Duchies
            Dictionary<MagickColor, List<Cell>> duchyCellsByColour = new();
            foreach (var duchy in map.Duchies!)
            {
                duchyCellsByColour.Add(duchy.GetColor(), duchy.GetAllCells());
            }
            await ImageUtility.DrawCellsWithColourImage(duchyCellsByColour, map, "duchies"); // Use default blue ocean
        }
        private static async Task ShowKingdoms(Map map)
        {
            // Like for Duchies and Counties, but for Kingdoms
            Dictionary<MagickColor, List<Cell>> kingdomCellsByColour = new();
            foreach (var kingdom in map.Kingdoms!)
            {
                kingdomCellsByColour.Add(kingdom.GetColor(), kingdom.GetAllCells());
            }
            await ImageUtility.DrawCellsWithColourImage(kingdomCellsByColour, map, "kingdoms"); // Use default blue ocean
        }

        private static async Task ShowEmpires(Map map)
        {
            // Like for Duchies, Counties and Kingdoms, but for Empires
            // NOTE: Orphan kingdoms (no parent empire) will appear as wilderness at this level
            Dictionary<MagickColor, List<Cell>> empireCellsByColour = new();
            foreach (var empire in map.Empires!)
            {
                //Empires can form from dead culture / religion so we sanitize this a bit more
                var cells = empire.GetAllCells();
                if (!cells.Any()) continue;
                 empireCellsByColour.Add(empire.GetColor(), cells);
            }
            await ImageUtility.DrawCellsWithColourImage(empireCellsByColour, map, "empires"); // Use default blue ocean
        }


        private static void GenerateBaronyAdjacency(Map map)
        {
            Logger.Section("Generating barony adjacency graph from cell graph");
            using var _ = OperationTimer.Start("Generating barony adjacency");

            foreach (var barony in map.Baronies!)
            {
                //fist get all the cells that the cells in this barony are adjacent to
                var cells = barony.GetAllCells();
                var adjacentCells = cells.SelectMany(c => c.Neighbors).Distinct().Select(k => map.Cells![k]).ToList();
                // Remove any cell that is in this barony
                adjacentCells.RemoveAll(cells.Contains);

                // Now find all unique baronies that the adjacent cells are in
                var adjacentBaronies = adjacentCells.Select(c => c.Province as Barony).Where(b => b != null).Distinct().ToList();

                Logger.Verbose($"Barony {barony.Name} has {adjacentBaronies.Count} adjacent baronies");
                //Add the found baronies to this baronys list of adjacent baronies. Can be null if there are no adjacent baronies
                barony.Neighbors = adjacentBaronies!;
            }
            Logger.Info($"Built barony adjacency graph ({map.Baronies.Count} baronies)");
        }
    }
}