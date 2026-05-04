using Converter;
using Converter.Lemur.Deserialization;
using Converter.Lemur.Writers;
using ImageMagick;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace HeightmapLab;

static class Program
{
    static async Task<int> Main(string[] args)
    {
        // ── Parse CLI args ───────────────────────────────────────────────────
        string? jsonPath = null, geojsonPath = null, outputPath = null, terrainOut = null;
        string? comparePathA = null, comparePathB = null;
        string? cellsDumpPath = null;
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
        bool detailIntensity = false;

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
                case "--detail-intensity":    detailIntensity  = true; break;
                case "--cells":           cellsDumpPath = args[++i]; break;
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
        bool dataRequired   = !detailIntensity;
        bool outputRequired = !detailIntensity;
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
            RoughnessPower: roughnessPower);

        // ── Generate ─────────────────────────────────────────────────────────
        Console.WriteLine($"Generating heightmap (seed={seed}, strength={strength}, nodesPerCell={nodesPerCell}, samples={sampleCount}, roughnessNorm={roughnessNorm}).");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = HeightmapGenerator.Generate(cells, genParams);
        sw.Stop();
        Console.WriteLine($"Generated in {sw.Elapsed.TotalSeconds:F1}s");

        // ── Mesh visualisation (skipped when --coast-map is set; mesh is drawn there instead) ─
        if (mesh && !coastMap)
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
            const float wl = HeightmapGenerator.CK3WaterLevel;

            var steep = new float[w * h];
            for (int y = kd; y < h - kd; y++)
            for (int x = kd; x < w - kd; x++)
            {
                int idx = y * w + x;
                if (pixels[idx] < wl) continue;
                float gx = (pixels[idx + kd]     >= wl && pixels[idx - kd]     >= wl)
                    ? (hf[idx + kd]     - hf[idx - kd])     / (2f * kd) : 0f;
                float gy = (pixels[idx + kd * w] >= wl && pixels[idx - kd * w] >= wl)
                    ? (hf[idx + kd * w] - hf[idx - kd * w]) / (2f * kd) : 0f;
                steep[idx] = 1f - 1f / MathF.Sqrt(gx * gx + gy * gy + 1f);
            }

            var hist = new long[10000];
            int landN = 0;
            for (int idx = 0; idx < pixels.Length; idx++)
            {
                if (pixels[idx] < wl) continue;
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
                if (pixels[idx] < wl) continue;
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
                if (result.Core.Pixels[idx] < wl) continue;
                roughBytes[idx] = (byte)Math.Clamp((int)(roughF[idx] * 255f), 0, 255);
            }

            var rm = new MagickReadSettings { Width = w, Height = h, ColorSpace = ColorSpace.Gray, Format = MagickFormat.Gray };
            using var roughImg = new MagickImage(roughBytes, rm);
            roughImg.Depth = 8;
            await roughImg.WriteAsync(outputPath, MagickFormat.Png);
            Console.WriteLine($"Roughness map written to {outputPath} (Delaunay barycentric, terrain nodes only)");
            return 0;
        }

        // ── Coast map: blue tint below water level, greyscale above ─────────
        if (coastMap)
        {
            int w = genParams.Width, h = genParams.Height;
            const byte wl = HeightmapGenerator.CK3WaterLevel;

            var rgb = new byte[w * h * 3];
            for (int idx = 0; idx < result.Core.Pixels.Length; idx++)
            {
                byte px = result.Core.Pixels[idx];
                int o = idx * 3;
                if (px < wl)
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
                    if (nBadConn > 0 && nBadConn < 200)
                    {
                        var rings = new Drawables();
                        rings.FillColor(new MagickColor(0, 0, 0, 0))
                             .StrokeColor(MagickColors.Red).StrokeWidth(10);
                        for (int ci = 0; ci < result.Core.CoastNodes.Count; ci++)
                        {
                            if (result.Connectivity.Connections[ci] >= 2) continue;
                            if (result.Connectivity.IsHullNode[ci]) continue;
                            var cn = result.Core.CoastNodes[ci];
                            rings.Circle(cn.Px, cn.Py, cn.Px + 50, cn.Py);
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
            Usage: HeightmapLab --json <path> --geojson <path> --output <path.png>
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

                  HeightmapLab --compare <path-a> <path-b>
                                Pixel-by-pixel comparison of two grayscale PNGs. Exits 0 if identical.
            """);
    }
}
