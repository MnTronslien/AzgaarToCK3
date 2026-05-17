using Converter;
using Converter.Lemur.Deserialization;
using Converter.Lemur.Writers;
using ImageMagick;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace TerrainLab;

static class Program
{
    static async Task<int> Main(string[] args)
    {
        // ── Parse CLI args ───────────────────────────────────────────────────
        string? jsonPath = null, geojsonPath = null, outputPath = null, terrainOut = null;
        string? comparePathA = null, comparePathB = null;
        string? cellsDumpPath = null;
        string? riversGeojsonPath = null;
        float riverCpSpacing = 5f;
        int seed = 42;
        float strength = 0.25f, roughnessNorm = 1.0f;
        int nodesPerCell = 4;
        int sampleCount = 4;
        int relaxIters = 5;
        float terrainToPolySep = 0.25f;
        float relaxStep = 0.05f;
        int blurRadius = 3;
        float roughnessPower = 2.0f;
        bool debug = false;
        bool mesh = false;
        bool spawnLines = false;
        bool driftLines = false;
        bool steepnessMap = false;
        bool roughnessMap = false;
        bool coastMap = false;
        bool riverMap = false;
        bool detailIntensity = false;
        bool paintDetailIndex = false;
        bool paintDetailIntensity = false;
        bool noHeightmapForPaint = false;       // skip heightmap pre-pass → biome-only output
        int? alphaOverride = null;              // 0-255; null = leave painter default (255)
        bool checkNeighbors = false;
        bool neighborArrows = false;
        bool auditSharedEdges = false;
        string? dumpPair = null;
        bool vertexDebug = false;
        string? vertexRegion = null;
        string? vertexRiver = null;
        string? sampleTgaPath = null;
        int sampleTgaCount = 32;
        bool genMaterials = false;
        string? genMaterialsCk3Dir = null;
        string? genMaterialsOut = null;
        bool packHeightmap = false;
        bool packDebug = false;
        string? packOut = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json":           jsonPath      = args[++i]; break;
                case "--geojson":        geojsonPath   = args[++i]; break;
                case "--output":         outputPath    = args[++i]; break;
                case "--terrain-out":    terrainOut    = args[++i]; break;
                case "--seed":           seed          = int.Parse(args[++i]); break;
                case "--strength":       strength      = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--nodes":          nodesPerCell  = int.Parse(args[++i]); break;
                case "--sample-count":   sampleCount   = int.Parse(args[++i]); break;
                case "--roughness-norm": roughnessNorm = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--relax":               relaxIters       = int.Parse(args[++i]); break;
                case "--terrain-to-poly-sep": terrainToPolySep = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--relax-step":          relaxStep        = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--blur-radius":         blurRadius       = int.Parse(args[++i]); break;
                case "--roughness-power":     roughnessPower   = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--debug":               debug            = true; break;
                case "--mesh":                mesh             = true; break;
                case "--spawn-lines":         spawnLines       = true; break;
                case "--drift-lines":         driftLines       = true; break;
                case "--steepness-map":       steepnessMap     = true; break;
                case "--roughness-map":       roughnessMap     = true; break;
                case "--coast-map":           coastMap         = true; break;
                case "--river-map":           riverMap         = true; break;
                case "--check-neighbors":     checkNeighbors   = true; break;
                case "--neighbor-arrows":     neighborArrows   = true; break;
                case "--audit-shared-edges":  auditSharedEdges = true; break;
                case "--dump-pair":           dumpPair         = args[++i]; break;
                case "--vertex-debug":        vertexDebug      = true; break;
                case "--region":              vertexRegion     = args[++i]; break;
                case "--river-cell":          vertexRiver      = args[++i]; break;
                case "--detail-intensity":        detailIntensity      = true; break;
                case "--paint-detail-index":      paintDetailIndex     = true; break;
                case "--paint-detail-intensity":  paintDetailIntensity = true; break;
                case "--no-heightmap":            noHeightmapForPaint  = true; break;
                case "--alpha":                   alphaOverride        = int.Parse(args[++i]); break;
                case "--sample-tga":              sampleTgaPath        = args[++i]; break;
                case "--samples":                 sampleTgaCount       = int.Parse(args[++i]); break;
                case "--gen-materials":           genMaterials         = true; break;
                case "--ck3-dir":                 genMaterialsCk3Dir   = args[++i]; break;
                case "--gen-materials-out":       genMaterialsOut      = args[++i]; break;
                case "--pack-heightmap":          packHeightmap        = true; break;
                case "--pack-debug":              packDebug            = true; break;
                case "--pack-out":                packOut              = args[++i]; break;
                case "--cells":           cellsDumpPath     = args[++i]; break;
                case "--rivers-geojson":  riversGeojsonPath = args[++i]; break;
                case "--river-cp-spacing": riverCpSpacing  = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--compare":
                    comparePathA = args[++i];
                    comparePathB = args[++i];
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    PrintUsage();
                    return 1;
            }
        }

        // ── TGA pixel sampling mode — no Azgaar data needed ──────────────────
        if (sampleTgaPath != null)
        {
            return SampleTgaPixels(sampleTgaPath, sampleTgaCount);
        }

        // ── Generate Ck3MaterialBytes.cs from CK3's materials.settings ───────
        if (genMaterials)
        {
            return GenerateCk3MaterialBytes(genMaterialsCk3Dir, genMaterialsOut);
        }

        // ── Pixel comparison mode — no Azgaar data needed ────────────────────
        if (comparePathA != null)
        {
            if (comparePathB == null)
            {
                Console.Error.WriteLine("--compare requires two paths: --compare <path-a> <path-b>");
                return 1;
            }
            return CompareImages(comparePathA, comparePathB);
        }

        // --detail-intensity needs only --terrain-out (or --output for its dir); no Azgaar data needed
        // --check-neighbors needs cells but no output path
        bool dataRequired   = !detailIntensity;
        bool outputRequired = !detailIntensity && !checkNeighbors && !auditSharedEdges && dumpPair == null && !packHeightmap;
        bool dataProvided   = cellsDumpPath != null || (jsonPath != null && geojsonPath != null);
        if ((dataRequired && !dataProvided) || (outputRequired && outputPath == null && terrainOut == null))
        {
            Console.Error.WriteLine("Missing required arguments.");
            PrintUsage();
            return 1;
        }

        // ── detail-intensity: write diagnostic TGA, no generation needed ────
        if (detailIntensity)
        {
            var dir = terrainOut ?? Path.GetDirectoryName(outputPath!)!;
            Directory.CreateDirectory(dir);
            await WriteCheckerboardDetailIntensity(dir);
            Console.WriteLine($"detail_intensity.tga written to {dir}");
            return 0;
        }

        // ── Minimal init so Logger + OperationTimer work ─────────────────────
        SettingsManager.TryLoad();
        SettingsManager.Configure();

        // ── Load cells ───────────────────────────────────────────────────────
        IReadOnlyDictionary<int, Converter.Lemur.Entities.Cell> cells;
        float lonW, lonT, latS, latT;

        if (cellsDumpPath != null)
        {
            Console.WriteLine($"Loading cell dump: {cellsDumpPath}");
            var (dumpCells, coords) = CellDump.Read(cellsDumpPath);
            cells  = dumpCells;
            lonW = coords.LonW; lonT = coords.LonT;
            latS = coords.LatS; latT = coords.LatT;
            Console.WriteLine($"Loaded {cells.Count} cells from dump (including river cells).");
        }
        else
        {
            Console.WriteLine("Loading Azgaar data.");
            var geoMap  = await AzgaarLoader.LoadGeoJsonAsync(geojsonPath);
            var jsonMap = await AzgaarLoader.LoadJsonAsync(jsonPath);
            cells = AzgaarLoader.BuildCells(geoMap, jsonMap);
            var mc = jsonMap.mapCoordinates;
            lonW = mc.lonW; lonT = mc.lonT;
            latS = mc.latS; latT = mc.latT;
            Console.WriteLine($"Loaded {cells.Count} cells.");
        }

        // ── Paint detail TGAs (real cell-painted versions for hot-reload iteration) ──
        if (paintDetailIndex || paintDetailIntensity)
        {
            var dir = terrainOut ?? Path.GetDirectoryName(outputPath!)!;
            Directory.CreateDirectory(dir);

            // AzgaarMapCoordinates is a positional record (latT, latN, latS, lonT, lonW, lonE).
            // Cell dumps don't store latN/lonE so we synthesize them — painters only read latT/latS/lonT/lonW.
            var coords = new Converter.Lemur.Deserialization.AzgaarMapCoordinates(
                latT: latT, latN: latS + latT, latS: latS,
                lonT: lonT, lonW: lonW,         lonE: lonW + lonT);

            // ── Heightmap pre-pass (~30s) — required for steepness materials (hills/mountain).
            // Pass --no-heightmap to skip for fast biome-only iteration.
            byte[]? heightmapBytes = null;
            float[]? heightmapF = null;
            if (!noHeightmapForPaint)
            {
                Console.WriteLine("Heightmap pre-pass (required for hills/mountain materials)…");
                var hp = new Converter.Lemur.Writers.HeightmapAlgorithm.Params(
                    LonW: lonW, LonT: lonT, LatS: latS, LatT: latT,
                    Width: Converter.Lemur.Entities.Map.MapWidth,
                    Height: Converter.Lemur.Entities.Map.MapHeight);
                var hsw = System.Diagnostics.Stopwatch.StartNew();
                var hresult = Converter.Lemur.Writers.HeightmapAlgorithm.Generate(cells, hp);
                hsw.Stop();
                Console.WriteLine($"Heightmap pre-pass done in {hsw.Elapsed.TotalSeconds:F1}s.");
                heightmapBytes = hresult.Pixels;
                heightmapF = hresult.HeightmapF;
            }
            else
            {
                Console.WriteLine("--no-heightmap set — biome-only output (steepness materials disabled).");
            }

            if (paintDetailIndex)
            {
                using var indexImg = Converter.Lemur.Writers.TerrainMaskWriter.RenderDetailIndex(
                    cells, coords, heightmapF, heightmapBytes);
                if (alphaOverride.HasValue)
                {
                    indexImg.Evaluate(Channels.Alpha, EvaluateOperator.Set, new Percentage(alphaOverride.Value * 100.0 / 255.0));
                    Console.WriteLine($"  alpha override: {alphaOverride.Value}/255 ({alphaOverride.Value * 100.0 / 255.0:F1}%)");
                }
                await indexImg.WriteAsync(Path.Combine(dir, "detail_index.tga"), MagickFormat.Tga);
                Console.WriteLine($"detail_index.tga written to {dir}");
            }
            if (paintDetailIntensity)
            {
                using var intensityImg = Converter.Lemur.Writers.TerrainMaskWriter.RenderDetailIntensity(
                    cells, coords, heightmapF, heightmapBytes);
                if (alphaOverride.HasValue)
                {
                    intensityImg.Evaluate(Channels.Alpha, EvaluateOperator.Set, new Percentage(alphaOverride.Value * 100.0 / 255.0));
                    Console.WriteLine($"  alpha override: {alphaOverride.Value}/255 ({alphaOverride.Value * 100.0 / 255.0:F1}%)");
                }
                await intensityImg.WriteAsync(Path.Combine(dir, "detail_intensity.tga"), MagickFormat.Tga);
                Console.WriteLine($"detail_intensity.tga written to {dir}");
            }
            return 0;
        }

        // ── Neighbour-reciprocity audit (diagnostic, exits after) ───────────
        if (checkNeighbors)
        {
            int totalDirected = 0, oneWay = 0, missingTarget = 0;
            var sampleOneWay = new List<(int src, int dst, bool srcRiver, bool dstRiver)>();
            foreach (var (id, c) in cells)
            {
                if (c.Neighbors == null) continue;
                foreach (var nId in c.Neighbors)
                {
                    totalDirected++;
                    if (!cells.TryGetValue(nId, out var nbr))
                    {
                        missingTarget++;
                        continue;
                    }
                    if (nbr.Neighbors == null || !nbr.Neighbors.Contains(id))
                    {
                        oneWay++;
                        if (sampleOneWay.Count < 20)
                            sampleOneWay.Add((id, nId, c.IsRiverCell, nbr.IsRiverCell));
                    }
                }
            }

            int riverCells = cells.Values.Count(c => c.IsRiverCell);
            int landCells  = cells.Values.Count(c => Converter.Lemur.Entities.Cell.IsDryLand(c.Type));
            Console.WriteLine($"Cells: {cells.Count}  (land: {landCells}, river: {riverCells})");
            Console.WriteLine($"Directed neighbour edges: {totalDirected}");
            Console.WriteLine($"  Missing target cell: {missingTarget}");
            Console.WriteLine($"  One-way (A→B but not B→A): {oneWay}");

            // Break down one-way by category
            int landToLand = 0, landToRiver = 0, riverToLand = 0, riverToRiver = 0, other = 0;
            foreach (var (id, c) in cells)
            {
                if (c.Neighbors == null) continue;
                foreach (var nId in c.Neighbors)
                {
                    if (!cells.TryGetValue(nId, out var nbr)) continue;
                    if (nbr.Neighbors != null && nbr.Neighbors.Contains(id)) continue;
                    bool srcLand = Converter.Lemur.Entities.Cell.IsDryLand(c.Type);
                    bool dstLand = Converter.Lemur.Entities.Cell.IsDryLand(nbr.Type);
                    if (srcLand && dstLand)        landToLand++;
                    else if (srcLand && nbr.IsRiverCell)  landToRiver++;
                    else if (c.IsRiverCell && dstLand)    riverToLand++;
                    else if (c.IsRiverCell && nbr.IsRiverCell) riverToRiver++;
                    else other++;
                }
            }
            Console.WriteLine($"One-way breakdown:");
            Console.WriteLine($"  land  → land : {landToLand}");
            Console.WriteLine($"  land  → river: {landToRiver}");
            Console.WriteLine($"  river → land : {riverToLand}");
            Console.WriteLine($"  river → river: {riverToRiver}");
            Console.WriteLine($"  other        : {other}");

            if (sampleOneWay.Count > 0)
            {
                Console.WriteLine("Sample of one-way edges:");
                foreach (var s in sampleOneWay)
                    Console.WriteLine($"  cell {s.src}{(s.srcRiver ? " (river)" : "")} → {s.dst}{(s.dstRiver ? " (river)" : "")}");
            }
            return 0;
        }

        // ── Shared-edge audit: replicates the algorithm's match logic for every
        // (land, river) neighbour pair to count silent misses (H2 vertex coord drift).
        if (auditSharedEdges)
        {
            // We need pixel-space transforms; build genParams here so we can reuse.
            var auditParams = new HeightmapAlgorithm.Params(
                LonW: lonW, LonT: lonT, LatS: latS, LatT: latT,
                Width: Converter.Lemur.Entities.Map.MapWidth,
                Height: Converter.Lemur.Entities.Map.MapHeight);

            static (int, int) RK(double x, double y) => ((int)Math.Round(x), (int)Math.Round(y));
            float ToPxX(float lon) => (lon - auditParams.LonW) / auditParams.LonT * auditParams.Width;
            float ToPxY(float lat) => auditParams.Height - (lat - auditParams.LatS) / auditParams.LatT * auditParams.Height;

            int totalPairs = 0, pairsWithMatch = 0, pairsZeroMatch = 0;
            int sumExpectedEdges = 0, sumMatchedEdges = 0;
            var sampleMisses = new List<(int land, int river, int landVerts, int riverVerts)>();

            foreach (var (id, c) in cells)
            {
                if (!Converter.Lemur.Entities.Cell.IsDryLand(c.Type)) continue;
                if (c.Neighbors == null || c.GeoDataCoordinates == null) continue;
                int ln = c.GeoDataCoordinates.Length - 1;
                if (ln < 1) continue;

                foreach (var nId in c.Neighbors)
                {
                    if (!cells.TryGetValue(nId, out var nbr) || !nbr.IsRiverCell) continue;
                    if (nbr.GeoDataCoordinates == null) continue;
                    int sn = nbr.GeoDataCoordinates.Length - 1;
                    if (sn < 1) continue;

                    // Build river cell's rounded-pixel vertex set
                    var sKeys = new HashSet<(int, int)>(sn);
                    for (int i = 0; i < sn; i++)
                        sKeys.Add(RK(ToPxX(nbr.GeoDataCoordinates[i][0]), ToPxY(nbr.GeoDataCoordinates[i][1])));

                    // Scan land cell's edges; count how many have BOTH endpoints in sKeys
                    int matched = 0;
                    for (int vi = 0; vi < ln; vi++)
                    {
                        var kA = RK(ToPxX(c.GeoDataCoordinates[vi][0]),         ToPxY(c.GeoDataCoordinates[vi][1]));
                        var kB = RK(ToPxX(c.GeoDataCoordinates[(vi + 1) % ln][0]), ToPxY(c.GeoDataCoordinates[(vi + 1) % ln][1]));
                        if (kA == kB) continue;
                        if (sKeys.Contains(kA) && sKeys.Contains(kB)) matched++;
                    }

                    totalPairs++;
                    sumExpectedEdges += 1;   // each neighbour pair represents at least one shared boundary
                    sumMatchedEdges  += matched > 0 ? 1 : 0;
                    if (matched > 0) pairsWithMatch++;
                    else
                    {
                        pairsZeroMatch++;
                        if (sampleMisses.Count < 20)
                            sampleMisses.Add((id, nId, ln, sn));
                    }
                }
            }

            Console.WriteLine($"Shared-edge audit (land ↔ river neighbour pairs):");
            Console.WriteLine($"  Total (land, river) neighbour pairs: {totalPairs}");
            Console.WriteLine($"  Pairs that DO produce ≥1 shared edge: {pairsWithMatch}");
            Console.WriteLine($"  Pairs that produce ZERO shared edges (silent miss): {pairsZeroMatch}");
            if (totalPairs > 0)
                Console.WriteLine($"  Miss rate: {(double)pairsZeroMatch * 100 / totalPairs:F1}%");

            if (sampleMisses.Count > 0)
            {
                Console.WriteLine("Sample misses (land cell → river cell, vertex counts):");
                foreach (var s in sampleMisses)
                    Console.WriteLine($"  land {s.land} ({s.landVerts}v) → river {s.river} ({s.riverVerts}v)");
            }
            return 0;
        }

        // ── Dump full polygon coords for a specific cell pair (vertex drift inspection) ──
        if (dumpPair != null)
        {
            var parts = dumpPair.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int idA) || !int.TryParse(parts[1], out int idB))
            {
                Console.Error.WriteLine("Usage: --dump-pair <idA>,<idB>");
                return 1;
            }
            if (!cells.TryGetValue(idA, out var cA) || !cells.TryGetValue(idB, out var cB))
            {
                Console.Error.WriteLine($"Cell {idA} or {idB} not found.");
                return 1;
            }

            int W = Converter.Lemur.Entities.Map.MapWidth;
            int H = Converter.Lemur.Entities.Map.MapHeight;
            double ToPxX(double lon) => (lon - lonW) / lonT * W;
            double ToPxY(double lat) => H - (lat - latS) / latT * H;

            void Print(Converter.Lemur.Entities.Cell c, string label)
            {
                Console.WriteLine($"\n{label} (id {c.Id}, type {c.Type}, IsRiverCell {c.IsRiverCell}, {c.GeoDataCoordinates?.Length ?? 0} coords):");
                Console.WriteLine($"  Neighbors: [{string.Join(", ", c.Neighbors ?? Array.Empty<int>())}]");
                if (c.GeoDataCoordinates == null) return;
                Console.WriteLine($"  Vertices (geo lon,lat  →  pixel x,y  →  rounded ints):");
                for (int i = 0; i < c.GeoDataCoordinates.Length; i++)
                {
                    var v = c.GeoDataCoordinates[i];
                    if (v == null || v.Length < 2) continue;
                    double px = ToPxX(v[0]), py = ToPxY(v[1]);
                    Console.WriteLine($"    [{i,3}] geo ({v[0],10:F6}, {v[1],10:F6})  px ({px,10:F4}, {py,10:F4})  int ({(int)Math.Round(px),5}, {(int)Math.Round(py),5})");
                }
            }

            Print(cA, "Cell A");
            Print(cB, "Cell B");

            // Find closest-match between A vertices and B vertices (to highlight the drift magnitude)
            Console.WriteLine($"\nClosest-vertex-distance matrix (A → B, in pixels):");
            if (cA.GeoDataCoordinates != null && cB.GeoDataCoordinates != null)
            {
                var bPx = cB.GeoDataCoordinates
                    .Where(v => v != null && v.Length >= 2)
                    .Select(v => (px: ToPxX(v[0]), py: ToPxY(v[1])))
                    .ToArray();
                for (int i = 0; i < cA.GeoDataCoordinates.Length; i++)
                {
                    var v = cA.GeoDataCoordinates[i];
                    if (v == null || v.Length < 2) continue;
                    double ax = ToPxX(v[0]), ay = ToPxY(v[1]);
                    double bestDist = double.MaxValue; int bestJ = -1;
                    for (int j = 0; j < bPx.Length; j++)
                    {
                        double dx = ax - bPx[j].px, dy = ay - bPx[j].py;
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        if (d < bestDist) { bestDist = d; bestJ = j; }
                    }
                    Console.WriteLine($"  A[{i,3}] ({ax,10:F4}, {ay,10:F4})  →  nearest B[{bestJ,3}] ({bPx[bestJ].px,10:F4}, {bPx[bestJ].py,10:F4})  dist {bestDist:F4} px");
                }
            }
            return 0;
        }

        // ── Vertex debug map: highlights river-cell vertices + neighbour vertices ──
        if (vertexDebug)
        {
            // Parse region (CK3 pixel coords). Defaults to upper-left 2048×1500 crop.
            int rx1 = 0, ry1 = 0, rx2 = 2048, ry2 = 1500;
            if (vertexRegion != null)
            {
                var p = vertexRegion.Split(',');
                if (p.Length == 4
                    && int.TryParse(p[0], out rx1) && int.TryParse(p[1], out ry1)
                    && int.TryParse(p[2], out rx2) && int.TryParse(p[3], out ry2))
                { /* ok */ }
                else
                {
                    Console.Error.WriteLine("--region expects 'x1,y1,x2,y2' in CK3 pixel coords.");
                    return 1;
                }
            }

            int W = Converter.Lemur.Entities.Map.MapWidth;
            int H = Converter.Lemur.Entities.Map.MapHeight;
            float ToPxX(float lon) => (lon - lonW) / lonT * W;
            float ToPxY(float lat) => H - (lat - latS) / latT * H;

            // Build full-map image; pale background so dots/edges pop.
            var bg = new byte[W * H * 3];
            for (int i = 0; i < bg.Length; i += 3) { bg[i] = 245; bg[i + 1] = 245; bg[i + 2] = 245; }
            var vbgSettings = new MagickReadSettings { Width = W, Height = H, ColorSpace = ColorSpace.sRGB, Format = MagickFormat.Rgb };
            using var vimg = new MagickImage(bg, vbgSettings);
            vimg.Depth = 8;

            // Subset of river cells to highlight: optionally just one by id, otherwise all that touch the region.
            HashSet<int>? selected = null;
            if (vertexRiver != null && int.TryParse(vertexRiver, out int oneId))
                selected = new HashSet<int> { oneId };

            // For region filtering, compute each cell's pixel bbox once.
            bool InRegion(Converter.Lemur.Entities.Cell c)
            {
                if (c.GeoDataCoordinates == null) return false;
                foreach (var v in c.GeoDataCoordinates)
                {
                    if (v == null || v.Length < 2) continue;
                    float px = ToPxX(v[0]), py = ToPxY(v[1]);
                    if (px >= rx1 && px <= rx2 && py >= ry1 && py <= ry2) return true;
                }
                return false;
            }

            // 1) Collect the set of river cells to render (and from them, their neighbours).
            var riverIds = new HashSet<int>();
            foreach (var (id, c) in cells)
            {
                if (!c.IsRiverCell) continue;
                if (selected != null && !selected.Contains(id)) continue;
                if (selected == null && !InRegion(c)) continue;
                riverIds.Add(id);
            }
            var nbrIds = new HashSet<int>();
            foreach (var rid in riverIds)
            {
                if (!cells.TryGetValue(rid, out var rc) || rc.Neighbors == null) continue;
                foreach (var nId in rc.Neighbors)
                    if (nId != rid && cells.ContainsKey(nId) && !cells[nId].IsRiverCell)
                        nbrIds.Add(nId);
            }

            // 2) Draw neighbour polygons first (blue), so river polygons (red) draw on top.
            void DrawCellOutlineAndVerts(int cellId, MagickColor edgeCol, MagickColor vertCol, float vertRadius)
            {
                if (!cells.TryGetValue(cellId, out var c) || c.GeoDataCoordinates == null) return;
                int n = c.GeoDataCoordinates.Length;
                if (n < 2) return;

                var outline = new Drawables();
                outline.StrokeColor(edgeCol).StrokeWidth(2).FillColor(new MagickColor(0, 0, 0, 0));
                for (int i = 0; i < n - 1; i++)
                {
                    var a = c.GeoDataCoordinates[i];
                    var b = c.GeoDataCoordinates[i + 1];
                    if (a == null || b == null || a.Length < 2 || b.Length < 2) continue;
                    outline.Line(ToPxX(a[0]), ToPxY(a[1]), ToPxX(b[0]), ToPxY(b[1]));
                }
                vimg.Draw(outline);

                var verts = new Drawables();
                verts.FillColor(vertCol).StrokeColor(vertCol).StrokeWidth(1);
                for (int i = 0; i < n - 1; i++)  // skip the closing-ring duplicate
                {
                    var v = c.GeoDataCoordinates[i];
                    if (v == null || v.Length < 2) continue;
                    float px = ToPxX(v[0]), py = ToPxY(v[1]);
                    verts.Circle(px, py, px + vertRadius, py);
                }
                vimg.Draw(verts);
            }

            foreach (var nId in nbrIds)
                DrawCellOutlineAndVerts(nId, new MagickColor("#1f4d8a"), new MagickColor("#2a7fd8"), 0.8f);  // blue edge / lighter blue dots

            foreach (var rId in riverIds)
                DrawCellOutlineAndVerts(rId, new MagickColor("#a01818"), new MagickColor("#e02828"), 1.0f);  // dark red edge / bright red dots

            // 3) Crop to region and write. Upscale tiny regions for legibility (target ~1200 px wide).
            using var cropped = vimg.Clone();
            cropped.Crop(new MagickGeometry(rx1, ry1, rx2 - rx1, ry2 - ry1));
            cropped.RePage();
            int targetW = 1200;
            int cw = (int)cropped.Width;
            int ch = (int)cropped.Height;
            if (cw < targetW)
            {
                int scale = Math.Max(1, targetW / cw);
                cropped.Resize(cw * scale, ch * scale);
            }
            await cropped.WriteAsync(outputPath!, MagickFormat.Png);
            Console.WriteLine($"Vertex debug: {riverIds.Count} river cells (red), {nbrIds.Count} neighbour land cells (blue), region [{rx1},{ry1}]-[{rx2},{ry2}] → {outputPath}");
            return 0;
        }

        // ── Coordinate transform from map metadata ───────────────────────────
        var genParams = new HeightmapAlgorithm.Params(
            LonW: lonW, LonT: lonT,
            LatS: latS, LatT: latT,
            Width: Converter.Lemur.Entities.Map.MapWidth,
            Height: Converter.Lemur.Entities.Map.MapHeight,
            Seed: seed,
            DisplacementStrength: strength,
            NodesPerCell: nodesPerCell,
            RelaxIterations: relaxIters,
            TerrainToPolySep: terrainToPolySep,
            RelaxStep: relaxStep,
            PolyNodeSampleCount: sampleCount,
            RoughnessNorm: roughnessNorm,
            BlurRadius: blurRadius,
            RoughnessPower: roughnessPower,
            RiverControlPointSpacing: riverCpSpacing);

        // ── Load major rivers (control points) for centerline carving ───────
        List<HeightmapAlgorithm.RiverInput>? riverInputs = null;
        if (!string.IsNullOrWhiteSpace(riversGeojsonPath))
        {
            // Settings.Instance may be null in TerrainLab (no settings.json adjacent to the lab exe).
            float threshold = Settings.Instance?.MajorRiverThreshold ?? 300f;
            riverInputs = LoadRiverInputs(riversGeojsonPath, threshold);
            Console.WriteLine($"Loaded {riverInputs.Count} major rivers from {Path.GetFileName(riversGeojsonPath)} (threshold {threshold}).");
        }

        // ── Generate ─────────────────────────────────────────────────────────
        Console.WriteLine($"Generating heightmap (seed={seed}, strength={strength}, nodesPerCell={nodesPerCell}, samples={sampleCount}, roughnessNorm={roughnessNorm}).");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = HeightmapGenerator.Generate(cells, genParams, riverInputs);
        sw.Stop();
        Console.WriteLine($"Generated in {sw.Elapsed.TotalSeconds:F1}s");

        // ── Pack heightmap: run HeightmapWriter.PackAndWrite on freshly-generated pixels ─
        // Writes packed_heightmap.png, indirection_heightmap.png, heightmap.heightmap
        // (and heightmap.png + optional pack_detail_levels.png debug PNG) to the chosen dir.
        // Default dir: %LOCALAPPDATA%\AzgaarToCK3\debug\pack_<timestamp>\
        if (packHeightmap)
        {
            var packDir = packOut ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AzgaarToCK3", "debug",
                $"pack_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(packDir);

            // Save the source heightmap.png alongside the packed files for visual inspection.
            // PackAndWrite below doesn't write heightmap.png itself — only the three packed artifacts.
            var heightmapPath = Path.Combine(packDir, "heightmap.png");
            var hmSettings = new MagickReadSettings
            {
                Width      = genParams.Width,
                Height     = genParams.Height,
                ColorSpace = ColorSpace.Gray,
                Format     = MagickFormat.Gray,
            };
            using (var hmImg = new MagickImage(result.Core.Pixels, hmSettings))
            {
                hmImg.Depth = 8;
                await hmImg.WriteAsync(heightmapPath, MagickFormat.Png);
            }
            Console.WriteLine($"  heightmap.png written ({genParams.Width}×{genParams.Height})");

            string? debugPath = packDebug
                ? Path.Combine(packDir, "pack_detail_levels.png")
                : null;
            string? metricPath = packDebug
                ? Path.Combine(packDir, "pack_metric.png")
                : null;

            var packSw = System.Diagnostics.Stopwatch.StartNew();
            var stats = await HeightmapWriter.PackAndWrite(
                result.Core.Pixels, genParams.Width, genParams.Height,
                packDir, debugPath, metricPath);
            packSw.Stop();

            Console.WriteLine();
            Console.WriteLine($"Pack done in {packSw.Elapsed.TotalSeconds:F1}s. Output: {packDir}");
            Console.WriteLine($"  packed_heightmap.png:      {stats.PackedWidth}×{stats.PackedHeight}");
            Console.WriteLine($"  indirection_heightmap.png: {stats.IndirectionWidth}×{stats.IndirectionHeight}  (= {stats.IndirectionWidth * stats.IndirectionHeight} tiles total)");
            Console.WriteLine($"  Tiles per detail level (L0=highest, L4=lowest):");
            int sumTiles = 0;
            string[] avgSizeLabel = { "1×", "2×", "4×", "8×", "16×" };
            int[] tileSize = { 33, 17, 9, 5, 3 };
            for (int li = 0; li < stats.TilesPerDetailLevel.Count; li++)
            {
                int n = stats.TilesPerDetailLevel[li];
                sumTiles += n;
                double pct = stats.IndirectionWidth * stats.IndirectionHeight > 0
                    ? 100.0 * n / (stats.IndirectionWidth * stats.IndirectionHeight) : 0;
                Console.WriteLine($"    L{li}  tile {tileSize[li],2}×{tileSize[li],-2}  avg {avgSizeLabel[li],-3}  {n,6:N0} tiles  ({pct,5:F1}%)");
            }
            int unassigned = stats.IndirectionWidth * stats.IndirectionHeight - sumTiles;
            Console.WriteLine($"    Sum assigned: {sumTiles:N0}   Unassigned (empty_tile fallback): {unassigned:N0}");
            if (debugPath != null)
                Console.WriteLine($"  pack_detail_levels.png: {debugPath}");
            if (metricPath != null)
                Console.WriteLine($"  pack_metric.png:        {metricPath}");

            // Distribution of per-tile metric vs the four threshold cutoffs.
            // If most values pile up at one extreme, the metric is bimodal — the right
            // fix is to change the metric, not the thresholds.
            if (stats.PerTileMetrics.Count > 0)
            {
                var vals = stats.PerTileMetrics.Select(m => m.Value).OrderBy(v => v).ToArray();
                float P(double p)
                {
                    int idx = (int)Math.Clamp(Math.Round(p * (vals.Length - 1)), 0, vals.Length - 1);
                    return vals[idx];
                }
                Console.WriteLine($"  Per-tile metric distribution ({vals.Length:N0} tiles):");
                Console.WriteLine($"    min  = {vals[0]:E3}");
                Console.WriteLine($"    p10  = {P(0.10):E3}");
                Console.WriteLine($"    p25  = {P(0.25):E3}");
                Console.WriteLine($"    p50  = {P(0.50):E3}");
                Console.WriteLine($"    p75  = {P(0.75):E3}");
                Console.WriteLine($"    p90  = {P(0.90):E3}");
                Console.WriteLine($"    p95  = {P(0.95):E3}");
                Console.WriteLine($"    p99  = {P(0.99):E3}");
                Console.WriteLine($"    max  = {vals[^1]:E3}");
                Console.WriteLine($"  Current threshold cutoffs:  0.0001  0.0005  0.001  0.005");
                int Pct(float threshold) => vals.Count(v => v >= threshold);
                Console.WriteLine($"    tiles ≥ 0.0001 : {Pct(0.0001f),6:N0} ({100.0 * Pct(0.0001f) / vals.Length:F1}%)");
                Console.WriteLine($"    tiles ≥ 0.0005 : {Pct(0.0005f),6:N0} ({100.0 * Pct(0.0005f) / vals.Length:F1}%)");
                Console.WriteLine($"    tiles ≥ 0.001  : {Pct(0.001f),6:N0} ({100.0 * Pct(0.001f)  / vals.Length:F1}%)");
                Console.WriteLine($"    tiles ≥ 0.005  : {Pct(0.005f),6:N0} ({100.0 * Pct(0.005f)  / vals.Length:F1}%)");
            }
            return 0;
        }

        // ── Mesh visualisation (skipped when --coast-map / --river-map is set; mesh is drawn there instead) ─
        if (mesh && !coastMap && !riverMap)
        {
            using var meshImg = new MagickImage(MagickColors.White, genParams.Width, genParams.Height);

            var gf = new GeometryFactory();
            var builder = new DelaunayTriangulationBuilder();
            builder.SetSites(gf.CreateMultiPointFromCoords(
                result.Core.TerrainNodes.Select(t  => new Coordinate(t.Px,  t.Py))
                    .Concat(result.Core.PolyNodes.Select(pn => new Coordinate(pn.Px, pn.Py)))
                    .ToArray()));
            var triangles = builder.GetTriangles(gf);

            var d = new Drawables();
            d.StrokeColor(new MagickColor(180, 180, 180)).StrokeWidth(1).FillColor(MagickColors.None);

            var drawnEdges = new HashSet<long>();
            foreach (var geom in triangles.Geometries)
            {
                var ring = geom.Boundary.Coordinates;
                for (int e = 0; e < 3; e++)
                {
                    var a = ring[e]; var b = ring[e + 1];
                    long ak = (long)Math.Round(a.X) * 5000 + (long)Math.Round(a.Y);
                    long bk = (long)Math.Round(b.X) * 5000 + (long)Math.Round(b.Y);
                    long key = Math.Min(ak, bk) * 50_000_000L + Math.Max(ak, bk);
                    if (drawnEdges.Add(key))
                        d.Line(a.X, a.Y, b.X, b.Y);
                }
            }

            d.FillColor(MagickColors.Lime).StrokeColor(MagickColors.Lime).StrokeWidth(1);
            foreach (var t in result.Core.TerrainNodes)
                d.Circle(t.Px, t.Py, t.Px + 6, t.Py);

            d.FillColor(MagickColors.Red).StrokeColor(MagickColors.Red);
            foreach (var pn in result.Core.PolyNodes)
                d.Circle(pn.Px, pn.Py, pn.Px + 3, pn.Py);

            meshImg.Draw(d);
            await meshImg.WriteAsync(outputPath, MagickFormat.Png);
            Console.WriteLine($"Mesh written to {outputPath} ({result.Core.TerrainNodes.Count} terrain, {result.Core.PolyNodes.Count} poly-nodes, {triangles.NumGeometries} triangles)");
            return 0;
        }

        // ── Spawn-lines visualisation ─────────────────────────────────────────
        if (spawnLines)
        {
            using var slImg = new MagickImage(MagickColors.White, genParams.Width, genParams.Height);
            var d = new Drawables();

            d.StrokeColor(new MagickColor(180, 180, 180)).StrokeWidth(1).FillColor(MagickColors.None);
            foreach (var pn in result.Core.PolyNodes)
            {
                var parent = result.Core.TerrainNodes[pn.ParentId];
                d.Line(pn.Px, pn.Py, parent.Px, parent.Py);
            }

            d.FillColor(MagickColors.Lime).StrokeColor(MagickColors.Lime).StrokeWidth(1);
            foreach (var t in result.Core.TerrainNodes)
                d.Circle(t.Px, t.Py, t.Px + 5, t.Py);

            foreach (var pn in result.Core.PolyNodes)
            {
                float tv  = (pn.RawRand + 1f) / 2f;
                byte  r   = (byte)(255 * tv);
                byte  b   = (byte)(255 * (1f - tv));
                byte  alpha = (byte)(pn.IdwRoughness * 255f);
                var color = new MagickColor(r, 0, b, alpha);
                d.FillColor(color).StrokeColor(color).StrokeWidth(1);
                d.Circle(pn.Px, pn.Py, pn.Px + 3, pn.Py);
            }

            slImg.Draw(d);
            await slImg.WriteAsync(outputPath, MagickFormat.Png);
            Console.WriteLine($"Spawn-lines written to {outputPath} ({result.Core.TerrainNodes.Count} terrain, {result.Core.PolyNodes.Count} poly-nodes)");
            return 0;
        }

        // ── Drift-lines visualisation (spawn position → final position) ──────
        if (driftLines)
        {
            using var dlImg = new MagickImage(MagickColors.White, genParams.Width, genParams.Height);
            var d = new Drawables();

            d.StrokeColor(new MagickColor(180, 180, 180)).StrokeWidth(1).FillColor(MagickColors.None);
            foreach (var pn in result.Core.PolyNodes)
                d.Line(pn.SpawnPx, pn.SpawnPy, pn.Px, pn.Py);

            d.FillColor(new MagickColor(150, 150, 150)).StrokeColor(new MagickColor(150, 150, 150));
            foreach (var pn in result.Core.PolyNodes)
                d.Circle(pn.SpawnPx, pn.SpawnPy, pn.SpawnPx + 2, pn.SpawnPy);

            foreach (var pn in result.Core.PolyNodes)
            {
                float tv  = (pn.RawRand + 1f) / 2f;
                byte  r   = (byte)(255 * tv);
                byte  b   = (byte)(255 * (1f - tv));
                byte  alpha = (byte)(pn.IdwRoughness * 255f);
                var color = new MagickColor(r, 0, b, alpha);
                d.FillColor(color).StrokeColor(color).StrokeWidth(1);
                d.Circle(pn.Px, pn.Py, pn.Px + 3, pn.Py);
            }

            d.FillColor(MagickColors.Lime).StrokeColor(MagickColors.Lime).StrokeWidth(1);
            foreach (var t in result.Core.TerrainNodes)
                d.Circle(t.Px, t.Py, t.Px + 5, t.Py);

            dlImg.Draw(d);
            await dlImg.WriteAsync(outputPath, MagickFormat.Png);
            Console.WriteLine($"Drift-lines written to {outputPath} ({result.Core.PolyNodes.Count} poly-nodes)");
            return 0;
        }

        // ── Steepness map: black=flat, white=vertical ────────────────────────
        if (steepnessMap)
        {
            var hf = result.Core.HeightmapF;
            var pixels = result.Core.Pixels;
            int w = genParams.Width, h = genParams.Height;
            const int kd = 4;
            const float wl = HeightmapGenerator.MaxWaterByte;

            var steep = new float[w * h];
            for (int y = kd; y < h - kd; y++)
            for (int x = kd; x < w - kd; x++)
            {
                int idx = y * w + x;
                if (pixels[idx] <= wl) continue;
                float gx = (pixels[idx + kd]     > wl && pixels[idx - kd]     > wl)
                    ? (hf[idx + kd]     - hf[idx - kd])     / (2f * kd) : 0f;
                float gy = (pixels[idx + kd * w] > wl && pixels[idx - kd * w] > wl)
                    ? (hf[idx + kd * w] - hf[idx - kd * w]) / (2f * kd) : 0f;
                steep[idx] = 1f - 1f / MathF.Sqrt(gx * gx + gy * gy + 1f);
            }

            var hist = new long[10000];
            int landN = 0;
            for (int idx = 0; idx < pixels.Length; idx++)
            {
                if (pixels[idx] <= wl) continue;
                landN++;
                hist[Math.Clamp((int)(steep[idx] * 10000f), 0, 9999)]++;
            }
            float p95 = 0.0001f;
            if (landN > 0)
            {
                long target = (long)(landN * 0.95), cum = 0;
                for (int b = 0; b < hist.Length; b++)
                {
                    cum += hist[b];
                    if (cum >= target) { p95 = Math.Max(0.0001f, b / 10000f); break; }
                }
            }
            Console.WriteLine($"Steepness p95={p95:F5} (maps to white)");

            var steepBytes = new byte[w * h];
            for (int idx = 0; idx < pixels.Length; idx++)
            {
                if (pixels[idx] <= wl) continue;
                float s = Math.Clamp(steep[idx] / p95, 0f, 1f);
                steepBytes[idx] = (byte)(s * 255f);
            }

            var sm = new MagickReadSettings { Width = w, Height = h, ColorSpace = ColorSpace.Gray, Format = MagickFormat.Gray };
            using var steepImg = new MagickImage(steepBytes, sm);
            steepImg.Depth = 8;
            await steepImg.WriteAsync(outputPath, MagickFormat.Png);
            Console.WriteLine($"Steepness map written to {outputPath}");
            return 0;
        }

        // ── Roughness map: black=smooth, white=rough ─────────────────────────
        if (roughnessMap)
        {
            int w = genParams.Width, h = genParams.Height;
            const byte wl = 20;

            var roughF = HeightmapGenerator.RasterizeRoughness(result.Core.TerrainNodes, genParams);

            var roughBytes = new byte[w * h];
            for (int idx = 0; idx < roughF.Length; idx++)
            {
                if (result.Core.Pixels[idx] <= wl) continue;
                roughBytes[idx] = (byte)Math.Clamp((int)(roughF[idx] * 255f), 0, 255);
            }

            var rm = new MagickReadSettings { Width = w, Height = h, ColorSpace = ColorSpace.Gray, Format = MagickFormat.Gray };
            using var roughImg = new MagickImage(roughBytes, rm);
            roughImg.Depth = 8;
            await roughImg.WriteAsync(outputPath, MagickFormat.Png);
            Console.WriteLine($"Roughness map written to {outputPath} (Delaunay barycentric, terrain nodes only)");
            return 0;
        }

        // ── Neighbour arrows: heightmap + arrows for one-way neighbour edges ──
        if (neighborArrows)
        {
            int w = genParams.Width, h = genParams.Height;
            const byte wl = HeightmapGenerator.MaxWaterByte;

            // Greyscale + blue water background, same as --coast-map / --river-map
            var rgb = new byte[w * h * 3];
            for (int idx = 0; idx < result.Core.Pixels.Length; idx++)
            {
                byte px = result.Core.Pixels[idx];
                int o = idx * 3;
                if (px <= wl)
                {
                    rgb[o]     = 0;
                    rgb[o + 1] = (byte)(px * 3);
                    rgb[o + 2] = (byte)(120 + px * 6);
                }
                else
                {
                    rgb[o] = rgb[o + 1] = rgb[o + 2] = px;
                }
            }
            var nsettings = new MagickReadSettings { Width = w, Height = h, ColorSpace = ColorSpace.sRGB, Format = MagickFormat.Rgb };
            using var arrowsImg = new MagickImage(rgb, nsettings);
            arrowsImg.Depth = 8;

            // Cell centroid cache (pixel space) — computed lazily for cells we hit
            var centroidCache = new Dictionary<int, (float px, float py)>();
            (float, float) Centroid(Converter.Lemur.Entities.Cell c)
            {
                if (centroidCache.TryGetValue(c.Id, out var cached)) return cached;
                float sx = 0, sy = 0; int n = 0;
                if (c.GeoDataCoordinates != null)
                {
                    foreach (var v in c.GeoDataCoordinates)
                    {
                        if (v == null || v.Length < 2) continue;
                        sx += v[0]; sy += v[1]; n++;
                    }
                }
                float cgx = n > 0 ? sx / n : 0;
                float cgy = n > 0 ? sy / n : 0;
                float px = (cgx - genParams.LonW) / genParams.LonT * genParams.Width;
                float py = genParams.Height - (cgy - genParams.LatS) / genParams.LatT * genParams.Height;
                var tup = (px, py);
                centroidCache[c.Id] = tup;
                return tup;
            }

            void DrawArrow(Drawables d, float ax, float ay, float bx, float by, double headSize)
            {
                d.Line(ax, ay, bx, by);
                double dx = bx - ax, dy = by - ay;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1) return;
                double ux = dx / len, uy = dy / len;
                double pxn = -uy, pyn = ux;
                double backX = bx - ux * headSize, backY = by - uy * headSize;
                double lx = backX + pxn * (headSize / 2), ly = backY + pyn * (headSize / 2);
                double rx = backX - pxn * (headSize / 2), ry = backY - pyn * (headSize / 2);
                d.Polygon(new[]
                {
                    new PointD(bx, by),
                    new PointD(lx, ly),
                    new PointD(rx, ry),
                });
            }

            var arrows = new Drawables();
            arrows.StrokeWidth(2);
            int landToLand = 0, landToRiver = 0, riverToLand = 0, riverToRiver = 0, other = 0;
            foreach (var (id, c) in cells)
            {
                if (c.Neighbors == null) continue;
                foreach (var nId in c.Neighbors)
                {
                    if (!cells.TryGetValue(nId, out var nbr)) continue;
                    if (nbr.Neighbors != null && nbr.Neighbors.Contains(id)) continue;

                    bool srcLand = Converter.Lemur.Entities.Cell.IsDryLand(c.Type);
                    bool dstLand = Converter.Lemur.Entities.Cell.IsDryLand(nbr.Type);
                    MagickColor col;
                    if (srcLand && dstLand)             { col = MagickColors.Red;     landToLand++; }
                    else if (srcLand && nbr.IsRiverCell){ col = MagickColors.Orange;  landToRiver++; }
                    else if (c.IsRiverCell && dstLand)  { col = MagickColors.Cyan;    riverToLand++; }
                    else if (c.IsRiverCell && nbr.IsRiverCell){ col = MagickColors.Yellow; riverToRiver++; }
                    else                                  { col = MagickColors.White;   other++; }

                    var (ax, ay) = Centroid(c);
                    var (bx, by) = Centroid(nbr);
                    arrows.StrokeColor(col).FillColor(col);
                    DrawArrow(arrows, ax, ay, bx, by, 8.0);
                }
            }
            arrowsImg.Draw(arrows);
            await arrowsImg.WriteAsync(outputPath!, MagickFormat.Png);
            Console.WriteLine($"Neighbour arrows: red={landToLand} (land→land), orange={landToRiver}, cyan={riverToLand}, yellow={riverToRiver}, white={other}");
            Console.WriteLine($"Written to {outputPath}");
            return 0;
        }

        // ── River debug map: simplified coast view + cyan control points ────
        if (riverMap)
        {
            int w = genParams.Width, h = genParams.Height;
            const byte wl = HeightmapGenerator.MaxWaterByte;

            // Same blue-water + greyscale background as --coast-map
            var rgb = new byte[w * h * 3];
            for (int idx = 0; idx < result.Core.Pixels.Length; idx++)
            {
                byte px = result.Core.Pixels[idx];
                int o = idx * 3;
                if (px <= wl)
                {
                    rgb[o]     = 0;
                    rgb[o + 1] = (byte)(px * 3);
                    rgb[o + 2] = (byte)(120 + px * 6);
                }
                else
                {
                    rgb[o] = rgb[o + 1] = rgb[o + 2] = px;
                }
            }
            var rmSettings = new MagickReadSettings { Width = w, Height = h, ColorSpace = ColorSpace.sRGB, Format = MagickFormat.Rgb };
            using var riverImg = new MagickImage(rgb, rmSettings);
            riverImg.Depth = 8;

            // Optional Delaunay mesh overlay (white, thin)
            if (mesh)
            {
                var allCoords = result.Core.TerrainNodes.Select(t  => new Coordinate(t.Px,  t.Py))
                                    .Concat(result.Core.PolyNodes.Select(pn => new Coordinate(pn.Px, pn.Py)))
                                    .Concat(result.Core.CoastNodes.Select(cn => new Coordinate(cn.Px, cn.Py)))
                                    .ToArray();
                var gf = new GeometryFactory();
                var builder = new DelaunayTriangulationBuilder();
                builder.SetSites(gf.CreateMultiPointFromCoords(allCoords));
                var triangles = builder.GetTriangles(gf);

                var dm = new Drawables();
                dm.StrokeColor(MagickColors.White).StrokeWidth(1).FillColor(new MagickColor(0, 0, 0, 0));
                foreach (var geom in triangles.Geometries)
                {
                    var ring = geom.Boundary.Coordinates;
                    if (ring.Length < 3) continue;
                    dm.Line(ring[0].X, ring[0].Y, ring[1].X, ring[1].Y);
                    dm.Line(ring[1].X, ring[1].Y, ring[2].X, ring[2].Y);
                    dm.Line(ring[2].X, ring[2].Y, ring[0].X, ring[0].Y);
                }
                riverImg.Draw(dm);
            }

            // Constraint segments coloured by what the LAND cell was walking against:
            //   magenta = LAND cell's other-side neighbour is a river cell
            //   green   = LAND cell's other-side neighbour is a regular sea cell
            // Precompute the set of vertex pixel-keys that belong to river-cell
            // polygons; a segment whose BOTH endpoints land in that set is at a
            // land/river boundary. (ConstraintSegToCellId only stores the land
            // side, so checking it alone reports every seg as land — not useful.)
            {
                static (int, int) RK(double x, double y) => ((int)Math.Round(x), (int)Math.Round(y));

                // Pixel coords for cells are derived from GeoToPixel*; replicate the
                // transform inline so we can build the vertex set without touching
                // the algorithm's privates.
                var riverVertexKeys = new HashSet<(int, int)>();
                foreach (var c in cells.Values)
                {
                    if (!c.IsRiverCell || c.GeoDataCoordinates == null) continue;
                    foreach (var v in c.GeoDataCoordinates)
                    {
                        if (v == null || v.Length < 2) continue;
                        float px = (v[0] - genParams.LonW) / genParams.LonT * genParams.Width;
                        float py = genParams.Height - (v[1] - genParams.LatS) / genParams.LatT * genParams.Height;
                        riverVertexKeys.Add(RK(px, py));
                    }
                }

                var segs = new Drawables();
                int nRiverSeg = 0, nSeaSeg = 0;
                segs.StrokeWidth(3).FillColor(new MagickColor(0, 0, 0, 0));
                foreach (var seg in result.Core.ConstraintSegs)
                {
                    if (seg.Coordinates.Length < 2) continue;
                    var a = seg.Coordinates[0];
                    var b = seg.Coordinates[1];
                    bool aRiv = riverVertexKeys.Contains(RK(a.X, a.Y));
                    bool bRiv = riverVertexKeys.Contains(RK(b.X, b.Y));
                    bool atRiverBoundary = aRiv && bRiv;

                    MagickColor col;
                    if (atRiverBoundary) { col = MagickColors.Magenta;   nRiverSeg++; }
                    else                  { col = MagickColors.LimeGreen; nSeaSeg++; }
                    segs.StrokeColor(col);
                    segs.Line(a.X, a.Y, b.X, b.Y);
                }
                riverImg.Draw(segs);
                Console.WriteLine($"Constraint segs: {nRiverSeg} at river boundary (magenta), {nSeaSeg} elsewhere (green).  River-cell vertices indexed: {riverVertexKeys.Count}.");
            }

            var d = new Drawables();

            // All coast nodes — yellow, ~1 px diameter (radius 0.4 → near single pixel)
            d.FillColor(MagickColors.Yellow).StrokeColor(MagickColors.Yellow).StrokeWidth(1);
            foreach (var cn in result.Core.CoastNodes)
                d.Circle(cn.Px, cn.Py, cn.Px + 0.4, cn.Py);

            // River control points — cyan, ~2 px diameter (radius 0.8) for slight visibility edge
            int cpCount = 0;
            if (riverInputs != null)
            {
                d.FillColor(MagickColors.Cyan).StrokeColor(MagickColors.Cyan).StrokeWidth(1);
                foreach (var river in riverInputs)
                {
                    if (river.ControlPoints == null) continue;
                    foreach (var cp in river.ControlPoints)
                    {
                        if (cp == null || cp.Length < 2) continue;
                        float px = (cp[0] - genParams.LonW) / genParams.LonT * genParams.Width;
                        float py = genParams.Height - (cp[1] - genParams.LatS) / genParams.LatT * genParams.Height;
                        d.Circle(px, py, px + 0.8, py);
                        cpCount++;
                    }
                }
            }

            riverImg.Draw(d);
            await riverImg.WriteAsync(outputPath!, MagickFormat.Png);
            Console.WriteLine($"River debug map → {outputPath}  (coast nodes: {result.Core.CoastNodes.Count}, control points: {cpCount}{(mesh ? ", mesh: yes" : "")})");
            return 0;
        }

        // ── Coast map: blue tint below water level, greyscale above ─────────
        if (coastMap)
        {
            int w = genParams.Width, h = genParams.Height;
            const byte wl = HeightmapGenerator.MaxWaterByte;

            var rgb = new byte[w * h * 3];
            for (int idx = 0; idx < result.Core.Pixels.Length; idx++)
            {
                byte px = result.Core.Pixels[idx];
                int o = idx * 3;
                if (px <= wl)
                {
                    rgb[o]     = 0;
                    rgb[o + 1] = (byte)(px * 3);
                    rgb[o + 2] = (byte)(120 + px * 6);
                }
                else
                {
                    rgb[o] = rgb[o + 1] = rgb[o + 2] = px;
                }
            }

            var cm = new MagickReadSettings { Width = w, Height = h, ColorSpace = ColorSpace.sRGB, Format = MagickFormat.Rgb };
            using var coastImg = new MagickImage(rgb, cm);
            coastImg.Depth = 8;

            {
                var d = new Drawables();

                if (debug)
                {
                    d.FillColor(MagickColors.Lime).StrokeColor(MagickColors.Lime).StrokeWidth(1);
                    foreach (var t in result.Core.TerrainNodes)
                        d.Circle(t.Px, t.Py, t.Px + 6, t.Py);

                    foreach (var pn in result.Core.PolyNodes)
                    {
                        float tv = (pn.RawRand + 1f) / 2f;
                        var color = new MagickColor((byte)(255 * tv), 0, (byte)(255 * (1f - tv)), (byte)(pn.IdwRoughness * 255f));
                        d.FillColor(color).StrokeColor(color).StrokeWidth(1);
                        d.Circle(pn.Px, pn.Py, pn.Px + 3, pn.Py);
                    }
                }

                // Red rings: coast connectivity violations
                {
                    int nBadConn = result.Connectivity.Connections
                        .Select((c, i) => c < 2 && !result.Connectivity.IsHullNode[i]).Count(x => x);
                    if (nBadConn > 0)
                    {
                        // Scale ring size/width down when there are many failures so they don't cover the map.
                        int radius   = nBadConn > 500 ? 8 : nBadConn > 200 ? 20 : 50;
                        int strokeW  = nBadConn > 500 ? 2 : nBadConn > 200 ? 4  : 10;
                        var rings = new Drawables();
                        rings.FillColor(new MagickColor(0, 0, 0, 0))
                             .StrokeColor(MagickColors.Red).StrokeWidth(strokeW);
                        for (int ci = 0; ci < result.Core.CoastNodes.Count; ci++)
                        {
                            if (result.Connectivity.Connections[ci] >= 2) continue;
                            if (result.Connectivity.IsHullNode[ci]) continue;
                            var cn = result.Core.CoastNodes[ci];
                            rings.Circle(cn.Px, cn.Py, cn.Px + radius, cn.Py);
                        }
                        coastImg.Draw(rings);
                    }
                }

                // Orange rings: Steiner-Steiner cascade violations
                if (result.CascadingNodes.Count > 0 && result.CascadingNodes.Count < 200)
                {
                    var rings = new Drawables();
                    rings.FillColor(new MagickColor(0, 0, 0, 0))
                         .StrokeColor(new MagickColor("#FF8800")).StrokeWidth(10);
                    foreach (var cn in result.CascadingNodes)
                        rings.Circle(cn.Px, cn.Py, cn.Px + 50, cn.Py);
                    coastImg.Draw(rings);
                }

                // Coast nodes — four colours
                int origCount = result.Core.OriginalCoastNodeCount;
                for (int ci = 0; ci < result.Core.CoastNodes.Count; ci++)
                {
                    var cn   = result.Core.CoastNodes[ci];
                    int conn = result.Connectivity.Connections[ci];
                    bool hull = result.Connectivity.IsHullNode[ci];
                    bool steiner = ci >= origCount;

                    MagickColor color = steiner
                        ? MagickColors.Magenta
                        : conn >= 2 ? MagickColors.Yellow
                        : hull      ? new MagickColor("#FF8800")
                                    : MagickColors.Red;
                    d.FillColor(color).StrokeColor(color).StrokeWidth(1);
                    d.Circle(cn.Px, cn.Py, cn.Px + 2, cn.Py);
                }

                coastImg.Draw(d);
            }

            string nodesPath = mesh
                ? System.IO.Path.ChangeExtension(outputPath, null) + "-nodes.png"
                : outputPath;
            await coastImg.WriteAsync(nodesPath, MagickFormat.Png);

            int nOk      = result.Connectivity.Connections.Count(c => c >= 2);
            int nHull    = result.Connectivity.Connections.Select((c,i) => c < 2 &&  result.Connectivity.IsHullNode[i]).Count(x => x);
            int nBad     = result.Connectivity.Connections.Select((c,i) => c < 2 && !result.Connectivity.IsHullNode[i]).Count(x => x);
            int nSteiner = result.Core.CoastNodes.Count - result.Core.OriginalCoastNodeCount;
            Console.WriteLine($"Coast: {nOk} yellow (OK), {nBad} red (connectivity fail), {nHull} orange-hull, " +
                              $"{nSteiner} magenta (CDT Steiner), {result.CascadingNodes.Count} cascade fail");

            if (mesh)
            {
                var allCoords = result.Core.TerrainNodes.Select(t  => new Coordinate(t.Px,  t.Py))
                                    .Concat(result.Core.PolyNodes.Select(pn => new Coordinate(pn.Px, pn.Py)))
                                    .Concat(result.Core.CoastNodes.Select(cn => new Coordinate(cn.Px, cn.Py)))
                                    .ToArray();
                var gf = new GeometryFactory();
                var builder = new DelaunayTriangulationBuilder();
                builder.SetSites(gf.CreateMultiPointFromCoords(allCoords));
                var triangles = builder.GetTriangles(gf);

                var dm = new Drawables();
                dm.StrokeColor(MagickColors.White).StrokeWidth(1).FillColor(new MagickColor(0, 0, 0, 0));
                foreach (var geom in triangles.Geometries)
                {
                    var ring = geom.Boundary.Coordinates;
                    if (ring.Length < 3) continue;
                    dm.Line(ring[0].X, ring[0].Y, ring[1].X, ring[1].Y);
                    dm.Line(ring[1].X, ring[1].Y, ring[2].X, ring[2].Y);
                    dm.Line(ring[2].X, ring[2].Y, ring[0].X, ring[0].Y);
                }
                coastImg.Draw(dm);
                await coastImg.WriteAsync(outputPath, MagickFormat.Png);
                Console.WriteLine($"Mesh: {triangles.NumGeometries} triangles → {outputPath}");
            }

            Console.WriteLine($"Nodes image → {nodesPath}");
            return 0;
        }

        // ── Write heightmap PNG ───────────────────────────────────────────────
        var readSettings = new MagickReadSettings
        {
            Width = genParams.Width,
            Height = genParams.Height,
            ColorSpace = ColorSpace.Gray,
            Format = MagickFormat.Gray,
        };
        using var img = new MagickImage(result.Core.Pixels, readSettings);
        img.Depth = 8;

        if (debug)
        {
            img.ColorSpace = ColorSpace.sRGB;
            var d = new Drawables();

            d.FillColor(MagickColors.Lime).StrokeColor(MagickColors.Lime).StrokeWidth(1);
            foreach (var t in result.Core.TerrainNodes)
                d.Circle(t.Px, t.Py, t.Px + 6, t.Py);

            foreach (var pn in result.Core.PolyNodes)
            {
                float tv  = (pn.RawRand + 1f) / 2f;
                byte  r   = (byte)(255 * tv);
                byte  b   = (byte)(255 * (1f - tv));
                byte  alpha = (byte)(pn.IdwRoughness * 255f);
                var color = new MagickColor(r, 0, b, alpha);
                d.FillColor(color).StrokeColor(color).StrokeWidth(1);
                d.Circle(pn.Px, pn.Py, pn.Px + 3, pn.Py);
            }

            img.Draw(d);
            Console.WriteLine($"Debug overlay: {result.Core.TerrainNodes.Count} terrain (green), {result.Core.PolyNodes.Count} poly-nodes (blue=negative, red=positive, alpha=idwRoughness)");
        }

        await img.WriteAsync(outputPath!, MagickFormat.Png);
        Console.WriteLine($"Written to {outputPath}");

        if (terrainOut != null)
        {
            var masksDir = Path.Combine(terrainOut, "masks");
            Directory.CreateDirectory(masksDir);
            await Converter.Lemur.Writers.HeightmapMasks.Write(
                result.Core.HeightmapF, result.Core.Pixels,
                genParams.Width, genParams.Height,
                masksDir);
            Console.WriteLine($"Hills/mountains/snow masks written to {masksDir}");
        }

        return 0;
    }

    // ── Pixel comparison ─────────────────────────────────────────────────────
    // Parse rivers.geojson directly (no JSON map / no AzgaarLoader dependency) and
    // return major rivers as RiverInput records ready for HeightmapAlgorithm.
    static List<HeightmapAlgorithm.RiverInput> LoadRiverInputs(string riversGeojsonPath, float majorThreshold)
    {
        var json = File.ReadAllText(riversGeojsonPath);
        var rgj  = System.Text.Json.JsonSerializer.Deserialize<Converter.Lemur.Deserialization.RiverGeoJson>(
            json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (rgj == null || rgj.features == null)
            throw new InvalidDataException($"Failed to parse rivers GeoJSON: {riversGeojsonPath}");

        var result = new List<HeightmapAlgorithm.RiverInput>(rgj.features.Length);
        foreach (var f in rgj.features)
        {
            if (f.geometry == null || f.geometry.coordinates == null || f.geometry.coordinates.Length < 2) continue;
            if (f.properties == null) continue;
            if (f.properties.discharge < majorThreshold) continue;
            result.Add(new HeightmapAlgorithm.RiverInput(
                Id:            f.properties.id,
                Width:         f.properties.widthFactor,
                SourceWidth:   f.properties.sourceWidth,
                ControlPoints: f.geometry.coordinates));
        }
        return result;
    }

    // Random-pixel sampler for any RGBA image. Useful for inspecting how vanilla CK3 detail TGAs
    // encode information across the four channels — answers questions like "is the G channel
    // constant 255 everywhere, or does it carry data?"
    // Parses CK3 materials.settings and writes Converter/Lemur/Splats/Ck3MaterialBytes.cs.
    // Manual run, intentional — we want to review the diff when CK3 patches.
    static int GenerateCk3MaterialBytes(string? ck3Dir, string? outPath)
    {
        ck3Dir ??= @"C:\Program Files (x86)\Steam\steamapps\common\Crusader Kings III";
        var materialsPath = Path.Combine(ck3Dir, "game", "gfx", "map", "terrain", "materials.settings");
        if (!File.Exists(materialsPath))
        {
            Console.Error.WriteLine($"materials.settings not found: {materialsPath}");
            Console.Error.WriteLine("Pass --ck3-dir <path> if CK3 lives elsewhere.");
            return 1;
        }

        // Default output: walk up from the running TerrainLab binary to the repo root, then to
        // Converter/Lemur/Splats/Ck3MaterialBytes.cs. The binary lives at
        // <repo>/TerrainLab/bin/Debug/net8.0/TerrainLab.exe, so up four levels gets us to the repo.
        outPath ??= Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Converter", "Lemur", "Splats", "Ck3MaterialBytes.cs"));

        Console.WriteLine($"Parsing: {materialsPath}");
        var parsed = Converter.Lemur.Splats.MaterialsParser.Parse(materialsPath);
        Console.WriteLine($"Active entries: {parsed.Materials.Count}");
        Console.WriteLine($"Source mtime:   {parsed.SourceMtimeUtc:O}");
        Console.WriteLine($"Source sha256:  {parsed.SourceSha256}");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// AUTO-GENERATED from CK3 materials.settings — DO NOT EDIT BY HAND.");
        sb.AppendLine($"// Source:  {parsed.SourcePath}");
        sb.AppendLine($"// Mtime:   {parsed.SourceMtimeUtc:O}");
        sb.AppendLine($"// Sha256:  {parsed.SourceSha256}");
        sb.AppendLine($"// Entries: {parsed.Materials.Count}");
        sb.AppendLine("// Regenerate: ./TerrainLab --gen-materials");
        sb.AppendLine();
        sb.AppendLine("namespace Converter.Lemur.Splats;");
        sb.AppendLine();
        sb.AppendLine("public static class Ck3MaterialBytes");
        sb.AppendLine("{");
        sb.AppendLine("    public const string SourceMtimeUtc = \"" + parsed.SourceMtimeUtc.ToString("O") + "\";");
        sb.AppendLine("    public const string SourceSha256   = \"" + parsed.SourceSha256 + "\";");
        sb.AppendLine();
        sb.AppendLine("    public static readonly IReadOnlyDictionary<string, byte> ByName = new Dictionary<string, byte>");
        sb.AppendLine("    {");
        foreach (var (idx, name) in parsed.Materials)
            sb.AppendLine($"        [\"{name}\"] = {idx},");
        sb.AppendLine("    };");
        sb.AppendLine("}");

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"Wrote: {outPath}");
        return 0;
    }

    static int SampleTgaPixels(string path, int count)
    {
        using var img = new MagickImage(path);
        Console.WriteLine($"File: {path}");
        Console.WriteLine($"Dimensions: {img.Width}×{img.Height}, ColorSpace={img.ColorSpace}, ChannelCount={img.ChannelCount}");
        Console.WriteLine($"HasAlpha: {img.HasAlpha}");

        var pixels = img.GetPixelsUnsafe();
        var bpp = (int)img.ChannelCount;
        var rng = new Random(42);  // deterministic for reproducibility

        // Channel histograms — useful for spotting constants vs variation.
        var rHist = new long[256]; var gHist = new long[256];
        var bHist = new long[256]; var aHist = new long[256];

        // Sample N random pixels for detailed inspection.
        Console.WriteLine($"\n--- {count} random pixel samples ---");
        Console.WriteLine("    x       y     R    G    B    A");
        // Magick.NET-Q8 stores each channel as a byte already (Quantum = byte), so no bit-shift.
        for (int i = 0; i < count; i++)
        {
            int x = rng.Next((int)img.Width);
            int y = rng.Next((int)img.Height);
            var px = pixels.GetPixel(x, y).ToArray()!;
            byte r = px.Length > 0 ? px[0] : (byte)0;
            byte g = px.Length > 1 ? px[1] : (byte)0;
            byte b = px.Length > 2 ? px[2] : (byte)0;
            byte a = px.Length > 3 ? px[3] : (byte)255;
            Console.WriteLine($"{x,7} {y,7}   {r,3}  {g,3}  {b,3}  {a,3}");
        }

        // Full-image histograms — scan every pixel, useful for "is this channel constant?"
        Console.WriteLine("\n--- Full-image channel histograms (top 5 values per channel) ---");
        int w = (int)img.Width, h = (int)img.Height;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var px = pixels.GetPixel(x, y).ToArray()!;
                if (px.Length > 0) rHist[px[0]]++;
                if (px.Length > 1) gHist[px[1]]++;
                if (px.Length > 2) bHist[px[2]]++;
                if (px.Length > 3) aHist[px[3]]++;
            }
        }
        PrintTop("R", rHist, w * h);
        PrintTop("G", gHist, w * h);
        PrintTop("B", bHist, w * h);
        PrintTop("A", aHist, w * h);
        return 0;
    }

    static void PrintTop(string channel, long[] hist, long total)
    {
        var topN = hist.Select((c, v) => (Value: v, Count: c))
            .Where(t => t.Count > 0)
            .OrderByDescending(t => t.Count).Take(5).ToList();
        int distinct = hist.Count(c => c > 0);
        Console.WriteLine($"  {channel}: {distinct} distinct values");
        foreach (var (v, c) in topN)
            Console.WriteLine($"     {v,3}: {c,12:N0} ({100.0 * c / total:F2}%)");
    }

    static int CompareImages(string pathA, string pathB)
    {
        using var imgA = new MagickImage(pathA);
        using var imgB = new MagickImage(pathB);
        imgA.Grayscale();
        imgB.Grayscale();

        if (imgA.Width != imgB.Width || imgA.Height != imgB.Height)
        {
            Console.Error.WriteLine($"Size mismatch: {pathA} is {imgA.Width}×{imgA.Height}, {pathB} is {imgB.Width}×{imgB.Height}");
            return 1;
        }

        var pxA = imgA.GetPixels().ToByteArray("R")!;
        var pxB = imgB.GetPixels().ToByteArray("R")!;

        int diffCount = 0, maxDiff = 0;
        for (int i = 0; i < pxA.Length; i++)
        {
            int d = Math.Abs(pxA[i] - pxB[i]);
            if (d > 0) { diffCount++; maxDiff = Math.Max(maxDiff, d); }
        }

        Console.WriteLine($"Differing pixels: {diffCount} / {pxA.Length}  MaxDelta: {maxDiff}");
        if (diffCount == 0)
            Console.WriteLine("PASS — pixel-identical");
        else
            Console.WriteLine("FAIL — drift detected");
        return diffCount == 0 ? 0 : 1;
    }

    // ── Checkerboard detail intensity ─────────────────────────────────────────
    static async Task WriteCheckerboardDetailIntensity(string terrainDir)
    {
        const int tileSize = 128;
        int w = Converter.Lemur.Entities.Map.MapWidth;
        int h = Converter.Lemur.Entities.Map.MapHeight;
        var pixels = new byte[w * h * 3];

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int rowCh = (y / tileSize) % 3;
            int colCh = (x / tileSize) % 3;
            int o = (y * w + x) * 3;
            pixels[o]     = (rowCh == 0 || colCh == 0) ? (byte)255 : (byte)0;
            pixels[o + 1] = (rowCh == 1 || colCh == 1) ? (byte)255 : (byte)0;
            pixels[o + 2] = (rowCh == 2 || colCh == 2) ? (byte)255 : (byte)0;
        }

        var settings = new MagickReadSettings { Width = w, Height = h, ColorSpace = ColorSpace.sRGB, Format = MagickFormat.Rgb };
        using var img = new MagickImage(pixels, settings);
        img.Alpha(AlphaOption.Off);
        await img.WriteAsync(Path.Combine(terrainDir, "detail_intensity.tga"), MagickFormat.Tga);
    }

    static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: TerrainLab --json <path> --geojson <path> --output <path.png>
                  OR             --cells <dump.json>           --output <path.png>
                                [--seed N]            default: 42
                                [--strength F]        displacement strength, default: 0.25
                                [--nodes N]           poly-nodes per land cell, default: 4
                                [--sample-count N]    IDW nearest nodes, default: 4
                                [--roughness-norm F]  normalisation factor, default: 1.0
                                [--relax N]                 repulsion relaxation iterations, default: 5
                                [--terrain-to-poly-sep F]  min distance from terrain centroid, default: 0.25
                                [--blur-radius N]           Gaussian blur radius in pixels, default: 3
                                [--debug]             overlay green dots (centroids) + red dots (poly-nodes)

                  --cells <dump.json>   Load cell dump produced by ConsoleUI --dump-cells instead of raw Azgaar files.
                                        Includes major river cell modifications. Replaces --json + --geojson.
                  --rivers-geojson <path>
                                        Major-river control points used to seed centerline TerrainNodes at
                                        MaxWaterByte - Params.RiverCenterlineDepth. Filtered by MajorRiverThreshold
                                        from settings.json. Compatible with both --cells and --json/--geojson modes.
                  --river-cp-spacing F  Densify control points to ≤ F pixels apart along each river polyline.
                                        Default: 5. Lower = denser spine, more CDT cost, fewer rasterization gaps.

                  --paint-detail-index       Render the real cell-painted detail_index.tga and exit.
                                             Writes to --terrain-out (or dirname of --output). Accepts both
                                             --json/--geojson (raw Azgaar cells) and --cells (post-pipeline cells).
                  --paint-detail-intensity   Render the real cell-painted detail_intensity.tga and exit.
                                             Same requirements as --paint-detail-index.
                  --alpha N                  Override the alpha channel to value N (0-255) on every pixel.
                                             Diagnostic — used with --paint-detail-* to test how CK3 interprets alpha.
                  --detail-intensity         (diagnostic) Write the row×column RGB checkerboard, no Azgaar data needed.

                  --pack-heightmap           Generate heightmap, run the packing algorithm (CreatePackedHeightmap +
                                             WritePackedHeightmap), write packed_heightmap.png + indirection_heightmap.png +
                                             heightmap.heightmap + source heightmap.png to --pack-out. Prints per-detail-level
                                             tile-count stats afterwards.
                                             Default --pack-out: %LOCALAPPDATA%/AzgaarToCK3/debug/pack_<timestamp>/
                                             For CK3 hot-reload, pass --pack-out '<mod>/map_data'.
                  --pack-out <dir>           Override the output directory for --pack-heightmap.
                  --pack-debug               Also writes pack_detail_levels.png — an indirection-grid-sized PNG with each
                                             pixel coloured by the detail level the packer chose for that tile
                                             (red=L0 highest, blue=L4 lowest). Killer diagnostic for spotting where
                                             the packer over- or under-allocates detail.

                  TerrainLab --compare <path-a> <path-b>
                                Pixel-by-pixel comparison of two grayscale PNGs. Exits 0 if identical.
            """);
    }
}
