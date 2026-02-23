using Converter.Lemur.Entities;
using ImageMagick;
using System.Drawing;
using System.Linq;

namespace Converter.Lemur.Rivers
{
    public static class RiverImageGenerator
    {
        /// <summary>
        /// Creates the base rivers image: white (land) background with hot-pink ocean polygons.
        /// Caller is responsible for disposing the returned image.
        /// </summary>
        private static MagickImage CreateBaseRiversImage(Entities.Map map)
        {
            var settings = new MagickReadSettings
            {
                Width = Entities.Map.MapWidth,
                Height = Entities.Map.MapHeight
            };

            var image = new MagickImage("xc:white", settings);

            // Draw ocean as hot-pink (CK3 convention: RGB 255, 0, 128 = #ff0080)
            var oceanColor = new MagickColor(255, 0, 128);
            var drawables = new Drawables();
            foreach (var cell in map.Cells!.Values.Where(c => !Entities.Cell.IsDryLand(c.Type)))
            {
                drawables
                    .DisableStrokeAntialias()
                    .StrokeColor(oceanColor)
                    .FillColor(oceanColor)
                    .Polygon(cell.GeoDataCoordinates.Select(n =>
                        Helper.GeoToPixel(n[0], n[1], map)));
            }
            image.Draw(drawables);

            return image;
        }

        /// <summary>
        /// Palette-quantizes the image using the CK3 reference file and saves to
        /// map_data/rivers.png. Also writes a debug copy when Debug is enabled.
        /// </summary>
        private static async Task SaveRiversImage(MagickImage riversImage, string debugFileName)
        {
            var ck3RiversPath = Path.Combine(
                Settings.Instance.Ck3Directory, "game", "map_data", "rivers.png");
            if (File.Exists(ck3RiversPath))
            {
                using var paletteRef = new MagickImage(ck3RiversPath);
                riversImage.Map(paletteRef, new QuantizeSettings { DitherMethod = DitherMethod.No });
                Console.WriteLine("  Applied CK3 palette from game reference file.");
            }
            else
            {
                Console.WriteLine($"  WARNING: CK3 rivers.png not found at '{ck3RiversPath}' — auto-quantizing.");
                riversImage.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.No });
            }
            riversImage.ColorType = ColorType.Palette;

            var outputPath = Helper.GetPath(Settings.OutputDirectory, "map_data", "rivers.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await riversImage.WriteAsync(outputPath);
            Console.WriteLine($"\nRivers image saved to '{outputPath}'");

            if (Settings.Instance.Debug)
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
                await riversImage.WriteAsync(debugPath);
                ImageUtility.RegisterGeneratedImage(debugPath);
                Console.WriteLine($"Debug: Saved rivers image to '{debugPath}'");
            }
        }

        /// <summary>
        /// Writes a blank rivers.png (ocean + land colours, no river pixels).
        /// Used when rivers are skipped for any reason, so CK3 always finds a valid file.
        /// </summary>
        public static async Task DrawBlankRiversImage(Entities.Map map)
        {
            Helper.PrintSectionHeader("Drawing Blank Rivers Image (no river pixels)");
            using var riversImage = CreateBaseRiversImage(map);
            await SaveRiversImage(riversImage, "7_rivers_blank.png");
        }

        /// <summary>
        /// Draws rivers.png for CK3 using manual pixel control and A* pathfinding.
        /// NO line drawing - we set each pixel individually for exact control.
        /// </summary>
        public static async Task DrawRiversImage(List<River> allRivers, float majorThreshold, Entities.Map map)
        {
            Helper.PrintSectionHeader("Drawing Rivers Image - Pure A* Approach");

            var minorRivers = allRivers
                .Where(r => !r.IsMajor(majorThreshold))
                .OrderBy(r => r.ParentId)   // Draw parent rivers first, then tributaries
                .ThenBy(r => r.Id)          // Deterministic secondary sort
                .ToList();
            Console.WriteLine($"Drawing {minorRivers.Count} minor rivers using pure A* pathfinding (parent rivers first)");
            Console.WriteLine($"No line drawing - exact pixel control with manual SetPixel");
            Console.WriteLine();

            using var riversImage = CreateBaseRiversImage(map);

            int drawnCount = 0;
            int skippedCount = 0;

            var riverColor = new MagickColor(0, 225, 255);  // #00e1ff  CK3 palette index 3 (thinnest minor river)
            var sourceColor = new MagickColor(0, 255, 0); // Green  – source marker (start of every river)
            var junctionColor = new MagickColor(255, 0, 0); // Red – tributary junction (end of tributaries)

            // Store actual drawn pixels for validation (not intended paths!)
            var riverActualPixels = new List<(River river, List<Point> actualPixels, bool connectedAsTributary)>();

            foreach (var river in minorRivers)
            {
                // Get control points from LineString coordinates (mandatory)
                if (river.ControlPoints == null || river.ControlPoints.Count == 0)
                {
                    Console.WriteLine($"SKIPPED: River {river.Id} '{river.Name}' - no control points in GeoJSON");
                    skippedCount++;
                    continue;
                }

                var controlPoints = new List<PointD>();
                foreach (var coord in river.ControlPoints)
                {
                    var pixel = Helper.GeoToPixel(coord[0], coord[1], map);
                    controlPoints.Add(pixel);
                }

                if (controlPoints.Count < 2)
                {
                    Console.WriteLine($"SKIPPED: River {river.Id} '{river.Name}' - only {controlPoints.Count} control point(s)");
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
                        .Select(c => Helper.GeoToPixel(c[0], c[1], map))
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
                    Console.WriteLine($"  WARNING: {river.Name} drawn as {drawnAs} but data says {dataDesc} (ParentId={river.ParentId})");
                else if (Settings.Instance.Debug)
                    Console.WriteLine($"  {river.Name}: drawn as {drawnAs} (matches data)");

                if (allActualPixels.Count < 2)
                {
                    Console.WriteLine($"SKIPPED: River {river.Id} '{river.Name}' - A* path generation resulted in {allActualPixels.Count} pixel(s)");
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
                        Console.WriteLine($"DREW: River {river.Id} '{river.Name}' with {allActualPixels.Count} pixels {markerInfo2}");
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
                                Console.WriteLine($"  {river.Name}: bridged junction to parent body (+{drawn.Count} pixels)");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"  {river.Name}: WARNING - junction could not connect to parent body within 150px");
                        }
                    }

                    RiverPixelDrawer.SetPixel(riversImage, allActualPixels[^1].X, allActualPixels[^1].Y, junctionColor);
                }

                string markerInfo = connectedAsTributary ? "[red junction placed]" : "[green source placed]";
                Console.WriteLine($"DREW: River {river.Id} '{river.Name}' with {allActualPixels.Count} pixels {markerInfo}");

                // Store ACTUAL pixels for validation
                riverActualPixels.Add((river, allActualPixels, connectedAsTributary));
                drawnCount++;
            }

            Console.WriteLine($"\nDrew {drawnCount} rivers, skipped {skippedCount} rivers");

            // Validate ACTUAL pixels from the image
            if (Settings.Instance.Debug && riverActualPixels.Any())
            {
                Helper.PrintSectionHeader("Validating ACTUAL Drawn Pixels");

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
                        Console.WriteLine($"  {validation}");
                    }
                }

                Console.WriteLine($"\nValidation Summary:");
                Console.WriteLine($"  Valid rivers: {validCount}");
                Console.WriteLine($"  Invalid rivers: {invalidCount}");

                if (validations.Any())
                {
                    Console.WriteLine($"  Total violations: {validations.Sum(v => v.TotalViolations)}");
                }

                // Save local views for rivers with violations (first 10)
                var riversWithViolations = validations
                    .Where(v => !v.IsValid)
                    .Take(10)
                    .ToList();

                if (riversWithViolations.Any())
                {
                    Console.WriteLine($"\nSaving local views for {riversWithViolations.Count} rivers with violations...");

                    foreach (var validation in riversWithViolations)
                    {
                        var riverData = riverActualPixels.First(rp => rp.river.Name == validation.RiverName);
                        SaveLocalRiverView(riversImage, riverData.actualPixels, validation);
                    }
                }
            }

            // Convert to 8-bit indexed PNG with the exact CK3 palette and save
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

            Console.WriteLine($"    Local view: {filename}");
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
                (c.R == 0   && c.G == 225 && c.B == 255) ||   // blue  – river body (#00e1ff)
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
                    if (c != null && c.R == 0 && c.G == 225 && c.B == 255 &&
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

        private static string GetDebugFolderName()
        {
            var mapName = Path.GetFileNameWithoutExtension(Settings.Instance.InputJsonPath);
            var timestamp = DateTime.Now.ToString("yyyy.MM.dd_HH.mm");
            return $"{mapName}_{timestamp}";
        }
    }
}
