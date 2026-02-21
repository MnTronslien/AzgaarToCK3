using Converter.Lemur.Entities;
using ImageMagick;
using System.Drawing;
using System.Linq;

namespace Converter.Lemur.Rivers
{
    public static class RiverImageGeneratorNew
    {
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

            var settings = new MagickReadSettings
            {
                Width = Entities.Map.MapWidth,
                Height = Entities.Map.MapHeight
            };

            // Start with white background (land)
            using var riversImage = new MagickImage("xc:white", settings);

            // Draw ocean as magenta (CK3 convention: RGB 255, 0, 255)
            var drawables = new Drawables();
            foreach (var cell in map.Cells!.Values.Where(c => !Entities.Cell.IsDryLand(c.Type)))
            {
                drawables
                    .DisableStrokeAntialias()
                    .StrokeColor(MagickColors.Magenta)
                    .FillColor(MagickColors.Magenta)
                    .Polygon(cell.GeoDataCoordinates.Select(n =>
                        Helper.GeoToPixel(n[0], n[1], map)));
            }
            riversImage.Draw(drawables);

            int drawnCount = 0;
            int skippedCount = 0;

            var riverColor = new MagickColor(0, 0, 180);  // Deep dark blue RGB(0, 0, 180)
            var sourceColor = new MagickColor(0, 255, 0); // Green  – source marker (start of every river)
            var junctionColor = new MagickColor(255, 0, 0); // Red – tributary junction (end of tributaries)

            // Store actual drawn pixels for validation (not intended paths!)
            var riverActualPixels = new List<(River river, List<Point> actualPixels)>();

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

                // Generate complete orthogonal path using ONLY A*
                // Each segment is drawn to the image immediately via the callback so that
                // subsequent segments (and rivers) see it as an obstacle.
                var allActualPixels = new List<Point>();
                RiverPathGenerator.GenerateCompletePath(
                    controlPoints,
                    riversImage,
                    river.Name,
                    river.IsTributary,
                    afterSegment: segmentPixels =>
                    {
                        var drawn = RiverPixelDrawer.DrawRiverPath(riversImage, segmentPixels, riverColor);
                        allActualPixels.AddRange(drawn);
                    });

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
                if (!river.IsTributary)
                {
                    var sourcePixel = allActualPixels[0];
                    RiverPixelDrawer.SetPixel(riversImage, sourcePixel.X, sourcePixel.Y, sourceColor);
                }

                if (river.IsTributary && allActualPixels.Count > 1)
                {
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

                string markerInfo = river.IsTributary ? "[red junction placed]" : "[green source placed]";
                Console.WriteLine($"DREW: River {river.Id} '{river.Name}' with {allActualPixels.Count} pixels {markerInfo}");

                // Store ACTUAL pixels for validation
                riverActualPixels.Add((river, allActualPixels));
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

                foreach (var (river, actualPixels) in riverActualPixels)
                {
                    // Validate the ACTUAL pixels we drew, not our intended path
                    var validation = RiverActualPixelValidator.ValidateActualPixels(actualPixels, river.Name);

                    // Red pixel rule: the junction marker must be orthogonally adjacent to
                    // exactly 2 blue pixels (one tributary body pixel + one parent body pixel).
                    if (river.IsTributary && actualPixels.Count >= 1)
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

            // Save to mod's map_data folder
            var outputPath = Helper.GetPath(Settings.OutputDirectory, "map_data", "rivers.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await riversImage.WriteAsync(outputPath);
            Console.WriteLine($"\nRivers image saved to '{outputPath}'");

            // Debug: Also save numbered copy if debug enabled
            if (Settings.Instance.Debug)
            {
                var debugRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AzgaarToCK3",
                    "debug");
                var debugPath = Helper.GetPath(
                    debugRoot,
                    GetDebugFolderName(),
                    "7_rivers.png");
                Directory.CreateDirectory(Path.GetDirectoryName(debugPath)!);
                await riversImage.WriteAsync(debugPath);
                ImageUtility.RegisterGeneratedImage(debugPath);
                Console.WriteLine($"Debug: Saved rivers image to '{debugPath}'");
            }
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
                (c.R == 0   && c.G == 0   && c.B == 180) ||   // blue  – river body
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
                    if (c != null && c.R == 0 && c.G == 0 && c.B == 180 &&
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
