using Converter;
using Converter.Lemur.Deserialization;
using ImageMagick;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace HeightmapLab;

static class Program
{
    static async Task<int> Main(string[] args)
    {
        // ── Parse CLI args ───────────────────────────────────────────────────
        string? jsonPath = null, geojsonPath = null, outputPath = null;
        int seed = 42;
        float strength = 0.25f, roughnessNorm = 1.0f;
        int nodesPerCell = 4;
        int sampleCount = 4;
        int relaxIters = 5;
        float terrainToPolySep = 0.25f;
        float relaxStep = 0.05f;
        int blurRadius = 3;
        float roughnessPower = 2.0f;
        bool baseOnly = false;
        bool debug = false;
        bool mesh = false;
        bool spawnLines = false;
        bool driftLines = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json":           jsonPath      = args[++i]; break;
                case "--geojson":        geojsonPath   = args[++i]; break;
                case "--output":         outputPath    = args[++i]; break;
                case "--seed":           seed          = int.Parse(args[++i]); break;
                case "--strength":       strength      = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--nodes":          nodesPerCell  = int.Parse(args[++i]); break;
                case "--sample-count":   sampleCount   = int.Parse(args[++i]); break;
                case "--roughness-norm": roughnessNorm = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--relax":                relaxIters       = int.Parse(args[++i]); break;
                case "--terrain-to-poly-sep": terrainToPolySep = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--relax-step":          relaxStep        = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--blur-radius":          blurRadius       = int.Parse(args[++i]); break;
                case "--roughness-power":      roughnessPower   = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--base-only":           baseOnly         = true; break;
                case "--debug":          debug         = true; break;
                case "--mesh":           mesh          = true; break;
                case "--spawn-lines":    spawnLines    = true; break;
                case "--drift-lines":    driftLines    = true; break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    PrintUsage();
                    return 1;
            }
        }

        if (jsonPath == null || geojsonPath == null || outputPath == null)
        {
            Console.Error.WriteLine("Missing required arguments.");
            PrintUsage();
            return 1;
        }

        // ── Minimal init so Logger + OperationTimer work ─────────────────────
        SettingsManager.TryLoad();
        SettingsManager.Configure();

        // ── Load cells ───────────────────────────────────────────────────────
        Console.WriteLine("Loading Azgaar data…");
        var geoMap  = await AzgaarLoader.LoadGeoJsonAsync(geojsonPath);
        var jsonMap = await AzgaarLoader.LoadJsonAsync(jsonPath);
        var cells   = AzgaarLoader.BuildCells(geoMap, jsonMap);
        Console.WriteLine($"Loaded {cells.Count} cells.");

        // ── Coordinate transform from map metadata ───────────────────────────
        var mc = jsonMap.mapCoordinates;
        var genParams = new HeightmapGenerator.Params(
            LonW: mc.lonW, LonT: mc.lonT,
            LatS: mc.latS, LatT: mc.latT,
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
            BaseOnly: baseOnly);

        // ── Generate ─────────────────────────────────────────────────────────
        Console.WriteLine($"Generating heightmap (seed={seed}, strength={strength}, nodesPerCell={nodesPerCell}, samples={sampleCount}, roughnessNorm={roughnessNorm})…");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = HeightmapGenerator.Generate(cells, genParams);
        sw.Stop();
        Console.WriteLine($"Generated in {sw.Elapsed.TotalSeconds:F1}s");

        // ── Mesh visualisation ────────────────────────────────────────────────
        if (mesh)
        {
            using var meshImg = new MagickImage(MagickColors.White, genParams.Width, genParams.Height);

            // Delaunay triangulation of the combined point set
            var gf = new GeometryFactory();
            var builder = new DelaunayTriangulationBuilder();
            builder.SetSites(gf.CreateMultiPointFromCoords(
                result.TerrainNodes.Select(t  => new Coordinate(t.Px,  t.Py))
                    .Concat(result.PolyNodes.Select(pn => new Coordinate(pn.Px, pn.Py)))
                    .ToArray()));
            var triangles = builder.GetTriangles(gf);

            var d = new Drawables();
            d.StrokeColor(new MagickColor(180, 180, 180)).StrokeWidth(1).FillColor(MagickColors.None);

            // Draw each unique triangle edge once
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

            // Terrain nodes — green, radius 6
            d.FillColor(MagickColors.Lime).StrokeColor(MagickColors.Lime).StrokeWidth(1);
            foreach (var t in result.TerrainNodes)
                d.Circle(t.Px, t.Py, t.Px + 6, t.Py);

            // Poly-nodes — red, radius 3
            d.FillColor(MagickColors.Red).StrokeColor(MagickColors.Red);
            foreach (var pn in result.PolyNodes)
                d.Circle(pn.Px, pn.Py, pn.Px + 3, pn.Py);

            meshImg.Draw(d);
            await meshImg.WriteAsync(outputPath, MagickFormat.Png);
            Console.WriteLine($"Mesh written to {outputPath} ({result.TerrainNodes.Count} terrain, {result.PolyNodes.Count} poly-nodes, {triangles.NumGeometries} triangles)");
            return 0;
        }

        // ── Spawn-lines visualisation ─────────────────────────────────────────
        if (spawnLines)
        {
            using var slImg = new MagickImage(MagickColors.White, genParams.Width, genParams.Height);
            var d = new Drawables();

            // Lines: poly-node current position → parent terrain node
            d.StrokeColor(new MagickColor(180, 180, 180)).StrokeWidth(1).FillColor(MagickColors.None);
            foreach (var pn in result.PolyNodes)
            {
                var parent = result.TerrainNodes[pn.ParentId];
                d.Line(pn.Px, pn.Py, parent.Px, parent.Py);
            }

            // Terrain nodes — green, radius 5
            d.FillColor(MagickColors.Lime).StrokeColor(MagickColors.Lime).StrokeWidth(1);
            foreach (var t in result.TerrainNodes)
                d.Circle(t.Px, t.Py, t.Px + 5, t.Py);

            // Poly-nodes — same blue/red/alpha scheme as debug
            foreach (var pn in result.PolyNodes)
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
            Console.WriteLine($"Spawn-lines written to {outputPath} ({result.TerrainNodes.Count} terrain, {result.PolyNodes.Count} poly-nodes)");
            return 0;
        }

        // ── Drift-lines visualisation (spawn position → final position) ──────
        if (driftLines)
        {
            using var dlImg = new MagickImage(MagickColors.White, genParams.Width, genParams.Height);
            var d = new Drawables();

            // Lines: spawn position → final position
            d.StrokeColor(new MagickColor(180, 180, 180)).StrokeWidth(1).FillColor(MagickColors.None);
            foreach (var pn in result.PolyNodes)
                d.Line(pn.SpawnPx, pn.SpawnPy, pn.Px, pn.Py);

            // Spawn positions — small grey dot
            d.FillColor(new MagickColor(150, 150, 150)).StrokeColor(new MagickColor(150, 150, 150));
            foreach (var pn in result.PolyNodes)
                d.Circle(pn.SpawnPx, pn.SpawnPy, pn.SpawnPx + 2, pn.SpawnPy);

            // Final positions — blue/red/alpha by rawRand and roughness
            foreach (var pn in result.PolyNodes)
            {
                float tv  = (pn.RawRand + 1f) / 2f;
                byte  r   = (byte)(255 * tv);
                byte  b   = (byte)(255 * (1f - tv));
                byte  alpha = (byte)(pn.IdwRoughness * 255f);
                var color = new MagickColor(r, 0, b, alpha);
                d.FillColor(color).StrokeColor(color).StrokeWidth(1);
                d.Circle(pn.Px, pn.Py, pn.Px + 3, pn.Py);
            }

            // Terrain nodes — green, on top
            d.FillColor(MagickColors.Lime).StrokeColor(MagickColors.Lime).StrokeWidth(1);
            foreach (var t in result.TerrainNodes)
                d.Circle(t.Px, t.Py, t.Px + 5, t.Py);

            dlImg.Draw(d);
            await dlImg.WriteAsync(outputPath, MagickFormat.Png);
            Console.WriteLine($"Drift-lines written to {outputPath} ({result.PolyNodes.Count} poly-nodes)");
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
        using var img = new MagickImage(result.Pixels, readSettings);
        img.Depth = 8;

        // ── Debug overlay: green = centroids; poly-nodes colored by contribution ─
        // Poly-node color: blue=negative displacement, red=positive; alpha=roughness (50%→100%)
        if (debug)
        {
            img.ColorSpace = ColorSpace.sRGB;
            var d = new Drawables();

            d.FillColor(MagickColors.Lime).StrokeColor(MagickColors.Lime).StrokeWidth(1);
            foreach (var t in result.TerrainNodes)
                d.Circle(t.Px, t.Py, t.Px + 6, t.Py);

            foreach (var pn in result.PolyNodes)
            {
                float tv  = (pn.RawRand + 1f) / 2f;                    // 0=blue, 1=red
                byte  r   = (byte)(255 * tv);
                byte  b   = (byte)(255 * (1f - tv));
                byte  alpha = (byte)(pn.IdwRoughness * 255f);    // 50%–100% opaque
                var color = new MagickColor(r, 0, b, alpha);
                d.FillColor(color).StrokeColor(color).StrokeWidth(1);
                d.Circle(pn.Px, pn.Py, pn.Px + 3, pn.Py);
            }

            img.Draw(d);
            Console.WriteLine($"Debug overlay: {result.TerrainNodes.Count} terrain (green), {result.PolyNodes.Count} poly-nodes (blue=negative, red=positive, alpha=idwRoughness)");
        }

        await img.WriteAsync(outputPath, MagickFormat.Png);
        Console.WriteLine($"Written to {outputPath}");

        return 0;
    }

    static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: HeightmapLab --json <path> --geojson <path> --output <path.png>
                                [--seed N]            default: 42
                                [--strength F]        displacement strength, default: 0.25
                                [--nodes N]           poly-nodes per land cell, default: 4
                                [--sample-count N]    IDW nearest nodes, default: 4
                                [--roughness-norm F]  normalisation factor, default: 25.0
                                [--relax N]                 repulsion relaxation iterations, default: 5
                                [--terrain-to-poly-sep F]  min distance from terrain centroid as fraction of avg terrain spacing, default: 0.25
                                [--blur-radius N]           Gaussian blur radius in pixels, default: 3
                                [--base-only]              skip poly-node displacement, show raw Delaunay layer
                                [--debug]             overlay green dots (centroids) + red dots (poly-nodes)
            """);
    }
}
