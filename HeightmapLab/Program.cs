using Converter;
using Converter.Lemur.Deserialization;
using ImageMagick;

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

        // ── Write PNG ────────────────────────────────────────────────────────
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

            // Cell centroids — green, radius 6
            d.FillColor(MagickColors.Lime).StrokeColor(MagickColors.Lime);
            foreach (var (px, py) in result.Centroids)
                d.Circle(px, py, px + 6, py);

            // Poly-nodes — red, radius 3
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
