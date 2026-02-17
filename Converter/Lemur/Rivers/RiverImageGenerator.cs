using Converter.Lemur.Entities;
using ImageMagick;

namespace Converter.Lemur.Rivers
{
    public static class RiverImageGenerator
    {
        /// <summary>
        /// Draws rivers.png for CK3.
        /// Format: White land, magenta ocean, gradient blue rivers based on width.
        /// </summary>
        public static async Task DrawRiversImage(List<River> allRivers, float majorThreshold, Entities.Map map)
        {
            Helper.PrintSectionHeader("Drawing Rivers Image");

            var minorRivers = allRivers.Where(r => !r.IsMajor(majorThreshold)).ToList();
            Console.WriteLine($"Drawing {minorRivers.Count} minor rivers to rivers.png");

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

            // Draw rivers using CK3 color scheme:
            // - Blue gradient (light to dark) for river width
            // - Green (#00ff00) for river source
            // - Red (#ff0000) for tributaries (future)
            // - Yellow (#fffc00) for splits (future)

            Helper.PrintSectionHeader("Processing Rivers");
            Console.WriteLine($"Phase 2D: Filling gaps with A* pathfinding...");
            Console.WriteLine($"Phase 2B: Removing duplicate pixels...");
            Console.WriteLine($"Phase 2E: Fixing self-violations (iterative)...");
            Console.WriteLine();

            int drawnCount = 0;
            int skippedCount = 0;

            // Store river paths for validation after drawing
            var riverPaths = new List<(River river, List<PointD> points)>();

            foreach (var river in minorRivers)
            {
                // Draw river path through cells by connecting cell centroids
                // TODO: Implement Bezier curves for smoother river paths
                var points = new List<PointD>();

                foreach (var cellId in river.CellIds)
                {
                    if (map.Cells.ContainsKey(cellId))
                    {
                        var cell = map.Cells[cellId];
                        // Get centroid of the cell
                        var centroid = Helper.GeoToPixel(
                            cell.GeoDataCoordinates[0][0],
                            cell.GeoDataCoordinates[0][1],
                            map);
                        points.Add(centroid);
                    }
                }

                if (points.Count >= 2)
                {
                    // Phase 2D: Fill gaps with A* pathfinding to create orthogonal paths
                    points = RiverGapFiller.FillGaps(points, riversImage, river.Name);

                    // Phase 2B: Remove duplicate pixels from the path
                    points = RiverPathCleaner.CleanPath(points, river.Name);

                    // Phase 2E: Fix self-violations (pixels with wrong neighbor count)
                    // Use more iterations for complex rivers
                    points = RiverSelfViolationFixer.FixSelfViolations(points, riversImage, river.Name, maxIterations: 20);

                    // Check if gap filling + cleaning left us with enough points to draw (Polyline requires min 3 points)
                    if (points.Count < 3)
                    {
                        Console.WriteLine($"SKIPPED: River {river.Id} '{river.Name}' - processing resulted in only {points.Count} point(s) (need 3+)");
                        skippedCount++;
                        continue;
                    }

                    // FIXED: Deep dark blue color for all rivers (not dynamic)
                    var riverColor = new MagickColor(0, 0, 180);  // Deep dark blue RGB(0, 0, 180)

                    // FINAL: 1px stroke for CK3 river map
                    var strokeWidth = 1;

                    // FIX: Draw each river immediately instead of batching
                    var riverDrawables = new Drawables();
                    riverDrawables
                        .StrokeColor(riverColor)
                        .StrokeWidth(strokeWidth)
                        .FillOpacity(new Percentage(0))
                        .Polyline(points);

                    // TODO: Mark river source with green dot (single pixel, not large circle)
                    // Temporarily disabled - the 20px circle was interfering with validation
                    // if (points.Count > 0)
                    // {
                    //     var sourcePoint = points[0];
                    //     riverDrawables
                    //         .FillColor(new MagickColor("#00ff00"))  // Pure green for source
                    //         .StrokeOpacity(new Percentage(0))
                    //         .Point(sourcePoint.X, sourcePoint.Y);  // Should be single pixel
                    // }

                    // Draw this river NOW
                    riversImage.Draw(riverDrawables);

                    // Store for validation
                    riverPaths.Add((river, points));

                    Console.WriteLine($"DREW: River {river.Id} '{river.Name}' with {points.Count} points - First: ({points[0].X:F0},{points[0].Y:F0}) Last: ({points[points.Count-1].X:F0},{points[points.Count-1].Y:F0})");
                    drawnCount++;
                }
                else
                {
                    Console.WriteLine($"SKIPPED: River {river.Id} '{river.Name}' - only {points.Count} point(s)");
                    skippedCount++;
                }
            }

            Console.WriteLine($"Drew {drawnCount} rivers, skipped {skippedCount} rivers");

            // Validate rivers against CK3 topology rules
            if (Settings.Instance.Debug && riverPaths.Any())
            {
                Helper.PrintSectionHeader("Validating Rivers (CK3 Topology Rules)");

                var validations = new List<RiverValidation>();
                int validCount = 0;
                int invalidCount = 0;

                foreach (var (river, points) in riverPaths)
                {
                    // Use path-based validation with image context for offshore checking
                    var validation = RiverPathValidator.ValidatePath(points, river.Name, riversImage);
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
                    Console.WriteLine($"  Max gap distance: {validations.Max(v => v.MaxGapDistance)} pixels");
                    Console.WriteLine($"  Avg gap distance: {validations.Average(v => v.AverageGapDistance):F1} pixels");
                    Console.WriteLine($"  Total violations: {validations.Sum(v => v.TotalViolations)}");
                }

                // Save local views for rivers with violations (first 10 to avoid clutter)
                var riversWithViolations = validations
                    .Where(v => !v.IsValid)
                    .Take(10)
                    .ToList();

                if (riversWithViolations.Any())
                {
                    Console.WriteLine($"\nSaving local views for {riversWithViolations.Count} rivers with violations...");

                    foreach (var validation in riversWithViolations)
                    {
                        var riverPath = riverPaths.First(rp => rp.river.Name == validation.RiverName);
                        RiverValidator.SaveLocalRiverImage(
                            riversImage,
                            riverPath.points,
                            validation.RiverName,
                            validation,
                            "violations");
                    }
                }
            }

            // Save to mod's map_data folder
            var outputPath = Helper.GetPath(Settings.OutputDirectory, "map_data", "rivers.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await riversImage.WriteAsync(outputPath);
            Console.WriteLine($"Rivers image saved to '{outputPath}'");

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

        private static string GetDebugFolderName()
        {
            var mapName = Path.GetFileNameWithoutExtension(Settings.Instance.InputJsonPath);
            var timestamp = DateTime.Now.ToString("yyyy.MM.dd_HH.mm");
            return $"{mapName}_{timestamp}";
        }
    }
}
