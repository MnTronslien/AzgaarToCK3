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
        float strength = 0.25f, roughnessNorm = 25.0f;
        int nodesPerCell = 4;
        int sampleCount = 4;
        bool baseOnly = false;
        bool debug = false;
        bool mesh = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json":           jsonPath      = args[++i]; break;
                case "--geojson":        geojsonPath   = args[++i]; break;
                case "--output":         outputPath    = args[++i]; break;
                case "--seed":           seed          = int.Parse(args[++i]); break;
                case "--strength":       strength      = float.Parse(args[++i]); break;
                case "--nodes":          nodesPerCell  = int.Parse(args[++i]); break;
                case "--sample-count":   sampleCount   = int.Parse(args[++i]); break;
                case "--roughness-norm": roughnessNorm = float.Parse(args[++i]); break;
                case "--base-only":      baseOnly      = true; break;
                case "--debug":          debug         = true; break;
                case "--mesh":           mesh          = true; break;
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
            PolyNodeSampleCount: sampleCount,
            RoughnessNorm: roughnessNorm,
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

            var allPts = result.Centroids.Concat(result.PolyNodes).ToList();

            // Delaunay triangulation of the combined point set
            var gf = new GeometryFactory();
            var builder = new DelaunayTriangulationBuilder();
            builder.SetSites(gf.CreateMultiPointFromCoords(
                allPts.Select(p => new Coordinate(p.px, p.py)).ToArray()));
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

            // Terrain centroids — green, radius 6
            d.FillColor(MagickColors.Lime).StrokeColor(MagickColors.Lime).StrokeWidth(1);
            foreach (var (px, py) in result.Centroids)
                d.Circle(px, py, px + 6, py);

            // Poly-nodes — red, radius 3
            d.FillColor(MagickColors.Red).StrokeColor(MagickColors.Red);
            foreach (var (px, py) in result.PolyNodes)
                d.Circle(px, py, px + 3, py);

            meshImg.Draw(d);
            await meshImg.WriteAsync(outputPath, MagickFormat.Png);
            Console.WriteLine($"Mesh written to {outputPath} ({result.Centroids.Count} centroids, {result.PolyNodes.Count} poly-nodes, {triangles.NumGeometries} triangles)");
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

        // ── Debug overlay: green dots = cell centroids, red dots = poly-nodes ─
        if (debug)
        {
            img.ColorSpace = ColorSpace.sRGB;
            var d = new Drawables();

            d.FillColor(MagickColors.Lime).StrokeColor(MagickColors.Lime);
            foreach (var (px, py) in result.Centroids)
                d.Circle(px, py, px + 6, py);

            d.FillColor(MagickColors.Red).StrokeColor(MagickColors.Red);
            foreach (var (px, py) in result.PolyNodes)
                d.Circle(px, py, px + 3, py);

            img.Draw(d);
            Console.WriteLine($"Debug overlay: {result.Centroids.Count} centroids (green), {result.PolyNodes.Count} poly-nodes (red)");
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
                                [--base-only]         skip poly-node displacement, show raw Delaunay layer
                                [--debug]             overlay green dots (centroids) + red dots (poly-nodes)
            """);
    }
}
