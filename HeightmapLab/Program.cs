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
        string? jsonPath = null, geojsonPath = null, outputPath = null, terrainOut = null;
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
                case "--base-only":           baseOnly         = true; break;
                case "--debug":               debug            = true; break;
                case "--mesh":                mesh             = true; break;
                case "--spawn-lines":         spawnLines       = true; break;
                case "--drift-lines":         driftLines       = true; break;
                case "--steepness-map":       steepnessMap     = true; break;
                case "--roughness-map":       roughnessMap     = true; break;
                case "--coast-map":           coastMap         = true; break;
                case "--detail-intensity":    detailIntensity  = true; break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    PrintUsage();
                    return 1;
            }
        }

        // --detail-intensity needs only --terrain-out (or --output for its dir); no Azgaar data needed
        bool dataRequired = !detailIntensity;
        bool outputRequired = !detailIntensity;
        if ((dataRequired && (jsonPath == null || geojsonPath == null)) || (outputRequired && outputPath == null && terrainOut == null))
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

        // ── Steepness map: black=flat, white=vertical ────────────────────────
        if (steepnessMap)
        {
            // Use float heightmap (pre-quantization) — byte pixels have only 256 discrete
            // levels, which produces terracing artifacts in the normal computation.
            var hf = result.HeightmapF;
            var pixels = result.Pixels;  // still needed for ocean mask
            int w = genParams.Width, h = genParams.Height;
            const int kd = 4;
            const float wl = HeightmapGenerator.CK3WaterLevel;

            // Surface normal: N = normalize(-Gx, -Gy, 1)
            // Divide by 2*kd so Gx/Gy are gradient per pixel, not per 2*kd pixels.
            // steepness = 1 - N.z = 1 - 1/sqrt(Gx²+Gy²+1)
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

            // p95 over land pixels — maps the full range to 0–255
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
        // Rasterizes the Delaunay triangulation of terrain nodes only, interpolating
        // TerrainNode.Roughness barycentrically — the same approach used for the heightmap.
        if (roughnessMap)
        {
            int w = genParams.Width, h = genParams.Height;
            const byte wl = 20;

            var roughF = HeightmapGenerator.RasterizeRoughness(result.TerrainNodes, genParams);

            var roughBytes = new byte[w * h];
            for (int idx = 0; idx < roughF.Length; idx++)
            {
                if (result.Pixels[idx] < wl) continue;
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
            for (int idx = 0; idx < result.Pixels.Length; idx++)
            {
                byte px = result.Pixels[idx];
                int o = idx * 3;
                if (px < wl)
                {
                    rgb[o]     = 0;
                    rgb[o + 1] = (byte)(px * 3);        // slight green tint near shore
                    rgb[o + 2] = (byte)(120 + px * 6);  // blue, brighter closer to shore
                }
                else
                {
                    rgb[o] = rgb[o + 1] = rgb[o + 2] = px;
                }
            }

            var cm = new MagickReadSettings { Width = w, Height = h, ColorSpace = ColorSpace.sRGB, Format = MagickFormat.Rgb };
            using var coastImg = new MagickImage(rgb, cm);
            coastImg.Depth = 8;

            // Overlay nodes when --debug is also set
            {
                var d = new Drawables();

                if (debug)
                {
                    // Terrain centroids — green
                    d.FillColor(MagickColors.Lime).StrokeColor(MagickColors.Lime).StrokeWidth(1);
                    foreach (var t in result.TerrainNodes)
                        d.Circle(t.Px, t.Py, t.Px + 6, t.Py);

                    // Poly-nodes — blue=negative displacement, red=positive
                    foreach (var pn in result.PolyNodes)
                    {
                        float tv = (pn.RawRand + 1f) / 2f;
                        var color = new MagickColor((byte)(255 * tv), 0, (byte)(255 * (1f - tv)), (byte)(pn.IdwRoughness * 255f));
                        d.FillColor(color).StrokeColor(color).StrokeWidth(1);
                        d.Circle(pn.Px, pn.Py, pn.Px + 3, pn.Py);
                    }
                }

                // Coast constraint nodes — yellow (always shown on coast-map)
                d.FillColor(MagickColors.Yellow).StrokeColor(MagickColors.Yellow).StrokeWidth(1);
                foreach (var cn in result.CoastNodes)
                    d.Circle(cn.Px, cn.Py, cn.Px + 2, cn.Py);

                coastImg.Draw(d);
            }

            await coastImg.WriteAsync(outputPath, MagickFormat.Png);
            Console.WriteLine($"Coast map written to {outputPath} ({result.CoastNodes.Count} coast nodes yellow" +
                (debug ? $", {result.TerrainNodes.Count} terrain green, {result.PolyNodes.Count} poly-nodes red/blue" : "") +
                $"; water level={wl})");
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

        await img.WriteAsync(outputPath!, MagickFormat.Png);
        Console.WriteLine($"Written to {outputPath}");

        // ── Hot-reload: write geometry masks directly into mod terrain folder ─
        if (terrainOut != null)
        {
            var masksDir = Path.Combine(terrainOut, "masks");
            Directory.CreateDirectory(masksDir);
            await Converter.Lemur.Writers.HeightmapMasks.Write(
                result.HeightmapF, result.Pixels,
                genParams.Width, genParams.Height,
                masksDir);
            Console.WriteLine($"Hills/mountains/snow masks written to {masksDir}");
        }

        return 0;
    }

    // Matrix checkerboard: rows cycle R/G/B, columns cycle R/G/B independently.
    // Each pixel gets 255 in a channel if that channel is active on EITHER its row or column band.
    // 9 unique combinations: R, G, B, R+G, R+B, G+B, R+G, R+B, G+B (tiling 3×3 matrix).
    // Tile size 128px (quarter of old 512).
    static async Task WriteCheckerboardDetailIntensity(string terrainDir)
    {
        const int tileSize = 128;
        int w = Converter.Lemur.Entities.Map.MapWidth;
        int h = Converter.Lemur.Entities.Map.MapHeight;
        var pixels = new byte[w * h * 3];

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int rowCh = (y / tileSize) % 3; // 0=R 1=G 2=B
            int colCh = (x / tileSize) % 3;
            int o = (y * w + x) * 3;
            pixels[o]     = (rowCh == 0 || colCh == 0) ? (byte)255 : (byte)0; // R
            pixels[o + 1] = (rowCh == 1 || colCh == 1) ? (byte)255 : (byte)0; // G
            pixels[o + 2] = (rowCh == 2 || colCh == 2) ? (byte)255 : (byte)0; // B
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
