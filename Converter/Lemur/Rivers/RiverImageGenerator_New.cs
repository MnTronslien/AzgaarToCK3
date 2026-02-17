using Converter.Lemur.Entities;
using ImageMagick;
using System.Drawing;

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

            var minorRivers = allRivers.Where(r => !r.IsMajor(majorThreshold)).ToList();
            Console.WriteLine($"Drawing {minorRivers.Count} minor rivers using pure A* pathfinding");
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
                // No gap filling, cleaning, or fixing - A* creates the perfect path
                var riverPath = RiverPathGenerator.GenerateCompletePath(controlPoints, riversImage, river.Name);

                if (riverPath.Count < 2)
                {
                    Console.WriteLine($"SKIPPED: River {river.Id} '{river.Name}' - A* path generation resulted in {riverPath.Count} pixel(s)");
                    skippedCount++;
                    continue;
                }

                // Manually draw each pixel (exact control, no Polyline!)
                var actualPixelsDrawn = RiverPixelDrawer.DrawRiverPath(riversImage, riverPath, riverColor);

                Console.WriteLine($"DREW: River {river.Id} '{river.Name}' with {actualPixelsDrawn.Count} pixels");

                // Store ACTUAL pixels for validation
                riverActualPixels.Add((river, actualPixelsDrawn));
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

        private static string GetDebugFolderName()
        {
            var mapName = Path.GetFileNameWithoutExtension(Settings.Instance.InputJsonPath);
            var timestamp = DateTime.Now.ToString("yyyy.MM.dd_HH.mm");
            return $"{mapName}_{timestamp}";
        }
    }
}
