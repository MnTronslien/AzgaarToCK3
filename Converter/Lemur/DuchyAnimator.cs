using ImageMagick;
using Converter.Lemur.Entities;
using Converter.Lemur.Graphs;

namespace Converter.Lemur
{
    /// <summary>
    /// Captures the two territory-synthesis steps for a single duchy and renders them as
    /// animated GIFs: (1) barony growth (multi-source Voronoi cell claiming) and
    /// (2) county formation (graph partitioning of baronies). Active only when
    /// <see cref="Settings.AnimateDuchyId"/> is set via the <c>--animate-duchy</c> flag;
    /// in that mode the pipeline runs just far enough to form the duchy's counties, then
    /// <see cref="RenderAsync"/> writes the GIFs to the debug folder and the run exits.
    ///
    /// This is a debug/illustration tool — it does not affect normal conversions.
    /// </summary>
    public static class DuchyAnimator
    {
        // Cumulative full-state snapshots (cellId -> fill colour), one per algorithm step.
        private static readonly List<Dictionary<int, MagickColor>> _growthFrames = new();
        private static readonly List<Dictionary<int, MagickColor>> _countyFrames = new();
        private static Dictionary<Barony, MagickColor>? _baronyPalette;

        private static readonly MagickColor Unassigned = new MagickColor("#C8C8C8"); // light grey
        private const int TargetWidth = 820; // upscale small duchy crops to at least this width

        /// <summary>True when the given duchy is the one selected for animation.</summary>
        public static bool IsTarget(Duchy duchy) =>
            Settings.Instance.AnimateDuchyId.HasValue && Settings.Instance.AnimateDuchyId.Value == duchy.Id;

        // ----------------------------------------------------------------- capture

        /// <summary>
        /// Snapshot the current barony cell assignment as one growth frame.
        /// Call after seeding and after every Voronoi round.
        /// </summary>
        public static void CaptureGrowthFrame(IReadOnlyList<Barony> baronies)
        {
            EnsurePalette(baronies);
            var frame = new Dictionary<int, MagickColor>();
            foreach (var b in baronies)
            {
                var colour = _baronyPalette![b];
                foreach (var c in b.Cells)
                    frame[c.Id] = colour;
            }
            _growthFrames.Add(frame);
        }

        /// <summary>
        /// Snapshot the current partition state as one county frame. Each partition (county in
        /// progress) gets a distinct colour; baronies not yet in any partition stay grey.
        /// </summary>
        public static void CaptureCountyFrame(IReadOnlyList<Graph> partitions, Dictionary<Node, Barony> nodeToBarony)
        {
            var frame = new Dictionary<int, MagickColor>();
            for (int i = 0; i < partitions.Count; i++)
            {
                var colour = CountyColour(i);
                foreach (var node in partitions[i].GetNodes())
                {
                    if (!nodeToBarony.TryGetValue(node, out var barony)) continue;
                    foreach (var c in barony.Cells)
                        frame[c.Id] = colour;
                }
            }
            _countyFrames.Add(frame);
        }

        private static void EnsurePalette(IReadOnlyList<Barony> baronies)
        {
            if (_baronyPalette != null) return;
            _baronyPalette = new Dictionary<Barony, MagickColor>();
            for (int i = 0; i < baronies.Count; i++)
                _baronyPalette[baronies[i]] = DistinctColour(i, Math.Max(1, baronies.Count), 0.62, 0.92);
        }

        // ----------------------------------------------------------------- render

        public static async Task RenderAsync(Map map)
        {
            var id = Settings.Instance.AnimateDuchyId;
            if (id == null) return;

            var duchy = map.Duchies?.FirstOrDefault(d => d.Id == id.Value);
            if (duchy == null)
            {
                Logger.Warning($"--animate-duchy {id}: no duchy with that id was generated. Nothing to animate.");
                return;
            }

            var cells = duchy.GetAllCells();
            if (cells.Count == 0)
            {
                Logger.Warning($"--animate-duchy {id}: duchy '{duchy.Name}' has no cells. Nothing to animate.");
                return;
            }

            var coords = map.JsonMap.mapCoordinates;
            var bbox = ComputeBounds(cells, coords);
            var idTag = $"d{duchy.Id}";

            var growthPath = await WriteGifAsync(
                _growthFrames, duchy, bbox, coords,
                title: $"Barony growth — {duchy.Name} ({idTag})",
                legend: new[]
                {
                    "Each colour = one barony  •  dot = town (burg)",
                    "Cells claimed outward from each town, nearest first",
                },
                drawBurgDots: true,
                frameDelayCs: 12,
                fileName: $"anim_1_barony_growth_{idTag}.gif");

            var countyPath = await WriteGifAsync(
                _countyFrames, duchy, bbox, coords,
                title: $"County formation — {duchy.Name} ({idTag})",
                legend: new[]
                {
                    "Each colour = one county",
                    "Baronies grouped to balance population",
                },
                drawBurgDots: false,
                frameDelayCs: 80,
                fileName: $"anim_2_county_formation_{idTag}.gif");

            Logger.Section("Duchy process animation complete");
            if (growthPath != null) Logger.Info($"  Barony growth GIF:   {growthPath}");
            if (countyPath != null) Logger.Info($"  County formation GIF: {countyPath}");
        }

        private static async Task<string?> WriteGifAsync(
            List<Dictionary<int, MagickColor>> frames,
            Duchy duchy,
            Bounds bbox,
            Deserialization.AzgaarMapCoordinates coords,
            string title,
            string[] legend,
            bool drawBurgDots,
            int frameDelayCs,
            string fileName)
        {
            if (frames.Count == 0)
            {
                Logger.Warning($"DuchyAnimator: no frames captured for '{fileName}', skipping.");
                return null;
            }

            var allCells = duchy.GetAllCells();
            var crop = bbox.ToGeometry();

            using var collection = new MagickImageCollection();

            for (int fi = 0; fi < frames.Count; fi++)
            {
                var frame = frames[fi];

                // Group cells by fill colour for this frame (grey = not yet assigned).
                var byColour = new Dictionary<MagickColor, List<Cell>>();
                foreach (var c in allCells)
                {
                    var colour = frame.TryGetValue(c.Id, out var col) ? col : Unassigned;
                    if (!byColour.TryGetValue(colour, out var list))
                        byColour[colour] = list = new List<Cell>();
                    list.Add(c);
                }

                // Neutral light-grey canvas: any gaps (e.g. cells turned into wasteland, so excluded
                // from every county) read as intentional background rather than a stark white hole,
                // while staying distinct from the slightly darker "unassigned" cell grey.
                var readSettings = new MagickReadSettings { Width = Entities.Map.MapWidth, Height = Entities.Map.MapHeight };
                var img = new MagickImage("xc:#E8E8E8", readSettings);

                var drawablesList = new List<Drawables>();
                foreach (var group in byColour)
                    drawablesList.Add(ImageUtility.GenerateCellPolygons(group.Value, group.Key, coords));
                img.Draw(drawablesList.SelectMany(d => d));

                if (drawBurgDots)
                {
                    var dots = new Drawables();
                    foreach (var barony in duchy.Baronies)
                    {
                        var cell = barony.burg?.Cell;
                        if (cell == null) continue;
                        var p = Centroid(cell, coords);
                        dots.StrokeColor(MagickColors.Black).StrokeWidth(2)
                            .FillColor(MagickColors.White)
                            .Circle(p.X, p.Y, p.X + 5, p.Y);
                    }
                    img.Draw(dots);
                }

                // Crop to the duchy, then reset the virtual canvas so every frame shares the
                // same origin/size (otherwise Coalesce re-expands to the full map with an offset).
                img.Crop(crop);
                img.Page = new MagickGeometry((int)img.Width, (int)img.Height);

                // Upscale small duchies (nearest-neighbour keeps crisp cell edges) so the caption
                // text has room and the result is legible at a usable size.
                if (img.Width < TargetWidth)
                {
                    img.FilterType = FilterType.Point;
                    img.Resize(new MagickGeometry($"{TargetWidth}x"));
                    img.Page = new MagickGeometry((int)img.Width, (int)img.Height);
                }

                DrawCaption(img, title, legend, fi + 1, frames.Count);

                img.AnimationDelay = frameDelayCs;
                collection.Add(img);
            }

            // Hold the final frame so the finished result is readable before the loop restarts.
            collection[collection.Count - 1].AnimationDelay = 400;
            collection[0].AnimationIterations = 0; // loop forever

            collection.Coalesce();

            var debugRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AzgaarToCK3", "debug");
            var path = Helper.GetPath(debugRoot, ImageUtility.GetDebugFolderName(), fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await collection.WriteAsync(path, MagickFormat.Gif);
            ImageUtility.RegisterGeneratedImage(path);
            Logger.Debug($"Wrote {frames.Count}-frame GIF to '{path}'");
            return path;
        }

        private static void DrawCaption(MagickImage img, string title, string[] legend, int step, int total)
        {
            int lineCount = 1 + legend.Length + 1; // title + legend + step counter
            double fontSize = Math.Clamp(img.Width / 28.0, 13, 30);
            double lineH = fontSize * 1.35;
            double barH = lineH * (lineCount + 0.6);

            var bar = new Drawables()
                .FillColor(new MagickColor("#000000C0"))
                .StrokeColor(MagickColors.Transparent)
                .Rectangle(0, 0, img.Width, barH);
            img.Draw(bar);

            var text = new Drawables()
                .Font("Arial")
                .FillColor(MagickColors.White)
                .StrokeColor(MagickColors.Transparent)
                .TextAlignment(TextAlignment.Left);

            double y = lineH;
            text.FontPointSize(fontSize * 1.12).Text(10, y, title);
            y += lineH;
            text.FontPointSize(fontSize * 0.86).FillColor(new MagickColor("#DDDDDD"));
            foreach (var line in legend)
            {
                text.Text(14, y, line);
                y += lineH;
            }
            text.FillColor(new MagickColor("#9FE0FF")).Text(14, y, $"step {step} / {total}");
            img.Draw(text);
        }

        private static PointD Centroid(Cell cell, Deserialization.AzgaarMapCoordinates coords)
        {
            double sx = 0, sy = 0;
            int n = cell.GeoDataCoordinates.Length;
            foreach (var pt in cell.GeoDataCoordinates)
            {
                var ip = Transform(pt[0], pt[1], coords);
                sx += ip.X; sy += ip.Y;
            }
            return new PointD(sx / n, sy / n);
        }

        // Matches the transform in ImageUtility.GenerateCellPolygons so the crop lines up with the fill.
        private static PointD Transform(float lon, float lat, Deserialization.AzgaarMapCoordinates coords)
        {
            float xOffset = coords.lonW, yOffset = coords.latS;
            float xRatio = Entities.Map.MapWidth / coords.lonT;
            float yRatio = Entities.Map.MapHeight / coords.latT;
            return new PointD((lon - xOffset) * xRatio, Entities.Map.MapHeight - (lat - yOffset) * yRatio);
        }

        private static Bounds ComputeBounds(List<Cell> cells, Deserialization.AzgaarMapCoordinates coords)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var c in cells)
                foreach (var pt in c.GeoDataCoordinates)
                {
                    var ip = Transform(pt[0], pt[1], coords);
                    if (ip.X < minX) minX = ip.X;
                    if (ip.Y < minY) minY = ip.Y;
                    if (ip.X > maxX) maxX = ip.X;
                    if (ip.Y > maxY) maxY = ip.Y;
                }

            // Pad ~6% of the larger dimension, clamped to the canvas.
            double pad = Math.Max(24, Math.Max(maxX - minX, maxY - minY) * 0.06);
            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(Entities.Map.MapWidth, maxX + pad);
            maxY = Math.Min(Entities.Map.MapHeight, maxY + pad);
            return new Bounds(minX, minY, maxX, maxY);
        }

        private readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
        {
            public MagickGeometry ToGeometry() =>
                new MagickGeometry((int)Math.Floor(MinX), (int)Math.Floor(MinY),
                                   (int)Math.Ceiling(MaxX - MinX), (int)Math.Ceiling(MaxY - MinY));
        }

        // ----------------------------------------------------------------- colours

        // Even hue spread for baronies (many, need to be distinguishable).
        private static MagickColor DistinctColour(int index, int count, double sat, double val)
        {
            double hue = (360.0 * index) / count;
            return FromHsv(hue, sat, val);
        }

        // Counties: golden-ratio hue walk so adjacent indices look very different even with few of them.
        private static MagickColor CountyColour(int index)
        {
            double hue = (index * 137.508) % 360.0;
            return FromHsv(hue, 0.70, 0.88);
        }

        private static MagickColor FromHsv(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            byte R = (byte)Math.Round((r + m) * 255);
            byte G = (byte)Math.Round((g + m) * 255);
            byte B = (byte)Math.Round((b + m) * 255);
            return new MagickColor($"#{R:X2}{G:X2}{B:X2}");
        }
    }
}
