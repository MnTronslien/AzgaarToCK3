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
            Console.WriteLine($"Opened first image: {Path.GetFileName(firstImage)}");
            Console.WriteLine($"Note: {_generatedImages.Count} total images available in folder");
        }

        public static void ClearImageRegistry()
        {
            _generatedImages.Clear();
        }

        public static void OpenImageInExplorer(string path)
        {
            //Console.WriteLine("Debug is on, opening the image...");
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

                if (Settings.Instance.Debug)
                {
                    var path = Helper.GetPath(Settings.OutputDirectory, GetDebugFolderName(),"1_cells.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await cellsMap.WriteAsync(path);
                    Console.WriteLine($"Debug: Saved cells image to '{path}'");
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
            Console.WriteLine("Drawing provinces image...");
            try
            {
                // Use CK3-style ocean blue background
                var background = System.Drawing.Color.FromArgb(68, 107, 163);
                var settings = new MagickReadSettings()
                {
                    Width = Map.MapWidth,
                    Height = Map.MapHeight,
                };
                using var cellsMap = new MagickImage($"xc:#{background.R:X2}{background.G:X2}{background.B:X2}", settings);

                List<Drawables> drawablesList = new();
                //=================
                // EXTEND THIS TO DRAW ALL PROVINCEs, not just baronies. Should also draw inn wasteland, major rivers and sea zones
                //=================
                //concat the list from baronies and in the future major rivers and sea zones
                List<IProvince> provincesToDraw = map.Baronies!.Cast<IProvince>().ToList();

                // Calculate wilderness cells (cells not in any barony)
                var baronyCellIds = new HashSet<int>(
                    map.Baronies!.SelectMany(b => b.Cells.Select(c => c.Id))
                );
                var wildernessCells = map.Cells!.Values
                    .Where(cell => !baronyCellIds.Contains(cell.Id) && Entities.Cell.IsDryLand(cell.Type))
                    .ToList();

                // Draw wilderness cells in black first (background layer)
                if (wildernessCells.Any())
                {
                    Console.WriteLine($"Drawing {wildernessCells.Count} wilderness cells in black");
                    drawablesList.Add(GenerateCellPolygons(wildernessCells, MagickColors.Black, map));
                }

                // Draw baronies on top
                foreach (var province in provincesToDraw)
                {
                    drawablesList.Add(GenerateCellPolygons(province.Cells, province.Color, map));
                }

                // Flatten the list of Drawables into a single collection of IDrawable
                IEnumerable<IDrawable> drawables = drawablesList.SelectMany(d => d);

                cellsMap.Draw(drawables);

                // Always save production provinces.png to map_data
                var productionPath = Helper.GetPath(Settings.OutputDirectory, "map_data", "provinces.png");
                Directory.CreateDirectory(Path.GetDirectoryName(productionPath)!);
                Console.WriteLine($"Saving provinces image to '{productionPath}'");
                await cellsMap.WriteAsync(productionPath);
                Console.WriteLine($"Provinces image has been drawn and saved to '{productionPath}'");

                // If debug enabled, also save as baronies.png in debug folder
                if (Settings.Instance.Debug)
                {
                    var debugPath = Helper.GetPath(Settings.OutputDirectory, GetDebugFolderName(),"2_baronies.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(debugPath)!);
                    await cellsMap.WriteAsync(debugPath);
                    Console.WriteLine($"Debug: Saved baronies image to '{debugPath}'");
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
            Console.WriteLine("Drawing cells by couloured groups to image...");
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
                    Console.WriteLine($"Drawing {wildernessCells.Count} wilderness cells in black");
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

                if (Settings.Instance.Debug)
                {
                    var numberedName = GetNumberedImageName(name);
                    var path = Helper.GetPath(Settings.OutputDirectory, GetDebugFolderName(), numberedName);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    Console.WriteLine($"Debug: Saving {name} image to '{path}'");
                    await cellsMap.WriteAsync(path);
                    Console.WriteLine($"Debug: {name} image saved to '{path}'");
                    RegisterGeneratedImage(path);
                }

            }
            catch (Exception ex)
            {
                Debugger.Break();
                throw;
            }


        }

        private static Drawables GenerateCellPolygons(IEnumerable<Entities.Cell> cells, MagickColor color, Entities.Map map)
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