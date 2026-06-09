using Converter.Lemur.Entities;
using ImageMagick;
using System.Drawing;
using System.Linq;

namespace Converter.Lemur.Rivers
{
    public static class RiverImageGenerator
    {
        /// <summary>
        /// The river-body shade we draw. CK3 palette index 11 (#000064) — the widest/darkest
        /// shade. All neighbour-detection in the river pipeline keys off this single colour, so
        /// it is the one source of truth: change it here and the whole pipeline follows.
        /// </summary>
        public static readonly MagickColor RiverBodyColor = new MagickColor(0, 0, 100);

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
                        Helper.GeoToPixel(n[0], n[1], map)));
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
            // Force PNG palette type (color-type 1 = indexed/palette) before mapping.
            // This ensures CK3's expected 8-bit indexed format regardless of how many
            // unique colors are present (e.g. blank rivers image with only 2 colors).
            riversImage.Settings.SetDefine("png:color-type", "1");

            string[] colormap = [
                "#00FF00",
                "#FF0000",
                "#FFFC00",
                "#00E1FF",
                "#00C8FF",
                "#0096FF",
                "#0064FF",
                "#0000FF",
                "#0000E1",
                "#0000C8",
                "#000096",
                "#000064",
                "#005500",
                "#007D00",
                "#009E00",
                "#18CE00",
                "#FF0080",
                "#FFFFFF",
            ];
            riversImage.Map(colormap.Select(n => new MagickColor(n)));
            Logger.Info($"  Applied hardcoded CK3 rivers colormap ({colormap.Length} entries).");

            var outputPath = Helper.GetPath(Settings.OutputDirectory, "map_data", "rivers.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await riversImage.WriteAsync(outputPath);
            Logger.Info($"\nRivers image saved to '{outputPath}'");

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
                await riversImage.WriteAsync(debugPath);
                ImageUtility.RegisterGeneratedImage(debugPath);
                Logger.Debug($"Saved rivers image to '{debugPath}'");
            }
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
        /// Draws rivers.png for CK3 using manual pixel control and A* pathfinding.
        /// NO line drawing - we set each pixel individually for exact control.
        /// </summary>
        public static async Task DrawRiversImage(List<River> allRivers, float majorThreshold, Entities.Map map)
        {
            Logger.Section("Drawing Rivers Image - Pure A* Approach");

            var minorRivers = allRivers
                .Where(r => !r.IsMajor(majorThreshold))
                .OrderBy(r => r.ParentId)   // Draw parent rivers first, then tributaries
                .ThenBy(r => r.Id)          // Deterministic secondary sort
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
                    var pixel = Helper.GeoToPixel(coord[0], coord[1], map);
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

        private static string GetDebugFolderName() => ImageUtility.GetDebugFolderName();
    }
}
