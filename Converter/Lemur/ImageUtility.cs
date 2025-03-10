using System.Diagnostics;
using Converter.Lemur.Entities;
using ImageMagick;

namespace Converter.Lemur
{
    public static class ImageUtility
    {
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
                string path = Converter.Helper.GetPath($"{Environment.CurrentDirectory}/cells.png");
                await cellsMap.WriteAsync(path);

                if (Settings.Instance.Debug)
                {
                    Console.WriteLine("Debug is on, opening the image...");
                    OpenImageInExplorer(path);
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
                var settings = new MagickReadSettings()
                {
                    Width = Map.MapWidth,
                    Height = Map.MapHeight,
                };
                using var cellsMap = new MagickImage("xc:transparent", settings);

                List<Drawables> drawablesList = new();
                //=================
                // EXTEND THIS TO DRAW ALL PROVINCEs, not just baronies. Should also draw inn wasteland, major rivers and sea zones
                //=================
                //concat the list from baronies and in the future major rivers and sea zones
                List<IProvince> provincesToDraw = map.Baronies!.Cast<IProvince>().ToList();

                //Add wasteland provinces to the list as black
                


                foreach (var province in provincesToDraw)
                {
                    drawablesList.Add(GenerateCellPolygons(province.Cells, province.Color, map));
                }

                // Flatten the list of Drawables into a single collection of IDrawable
                IEnumerable<IDrawable> drawables = drawablesList.SelectMany(d => d);

                cellsMap.Draw(drawables);
                var path = Helper.GetPath(Settings.OutputDirectory, "map_data", "provinces.png");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                Console.WriteLine($"Saving provinces image to '{path}'");
                await cellsMap.WriteAsync(path);
                Console.WriteLine($"Provinces image has been drawn and saved to '{path}'");

               if (Settings.Instance.Debug)
                {
                    Console.WriteLine("Debug is on, opening the image...");
                    OpenImageInExplorer(path);
                }

            }
            catch (Exception ex)
            {
                Debugger.Break();
                throw;
            }
        }

        public static async Task DrawCellsWithColourImage(Dictionary<MagickColor, List<Entities.Cell>> colorCellsMap, Entities.Map map, string name = "colorCellsMap", System.Drawing.Color background = default)
        {
            Console.WriteLine("Drawing cells by couloured groups to image...");   
            try
            {
                var settings = new MagickReadSettings()
                {
                    Width = Map.MapWidth,
                    Height = Map.MapHeight,
                };
                using var cellsMap = new MagickImage($"xc:{background.Name.ToLower()}", settings);

                List<Drawables> drawablesList = new();

                foreach (var group in colorCellsMap)
                {
                    //drawablesList.Add(GenerateCellPolygons(province.Cells, province.Color, map));
                    drawablesList.Add(GenerateCellPolygons(group.Value, group.Key, map));
                }

                // Flatten the list of Drawables into a single collection of IDrawable
                IEnumerable<IDrawable> drawables = drawablesList.SelectMany(d => d);

                cellsMap.Draw(drawables);
                var path = Helper.GetPath(Settings.OutputDirectory, "map_data", $"{name}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                Console.WriteLine($"Saving image to '{path}'");
                await cellsMap.WriteAsync(path);
                Console.WriteLine($"Image has been drawn and saved to '{path}'");

               if (Settings.Instance.Debug)
                {
                    Console.WriteLine($"Debug is on, opening {name}.png");
                    OpenImageInExplorer(path);
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