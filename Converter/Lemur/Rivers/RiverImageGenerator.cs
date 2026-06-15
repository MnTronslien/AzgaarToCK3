using Converter.Lemur.Entities;
using ImageMagick;
using System.Drawing;
using System.Linq;

namespace Converter.Lemur.Rivers
{
    public static class RiverImageGenerator
    {
        /// <summary>
        /// The nine CK3 river-body shades, palette indices 3–11, thinnest→widest. The shade is the
        /// rendered river width; we ramp along it source→mouth (see <see cref="ApplyWidthGradient"/>).
        /// </summary>
        public static readonly MagickColor[] RiverShades =
        [
            new MagickColor(0, 225, 255), // 3  thinnest
            new MagickColor(0, 200, 255), // 4
            new MagickColor(0, 150, 255), // 5
            new MagickColor(0, 100, 255), // 6
            new MagickColor(0,   0, 255), // 7
            new MagickColor(0,   0, 225), // 8
            new MagickColor(0,   0, 200), // 9
            new MagickColor(0,   0, 150), // 10
            new MagickColor(0,   0, 100), // 11 widest
        ];

        /// <summary>
        /// The single river-body shade we draw with while pathfinding lays the trail (the widest,
        /// index 11). All draw-time neighbour-detection keys off this one colour; the per-pixel width
        /// gradient across <see cref="RiverShades"/> is applied as a final recolour pass once each
        /// river's full path is known.
        /// </summary>
        public static readonly MagickColor RiverBodyColor = RiverShades[^1];

        /// <summary>
        /// Creates the base rivers image: hot-pink background with white land polygons.
        /// Caller is responsible for disposing the returned image.
        /// </summary>
        private static MagickImage CreateBaseRiversImage(Entities.Map map)
        {
            var settings = new MagickReadSettings
            {
                Width = Entities.Map.MapWidth,
                Height = Entities.Map.MapHeight
            };

            var image = new MagickImage("xc:#ff0080", settings);

            // Draw land cells as white
            var landColor = new MagickColor(255, 255, 255);
            var drawables = new Drawables();
            foreach (var cell in map.Cells!.Values.Where(c => Entities.Cell.IsDryLand(c.Type)))
            {
                drawables
                    .DisableStrokeAntialias()
                    .StrokeColor(landColor)
                    .FillColor(landColor)
                    .Polygon(cell.GeoDataCoordinates.Select(n =>
                        Helper.GeoToImage(new GeoPoint(n[0], n[1]), map).ToMagickPoint()));
            }
            image.Draw(drawables);

            return image;
        }

        /// <summary>
        /// Palette-quantizes the image using the CK3 reference file and saves to map_data/rivers.png. 
        /// Also writes a debug copy when Debug is enabled.
        /// </summary>
        private static async Task SaveRiversImage(MagickImage riversImage, string debugFileName)
        {
            // CK3 reads rivers.png by PALETTE INDEX against a fixed table — index N has a meaning
            // (0=source, 1=merge, 2=split, 3–11 river widths thin→wide, 254=sea, 255=land); the RGB
            // values are only for editor display. ImageMagick can't emit a fixed 256-entry palette
            // (it compacts/reorders the colour set, and can't hold the duplicate filler entries), so
            // our pixels ended up on the wrong indices and CK3 ignored the rivers. Convert each pixel
            // to its CK3 index and write the indexed PNG ourselves with the exact 256-entry palette.
            int w = (int)riversImage.Width, h = (int)riversImage.Height;
            var indices = BuildIndexBuffer(riversImage, w, h);

            var outputPath = Helper.GetPath(Settings.OutputDirectory, "map_data", "rivers.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            IndexedPng.Write(outputPath, indices, w, h, Ck3RiverPalette);
            Logger.Info($"\nRivers image saved to '{outputPath}' (CK3 index-correct 8-bit palette).");

            if (Settings.Instance.GenerateDebugImages)
            {
                var debugRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AzgaarToCK3",
                    "debug");
                var debugPath = Helper.GetPath(
                    debugRoot,
                    GetDebugFolderName(),
                    debugFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(debugPath)!);
                IndexedPng.Write(debugPath, indices, w, h, Ck3RiverPalette);
                ImageUtility.RegisterGeneratedImage(debugPath);
                Logger.Debug($"Saved rivers image to '{debugPath}'");
            }
        }

        /// <summary>
        /// Maps each RGB pixel of the drawn rivers image to its CK3 palette index. Pixels we draw are
        /// exact palette members (land, sea, the river-body shade, green source, red junction); any
        /// stray colour falls back to land (255) and is counted so it can't silently corrupt output.
        /// </summary>
        private static byte[] BuildIndexBuffer(MagickImage image, int w, int h)
        {
            // RGB → index, derived straight from the palette: every non-filler entry is a colour we
            // might have drawn, so the index layout lives only in Ck3RiverPalette, not here too.
            var rgbToIndex = new Dictionary<int, byte>();
            for (int i = 0; i < Ck3RiverPalette.Length; i++)
            {
                var c = Ck3RiverPalette[i];
                if (c.R == 2 && c.G == 0 && c.B == 1) continue;   // filler (#020001) — never drawn
                rgbToIndex[(c.R << 16) | (c.G << 8) | c.B] = (byte)i;
            }

            var indices = new byte[w * h];
            int unknown = 0;
            using (var pixels = image.GetPixels())
            {
                byte[] data = pixels.GetValues()!;
                int ch = data.Length / (w * h);
                for (int i = 0; i < w * h; i++)
                {
                    int o = i * ch;
                    int key = (data[o] << 16) | (data[o + 1] << 8) | data[o + 2];
                    if (rgbToIndex.TryGetValue(key, out var idx)) indices[i] = idx;
                    else { indices[i] = 255; unknown++; }   // land fallback
                }
            }
            if (unknown > 0)
                Logger.Warning($"  rivers.png: {unknown} pixel(s) had an off-palette colour, written as land (255).");
            return indices;
        }

        /// <summary>
        /// CK3's canonical rivers.png palette, in the exact index order the engine expects (verified
        /// against the shipped game/map_data/rivers.png PLTE). The index — not the RGB — is what CK3
        /// renders from: 0 source, 1 merge, 2 split, 3–11 river widths (thin→wide, = <see cref="RiverShades"/>),
        /// 12–15 reserved greens, 254 sea, 255 land; 16–253 are filler (#020001). One entry per index 0–255.
        /// Built once — the palette is a fixed contract. Exposed so the validator can check that a
        /// rivers.png carries this exact index order (CK3 reads by index; a scrambled order renders
        /// the rivers invisible even when the RGB values are right).
        /// </summary>
        internal static readonly MagickColor[] Ck3RiverPalette = BuildCk3RiverPalette();

        private static MagickColor[] BuildCk3RiverPalette()
        {
            var pal = new MagickColor[256];
            for (int i = 0; i < 256; i++) pal[i] = new MagickColor(2, 0, 1);  // filler, matches vanilla
            pal[0] = new MagickColor(0, 255, 0);    // source (green)
            pal[1] = new MagickColor(255, 0, 0);    // merge / tributary junction (red)
            pal[2] = new MagickColor(255, 252, 0);  // split (yellow)
            for (int i = 0; i < RiverShades.Length; i++)
                pal[3 + i] = RiverShades[i];        // 3–11 river widths thin→wide (single source: RiverShades)
            pal[12] = new MagickColor(0, 85, 0);
            pal[13] = new MagickColor(0, 125, 0);
            pal[14] = new MagickColor(0, 158, 0);
            pal[15] = new MagickColor(24, 206, 0);
            pal[254] = new MagickColor(255, 0, 128);     // sea
            pal[255] = new MagickColor(255, 255, 255);   // land
            return pal;
        }

        /// <summary>
        /// Writes a blank rivers.png (ocean + land colours, no river pixels).
        /// Used when rivers are skipped for any reason, so CK3 always finds a valid file.
        /// </summary>
        public static async Task DrawBlankRiversImage(Entities.Map map)
        {
            Logger.Section("Drawing Blank Rivers Image (no river pixels)");
            using var riversImage = CreateBaseRiversImage(map);
            await SaveRiversImage(riversImage, "7_rivers_blank.png");
        }

        /// <summary>
        /// Depth in the tributary tree: 0 for a mainstem, +1 per hop up the ParentId
        /// chain. Used to order drawing so every river is on the canvas before its
        /// tributaries. Guards against parent==self and cycles.
        /// </summary>
        private static int TributaryDepth(River river, Dictionary<int, River> byId)
        {
            int depth = 0;
            var seen = new HashSet<int>();
            var cur = river;
            while (cur.ParentId != 0 && cur.ParentId != cur.Id && seen.Add(cur.Id)
                   && byId.TryGetValue(cur.ParentId, out var parent))
            {
                depth++;
                cur = parent;
            }
            return depth;
        }

        /// <summary>
        /// Draws rivers.png for CK3 using manual pixel control and A* pathfinding.
        /// NO line drawing - we set each pixel individually for exact control.
        /// </summary>
        public static async Task DrawRiversImage(List<River> allRivers, float majorThreshold, Entities.Map map)
        {
            Logger.Section("Drawing Rivers Image - Pure A* Approach");

            // Draw shallower rivers (mainstems) before their tributaries so a river is
            // always on the canvas before any river that flows into it. Ordering by
            // ParentId does NOT achieve this: a river with a low Id but high ParentId
            // (Showcase "Saint": Id=2, ParentId=632) sorts AFTER its own child
            // ("Ongor", ParentId=2), so Saint collided with Ongor and truncated.
            var byId = allRivers.ToDictionary(r => r.Id);
            var minorRivers = allRivers
                .Where(r => !r.IsMajor(majorThreshold))
                .OrderBy(r => TributaryDepth(r, byId))   // parents before tributaries
                .ThenBy(r => r.Id)                       // deterministic secondary sort
                .ToList();
            Logger.Info($"Drawing {minorRivers.Count} minor rivers using pure A* pathfinding (parent rivers first)");
            Logger.Info($"No line drawing - exact pixel control with manual SetPixel");
            Logger.Info(string.Empty);

            using var riversImage = CreateBaseRiversImage(map);

            int drawnCount = 0;
            int skippedCount = 0;

            var riverColor = RiverBodyColor;  // #000064  CK3 palette index 11 (widest/darkest)
            var sourceColor = new MagickColor(0, 255, 0); // Green  – source marker (start of every river)
            var junctionColor = new MagickColor(255, 0, 0); // Red – tributary junction (end of tributaries)

            // Store actual drawn pixels for validation (not intended paths!)
            var riverActualPixels = new List<(River river, List<Point> actualPixels, bool connectedAsTributary)>();

            foreach (var river in minorRivers)
            {
                // Get control points from LineString coordinates (mandatory)
                if (river.ControlPoints == null || river.ControlPoints.Count == 0)
                {
                    Logger.Info($"SKIPPED: River {river.Id} '{river.Name}' - no control points in GeoJSON");
                    skippedCount++;
                    continue;
                }

                var controlPoints = new List<PointD>();
                foreach (var coord in river.ControlPoints)
                {
                    var pixel = Helper.GeoToImage(new GeoPoint(coord[0], coord[1]), map).ToMagickPoint();
                    controlPoints.Add(pixel);
                }

                if (controlPoints.Count < 2)
                {
                    Logger.Info($"SKIPPED: River {river.Id} '{river.Name}' - only {controlPoints.Count} control point(s)");
                    skippedCount++;
                    continue;
                }

                // Compute terminal cell check: rivers flowing into ocean or lake cells
                // must be trimmed at the cell boundary to prevent multi-river collisions.
                // MouthCellId is the last LAND cell; the true outflow cell (ocean/lake) is
                // the last entry in CellIds (azRiver.cells[^1]).
                var terminalCellId = river.CellIds.Count > 0 ? river.CellIds[^1] : river.MouthCellId;
                var terminalCell = map.Cells!.GetValueOrDefault(terminalCellId);
                bool needsTerminalTrim = terminalCell != null && !Entities.Cell.IsDryLand(terminalCell.Type);
                Func<int, int, bool>? inTerminalCell = null;
                if (needsTerminalTrim && terminalCell != null)
                {
                    var poly = terminalCell.GeoDataCoordinates
                        .Select(c => Helper.GeoToImage(new GeoPoint(c[0], c[1]), map).ToMagickPoint())
                        .ToArray();
                    int bx0 = (int)poly.Min(p => p.X), by0 = (int)poly.Min(p => p.Y);
                    int bx1 = (int)poly.Max(p => p.X), by1 = (int)poly.Max(p => p.Y);
                    inTerminalCell = (x, y) =>
                        x >= bx0 && x <= bx1 && y >= by0 && y <= by1 &&
                        Helper.PointInPolygon(poly, x, y);
                }

                // Generate complete orthogonal path using ONLY A*
                // Each segment is drawn to the image immediately via the callback so that
                // subsequent segments (and rivers) see it as an obstacle.
                var allActualPixels = new List<Point>();
                var (_, connectedAsTributary) = RiverPathGenerator.GenerateCompletePath(
                    controlPoints,
                    riversImage,
                    river.Name,
                    river.IsTributary,
                    terminalCellCheck: inTerminalCell,
                    afterSegment: segmentPixels =>
                    {
                        var drawn = RiverPixelDrawer.DrawRiverPath(riversImage, segmentPixels, riverColor);
                        allActualPixels.AddRange(drawn);
                    });

                bool dataIsTributary = river.IsTributary;   // ParentId != 0 (data-based)
                string drawnAs  = connectedAsTributary ? "tributary" : "main river";
                string dataDesc = dataIsTributary         ? "tributary" : "main river";
                if (connectedAsTributary != dataIsTributary)
                    Logger.Warning($"  WARNING: {river.Name} drawn as {drawnAs} but data says {dataDesc} (ParentId={river.ParentId})");
                else
                    Logger.Debug($"  {river.Name}: drawn as {drawnAs} (matches data)");

                if (allActualPixels.Count < 2)
                {
                    Logger.Info($"SKIPPED: River {river.Id} '{river.Name}' - A* path generation resulted in {allActualPixels.Count} pixel(s)");
                    skippedCount++;
                    continue;
                }

                // Place CK3 marker pixels:
                // Green at the upstream source — only for rivers that do NOT connect to a parent
                //   (tributaries must NOT have a green source pixel)
                // Red   at the tributary junction (last pixel of tributaries only)
                if (!connectedAsTributary)
                {
                    var sourcePixel = allActualPixels[0];
                    RiverPixelDrawer.SetPixel(riversImage, sourcePixel.X, sourcePixel.Y, sourceColor);
                }

                if (connectedAsTributary && allActualPixels.Count > 1)
                {
                    // If the path was terminated inside the terminal cell (ocean/lake cutoff),
                    // skip junction bridging entirely — there is no parent river body to connect to
                    // inside the terminal cell, and bridging would erroneously extend the path there.
                    if (inTerminalCell != null && inTerminalCell(allActualPixels[^1].X, allActualPixels[^1].Y))
                    {
                        RiverPixelDrawer.SetPixel(riversImage, allActualPixels[^1].X, allActualPixels[^1].Y, junctionColor);
                        string markerInfo2 = "[red junction placed - terminal cell, no bridge]";
                        Logger.Info($"DREW: River {river.Id} '{river.Name}' with {allActualPixels.Count} pixels {markerInfo2}");
                        riverActualPixels.Add((river, allActualPixels, connectedAsTributary));
                        drawnCount++;
                        continue;
                    }

                    // Ensure junction pixel is adjacent to the parent body before placing red marker.
                    // allActualPixels[^1] is currently BLUE (drawn by DrawRiverPath).
                    // It needs exactly 2 blue neighbors: one from this tributary's body and one from
                    // the parent river body.  If only 1 is found the path didn't quite reach the parent
                    // — bridge the gap via BFS + A*.
                    var junctionPixel = allActualPixels[^1];
                    int blueNeighbors = RiverPathGenerator.CountAdjacentBluePixels(junctionPixel, riversImage);

                    if (blueNeighbors < 2)
                    {
                        var tributarySet = new HashSet<Point>(allActualPixels);
                        var connectionPoint = FindParentConnectionPoint(
                            junctionPixel, tributarySet, riversImage, maxRadius: 150);

                        if (connectionPoint.HasValue)
                        {
                            var correctionPath = RiverPathGenerator.FindOrthogonalPath(
                                junctionPixel, connectionPoint.Value, riversImage,
                                excludeFromPass2: junctionPixel);

                            if (correctionPath != null && correctionPath.Count > 1)
                            {
                                // Skip first element (= junctionPixel, already blue in image)
                                var newPixels = correctionPath.Skip(1).ToList();
                                var drawn = RiverPixelDrawer.DrawRiverPath(riversImage, newPixels, riverColor);
                                allActualPixels.AddRange(drawn);
                                Logger.Info($"  {river.Name}: bridged junction to parent body (+{drawn.Count} pixels)");
                            }
                        }
                        else
                        {
                            Logger.Warning($"  {river.Name}: WARNING - junction could not connect to parent body within 150px");
                        }
                    }

                    RiverPixelDrawer.SetPixel(riversImage, allActualPixels[^1].X, allActualPixels[^1].Y, junctionColor);
                }

                string markerInfo = connectedAsTributary ? "[red junction placed]" : "[green source placed]";
                Logger.Info($"DREW: River {river.Id} '{river.Name}' with {allActualPixels.Count} pixels {markerInfo}");

                // Store ACTUAL pixels for validation
                riverActualPixels.Add((river, allActualPixels, connectedAsTributary));
                drawnCount++;
            }

            Logger.Info($"\nDrew {drawnCount} rivers, skipped {skippedCount} rivers");

            // Validate ACTUAL pixels from the image
            if (Settings.Instance.GenerateDebugImages && riverActualPixels.Any())
            {
                Logger.Section("Validating ACTUAL Drawn Pixels");

                var validations = new List<RiverValidation>();
                int validCount = 0;
                int invalidCount = 0;

                foreach (var (river, actualPixels, wasConnectedAsTributary) in riverActualPixels)
                {
                    // Validate the ACTUAL pixels we drew, not our intended path
                    var validation = RiverActualPixelValidator.ValidateActualPixels(actualPixels, river.Name);

                    // Red pixel rule: the junction marker must be orthogonally adjacent to
                    // exactly 2 blue pixels (one tributary body pixel + one parent body pixel).
                    // Use draw-time flag — only check if a red junction was actually placed.
                    if (wasConnectedAsTributary && actualPixels.Count >= 1)
                    {
                        var redPixel = actualPixels[^1];
                        int blueCount = RiverPathGenerator.CountAdjacentBluePixels(redPixel, riversImage);
                        if (blueCount != 2)
                        {
                            validation.RedPixelViolations.Add(new PixelViolation
                            {
                                Pixel = redPixel,
                                ActualNeighbors = blueCount,
                                ExpectedNeighbors = 2
                            });
                        }
                    }

                    validations.Add(validation);

                    if (validation.IsValid)
                    {
                        validCount++;
                    }
                    else
                    {
                        invalidCount++;
                        Logger.Info($"  {validation}");
                    }
                }

                Logger.Info($"\nValidation Summary:");
                Logger.Info($"  Valid rivers: {validCount}");
                Logger.Info($"  Invalid rivers: {invalidCount}");

                if (validations.Any())
                {
                    Logger.Info($"  Total violations: {validations.Sum(v => v.TotalViolations)}");
                }

                // Save local views for rivers with violations (first 10)
                var riversWithViolations = validations
                    .Where(v => !v.IsValid)
                    .Take(10)
                    .ToList();

                if (riversWithViolations.Any())
                {
                    Logger.Info($"\nSaving local views for {riversWithViolations.Count} rivers with violations...");

                    foreach (var validation in riversWithViolations)
                    {
                        var riverData = riverActualPixels.First(rp => rp.river.Name == validation.RiverName);
                        SaveLocalRiverView(riversImage, riverData.actualPixels, validation);
                    }
                }
            }

            // Repaint each river with a source→mouth width gradient before saving (data-driven,
            // single write pass). Drawing above used one shade so the topology/adjacency logic stays
            // simple; the shade detail is enriched here now that every river's full path is known.
            ApplyWidthGradient(riversImage, riverActualPixels);

            // SaveRiversImage converts to the 8-bit indexed PNG with CK3's exact palette and writes it.
            await SaveRiversImage(riversImage, "7_rivers.png");
        }

        private static void SaveLocalRiverView(MagickImage fullImage, List<Point> riverPixels, RiverValidation validation)
        {
            if (riverPixels.Count < 2) return;

            var minX = riverPixels.Min(p => p.X);
            var maxX = riverPixels.Max(p => p.X);
            var minY = riverPixels.Min(p => p.Y);
            var maxY = riverPixels.Max(p => p.Y);

            var padding = 20;
            var cropX = Math.Max(0, minX - padding);
            var cropY = Math.Max(0, minY - padding);
            var cropWidth = Math.Min((int)fullImage.Width - cropX, maxX - minX + 2 * padding);
            var cropHeight = Math.Min((int)fullImage.Height - cropY, maxY - minY + 2 * padding);

            using var localImage = fullImage.Clone();
            localImage.Crop(new MagickGeometry(cropX, cropY, cropWidth, cropHeight));
            localImage.RePage();

            var sanitizedName = string.Join("_", validation.RiverName.Split(Path.GetInvalidFileNameChars()));
            var filename = $"river_{sanitizedName}_local_violations.png";

            var debugPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AzgaarToCK3",
                "debug",
                GetDebugFolderName(),
                "rivers_local",
                filename);

            Directory.CreateDirectory(Path.GetDirectoryName(debugPath)!);
            localImage.Write(debugPath);
            ImageUtility.RegisterGeneratedImage(debugPath);

            Logger.Info($"    Local view: {filename}");
        }

        /// <summary>
        /// BFS from <paramref name="junction"/> through non-river pixels to find the nearest
        /// white/magenta pixel that is adjacent to a blue pixel NOT belonging to this tributary.
        /// That pixel is a valid connection point for the junction — it sits between the
        /// tributary body (blue) and the parent river body (also blue).
        /// </summary>
        private static Point? FindParentConnectionPoint(
            Point junction,
            HashSet<Point> tributaryPixels,
            MagickImage image,
            int maxRadius)
        {
            bool IsRiverColor(IMagickColor<byte> c) =>
                (c.R == RiverBodyColor.R && c.G == RiverBodyColor.G && c.B == RiverBodyColor.B) ||   // river body (#000064)
                (c.R == 255 && c.G == 0   && c.B == 0)   ||   // red   – junction marker
                (c.R == 0   && c.G == 255 && c.B == 0);        // green – source marker

            bool InBounds(Point p) =>
                p.X >= 0 && p.X < image.Width &&
                p.Y >= 0 && p.Y < image.Height;

            Point[] Neighbors(Point p) =>
            [
                new Point(p.X, p.Y - 1),
                new Point(p.X, p.Y + 1),
                new Point(p.X - 1, p.Y),
                new Point(p.X + 1, p.Y)
            ];

            // Open the pixel collection once for the entire BFS
            using var pixels = image.GetPixels();

            IMagickColor<byte>? ColorAt(Point p)
            {
                if (!InBounds(p)) return null;
                try { return pixels.GetPixel(p.X, p.Y)?.ToColor(); }
                catch { return null; }
            }

            // BFS seeds: passable (non-river) neighbours of the junction
            // The junction itself is blue — we can't traverse through it
            var queue = new Queue<(Point p, int d)>();
            var visited = new HashSet<Point> { junction };

            foreach (var n in Neighbors(junction))
            {
                if (!InBounds(n) || visited.Contains(n)) continue;
                var c = ColorAt(n);
                if (c != null && !IsRiverColor(c))
                {
                    visited.Add(n);
                    queue.Enqueue((n, 1));
                }
            }

            while (queue.Count > 0)
            {
                var (current, dist) = queue.Dequeue();
                if (dist > maxRadius) continue;

                // Valid connection point: adjacent to at least one blue pixel NOT in this tributary
                foreach (var n in Neighbors(current))
                {
                    var c = ColorAt(n);
                    if (c != null && c.R == RiverBodyColor.R && c.G == RiverBodyColor.G && c.B == RiverBodyColor.B &&
                        !tributaryPixels.Contains(n))
                    {
                        return current;
                    }
                }

                // Expand through passable pixels
                foreach (var n in Neighbors(current))
                {
                    if (visited.Contains(n) || !InBounds(n)) continue;
                    var c = ColorAt(n);
                    if (c != null && !IsRiverColor(c))
                    {
                        visited.Add(n);
                        queue.Enqueue((n, dist + 1));
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Repaints each drawn river with a width gradient: thin at the source, widening toward the
        /// mouth. The mouth shade is scaled by the river's discharge on a log scale across the map's
        /// range — so small creeks stay thin and great rivers reach the widest shade, and a tributary
        /// (lower discharge) is thinner than the trunk it joins. CK3 renders the shade as the river's
        /// width (palette index 3=thin … 11=wide). Azgaar's per-river width/sourceWidth fields are
        /// unreliable (often ~0), so discharge — its flow-volume metric — is the size signal.
        ///
        /// Pure data → one write pass: the ordered pixel list and discharge are already in memory, so
        /// no image reads are needed. The green source and red junction markers are left untouched.
        /// </summary>
        private static void ApplyWidthGradient(
            MagickImage image,
            List<(River river, List<Point> actualPixels, bool connectedAsTributary)> rivers)
        {
            // Discharge range across the drawn (minor) rivers → log scale (discharge is heavy-tailed).
            double dMin = double.MaxValue, dMax = double.MinValue;
            foreach (var (r, pts, _) in rivers)
                if (pts.Count >= 2)
                {
                    double d = Math.Max(1.0, r.Discharge);
                    dMin = Math.Min(dMin, d);
                    dMax = Math.Max(dMax, d);
                }
            if (rivers.Count == 0 || dMin == double.MaxValue) return;
            if (dMax <= dMin) dMax = dMin + 1;            // degenerate guard (all equal discharge)

            double lnMin = Math.Log(dMin), lnSpan = Math.Log(dMax) - lnMin;
            const double SourceFraction = 0.3;            // a river's source is ~30% of its mouth width
            int top = RiverShades.Length - 1;             // 8 (= palette index 11)

            using var px = image.GetPixelsUnsafe();
            foreach (var (river, pts, connectedAsTributary) in rivers)
            {
                if (pts.Count < 2) continue;

                double t = (Math.Log(Math.Max(1.0, river.Discharge)) - lnMin) / lnSpan;
                int mouthIdx = Math.Clamp((int)Math.Round(t * top), 0, top);
                int srcIdx = Math.Clamp((int)Math.Round(mouthIdx * SourceFraction), 0, mouthIdx);

                // pts run source→mouth. Skip whichever endpoint holds a marker so we don't recolour
                // it away: a normal river has its green source at [0]; a tributary has its red
                // junction at [^1].
                bool skipSource = !connectedAsTributary;   // green source pixel at [0]
                bool skipJunction = connectedAsTributary;  // red junction pixel at [^1]
                int first = skipSource ? 1 : 0;
                int last = skipJunction ? pts.Count - 2 : pts.Count - 1;
                int denom = pts.Count - 1;
                for (int i = first; i <= last; i++)
                {
                    double f = denom > 0 ? (double)i / denom : 1.0;
                    int idx = Math.Clamp((int)Math.Round(srcIdx + f * (mouthIdx - srcIdx)), 0, top);
                    var c = RiverShades[idx];
                    var pix = px[pts[i].X, pts[i].Y];
                    if (pix == null) continue;
                    pix.SetChannel(0, c.R);
                    pix.SetChannel(1, c.G);
                    pix.SetChannel(2, c.B);
                }
            }
            Logger.Info($"  Applied width gradient across {RiverShades.Length} shades (discharge {dMin:F0}–{dMax:F0}).");
        }

        private static string GetDebugFolderName() => ImageUtility.GetDebugFolderName();
    }
}
