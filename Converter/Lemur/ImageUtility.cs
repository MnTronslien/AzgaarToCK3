using System.Diagnostics;
using Converter.Lemur.Entities;
using ImageMagick;

namespace Converter.Lemur
{
    public static class ImageUtility
    {
        private static List<string> _generatedImages = new List<string>();
        private static string? _debugFolderName = null;

        private static string GetDebugFolderName()
        {
            if (_debugFolderName == null)
            {
                // Extract map name from input path (e.g., "Touria.json" -> "Touria")
                var mapName = Path.GetFileNameWithoutExtension(Settings.Instance.InputJsonPath);
                // Generate timestamp in YYYY.MM.DD_HH.MM format
                var timestamp = DateTime.Now.ToString("yyyy.MM.dd_HH.mm");
                _debugFolderName = $"{mapName}_{timestamp}";
            }
            return _debugFolderName;
        }

        private static string GetNumberedImageName(string name)
        {
            // Map image names to chronological order numbers
            return name switch
            {
                "counties" => "3_counties.png",
                "duchies" => "4_duchies.png",
                "kingdoms" => "5_kingdoms.png",
                "empires" => "6_empires.png",
                _ => $"{name}.png" // fallback for any other names
            };
        }

        public static void RegisterGeneratedImage(string path)
        {
            _generatedImages.Add(path);
        }

        public static void OpenAllImages()
        {
            if (_generatedImages.Count == 0) return;

            // Open first image with default viewer
            var firstImage = _generatedImages[0];
            var psi = new ProcessStartInfo
            {
                FileName = firstImage,
                UseShellExecute = true
            };
            Process.Start(psi);
            Logger.Info($"Opened first image: {Path.GetFileName(firstImage)}");
            Logger.Info($"Note: {_generatedImages.Count} total images available in folder");
        }

        public static void ClearImageRegistry()
        {
            _generatedImages.Clear();
        }

        public static void OpenImageInExplorer(string path)
        {
            //Logger.Info("Debug is on, opening the image...");
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { path },
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        public static async Task DrawCells(List<Entities.Cell> cells, Entities.Map map)
        {
            try
            {
                var settings = new MagickReadSettings()
                {
                    Width = Map.MapWidth,
                    Height = Map.MapHeight,
                };
                using var cellsMap = new MagickImage("xc:white", settings);

                var drawables = new Drawables();
                foreach (var cell in cells)
                {
                    drawables
                        .DisableStrokeAntialias()
                        .StrokeWidth(2)
                        .StrokeColor(MagickColors.Black)
                        .FillOpacity(new Percentage(0))
                        .Polygon(cell.GeoDataCoordinates.Select(n => Helper.GeoToPixel(n[0], n[1], map)));

                }

                cellsMap.Draw(drawables);

                if (Settings.Instance.GenerateDebugImages)
                {
                    var debugRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AzgaarToCK3", "debug");
                    var path = Helper.GetPath(debugRoot, GetDebugFolderName(),"1_cells.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await cellsMap.WriteAsync(path);
                    Logger.Debug($"Saved cells image to '{path}'");
                    RegisterGeneratedImage(path);
                }
            }
            catch (Exception ex)
            {
                Debugger.Break();
                throw;
            }


        }


        public static async Task DrawProvincesImage(Entities.Map map)
        {
            Logger.Info("Drawing provinces image...");
            try
            {
                var settings = new MagickReadSettings()
                {
                    Width = Map.MapWidth,
                    Height = Map.MapHeight,
                };
                using var cellsMap = new MagickImage("xc:black", settings);

                // Draw far sea zone background tiles in a grid to cover corner pixels.
                // Grid proportions match the map aspect ratio so cells are roughly square
                // (e.g. 4 cols × 2 rows for n=8 on a 2:1 canvas → 2048×2048 tiles).
                if (map.FarSeaZones != null && map.FarSeaZones.Count > 0)
                {
                    int n = map.FarSeaZones.Count;
                    double aspectRatio = (double)Map.MapWidth / Map.MapHeight;
                    int cols = Math.Max(1, (int)Math.Round(Math.Sqrt(n * aspectRatio)));
                    int rows = (int)Math.Ceiling((double)n / cols);
                    int cellW = Map.MapWidth / cols;
                    int cellH = Map.MapHeight / rows;
                    var bgDrawables = new Drawables();
                    for (int i = 0; i < n; i++)
                    {
                        int row = i / cols;
                        int col = i % cols;
                        int x = col * cellW;
                        int y = row * cellH;
                        int w = (col == cols - 1) ? Map.MapWidth - x : cellW;
                        int h = (row == rows - 1) ? Map.MapHeight - y : cellH;
                        var color = map.FarSeaZones[i].Color;
                        bgDrawables
                            .DisableStrokeAntialias()
                            .FillColor(color)
                            .StrokeColor(color)
                            .Rectangle(x, y, x + w - 1, y + h - 1);
                    }
                    cellsMap.Draw(bgDrawables);
                }

                List<Drawables> drawablesList = new();

                // Calculate true wilderness: land cells not in any named province (barony or wasteland)
                var namedProvinceCellIds = new HashSet<int>(
                    map.Baronies!.SelectMany(b => b.Cells.Select(c => c.Id))
                    .Concat(map.Wastelands!.SelectMany(w => w.Cells.Select(c => c.Id)))
                );
                var wildernessCells = map.Cells!.Values
                    .Where(cell => !namedProvinceCellIds.Contains(cell.Id) && Entities.Cell.IsDryLand(cell.Type))
                    .ToList();

                // Draw any true wilderness cells in black (should be none after AssertEveryLandCellIsAssignedToABurg)
                if (wildernessCells.Any())
                {
                    Logger.Info($"Drawing {wildernessCells.Count} wilderness cells in black");
                    drawablesList.Add(GenerateCellPolygons(wildernessCells, MagickColors.Black, map));
                }

                // Draw baronies
                foreach (var province in map.Baronies!.Cast<IProvince>())
                    drawablesList.Add(GenerateCellPolygons(province.Cells, province.Color, map));

                // Draw wastelands (impassable land provinces — must be colored so CK3 can map pixel → province ID)
                foreach (var wasteland in map.Wastelands!.Cast<IProvince>())
                    drawablesList.Add(GenerateCellPolygons(wasteland.Cells, wasteland.Color, map));

                // Draw sea zones
                foreach (var zone in map.SeaZones!)
                    drawablesList.Add(GenerateCellPolygons(zone.Cells, zone.Color, map));

                // Flatten the list of Drawables into a single collection of IDrawable
                IEnumerable<IDrawable> drawables = drawablesList.SelectMany(d => d);

                cellsMap.Draw(drawables);

                // Strip alpha — ensure 24-bit RGB output (prevents CK3 CTD)
                cellsMap.HasAlpha = false;

                // Always save production provinces.png to map_data
                var productionPath = Helper.GetPath(Settings.OutputDirectory, "map_data", "provinces.png");
                Directory.CreateDirectory(Path.GetDirectoryName(productionPath)!);
                Logger.Info($"Saving provinces image to '{productionPath}'");
                await cellsMap.WriteAsync(productionPath);
                Logger.Info($"Provinces image has been drawn and saved to '{productionPath}'");

                // If debug enabled, also save as baronies.png in debug folder
                if (Settings.Instance.GenerateDebugImages)
                {
                    var debugRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AzgaarToCK3", "debug");
                    var debugPath = Helper.GetPath(debugRoot, GetDebugFolderName(),"2_baronies.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(debugPath)!);
                    await cellsMap.WriteAsync(debugPath);
                    Logger.Debug($"Saved baronies image to '{debugPath}'");
                    RegisterGeneratedImage(debugPath);
                }

            }
            catch (Exception ex)
            {
                Debugger.Break();
                throw;
            }
        }

        public static async Task DrawCellsWithColourImage(
            Dictionary<MagickColor, List<Entities.Cell>> colorCellsMap,
            Entities.Map map,
            string name = "colorCellsMap",
            System.Drawing.Color background = default)
        {
            Logger.Info("Drawing cells by couloured groups to image...");
            try
            {
                // Default background to blue (ocean) if not specified
                if (background == default(System.Drawing.Color))
                {
                    background = System.Drawing.Color.FromArgb(68, 107, 163); // CK3-style ocean blue
                    // TODO: Add simple texture to ocean to make it distinct from blueish land regions
                }

                var settings = new MagickReadSettings()
                {
                    Width = Map.MapWidth,
                    Height = Map.MapHeight,
                };
                using var cellsMap = new MagickImage($"xc:#{background.R:X2}{background.G:X2}{background.B:X2}", settings);

                List<Drawables> drawablesList = new();

                // Calculate wilderness cells (cells not in any colored group)
                var coloredCellIds = new HashSet<int>(
                    colorCellsMap.Values.SelectMany(cells => cells.Select(c => c.Id))
                );
                var wildernessCells = map.Cells!.Values
                    .Where(cell => !coloredCellIds.Contains(cell.Id) && Entities.Cell.IsDryLand(cell.Type))
                    .ToList();

                // Draw wilderness cells in black first (so they appear as background layer)
                if (wildernessCells.Any())
                {
                    Logger.Info($"Drawing {wildernessCells.Count} wilderness cells in black");
                    drawablesList.Add(GenerateCellPolygons(wildernessCells, MagickColors.Black, map));
                }

                // Draw colored cells on top
                foreach (var group in colorCellsMap)
                {
                    drawablesList.Add(GenerateCellPolygons(group.Value, group.Key, map));
                }

                // Flatten the list of Drawables into a single collection of IDrawable
                IEnumerable<IDrawable> drawables = drawablesList.SelectMany(d => d);

                cellsMap.Draw(drawables);

                if (Settings.Instance.GenerateDebugImages)
                {
                    var numberedName = GetNumberedImageName(name);
                    var debugRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AzgaarToCK3", "debug");
                    var path = Helper.GetPath(debugRoot, GetDebugFolderName(), numberedName);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    Logger.Debug($"Saving {name} image to '{path}'");
                    await cellsMap.WriteAsync(path);
                    Logger.Debug($"{name} image saved to '{path}'");
                    RegisterGeneratedImage(path);
                }

            }
            catch (Exception ex)
            {
                Debugger.Break();
                throw;
            }


        }

        public static async Task DrawSeaZonesImage(Entities.Map map)
        {
            Logger.Info("Drawing sea zones image...");
            try
            {
                var readSettings = new MagickReadSettings()
                {
                    Width = Map.MapWidth,
                    Height = Map.MapHeight,
                };
                using var image = new MagickImage("xc:black", readSettings);

                var drawablesList = new List<Drawables>();

                // Fill land cells black (clean edges against sea)
                var landCells = map.Cells!.Values
                    .Where(c => Entities.Cell.IsDryLand(c.Type))
                    .ToList();
                if (landCells.Any())
                    drawablesList.Add(GenerateCellPolygons(landCells, MagickColors.Black, map));

                // Fill sea zones with their unique colors
                foreach (var zone in map.SeaZones!)
                    drawablesList.Add(GenerateCellPolygons(zone.Cells, zone.Color, map));

                image.Draw(drawablesList.SelectMany(d => d));

                // Overlay cell grid on sea cells only (thin black outlines; invisible on black land)
                var seaCellIds = new HashSet<int>(
                    map.SeaZones.SelectMany(z => z.Cells.Select(c => c.Id)));
                var gridDrawables = new Drawables();
                foreach (var cell in map.Cells.Values)
                {
                    if (!seaCellIds.Contains(cell.Id)) continue;
                    gridDrawables
                        .DisableStrokeAntialias()
                        .StrokeWidth(1)
                        .StrokeColor(MagickColors.Black)
                        .FillOpacity(new Percentage(0))
                        .Polygon(cell.GeoDataCoordinates.Select(
                            n => new PointD((n[0] - map.XOffset) * map.XRatio,
                                           Map.MapHeight - (n[1] - map.YOffset) * map.YRatio)));
                }
                image.Draw(gridDrawables);

                if (Settings.Instance.GenerateDebugImages)
                {
                    var debugRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AzgaarToCK3", "debug");
                    var path = Helper.GetPath(debugRoot, GetDebugFolderName(), "2_sea_zones.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await image.WriteAsync(path);
                    Logger.Debug($"Saved sea zones image to '{path}'");
                    RegisterGeneratedImage(path);
                }
            }
            catch (Exception ex)
            {
                Debugger.Break();
                throw;
            }
        }

        internal static Drawables GenerateCellPolygons(IEnumerable<Entities.Cell> cells, MagickColor color, Entities.Map map)
        {
            var drawables = new Drawables();
            foreach (var cell in cells)
            {
                drawables
                    .DisableStrokeAntialias()
                    .StrokeColor(color)
                    .FillColor(color)
                    .Polygon(cell.GeoDataCoordinates.Select(n => new PointD((n[0] - map.XOffset) * map.XRatio, Map.MapHeight - (n[1] - map.YOffset) * map.YRatio)));
            }
            return drawables;
        }
    }
}