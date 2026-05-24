using Converter.Lemur.Entities;
using ImageMagick;

namespace Converter.Lemur.Provinces;

// Debug visualisation: each barony filled with its assigned Ck3Terrain colour, river network
// overlaid, province outlines drawn from provinces.png. Gated on Settings.GenerateDebugImages.
// Colour palette follows the CK3 community convention used in published terrain-overview maps.
public static class TerrainDebugImage
{
    public static readonly IReadOnlyDictionary<Ck3Terrain, MagickColor> Palette = new Dictionary<Ck3Terrain, MagickColor>
    {
        [Ck3Terrain.Desert]           = MagickColor.FromRgb(0xFF, 0xFF, 0x00),
        [Ck3Terrain.DesertMountains]  = MagickColor.FromRgb(0x1A, 0x1A, 0x1A),
        [Ck3Terrain.Drylands]         = MagickColor.FromRgb(0xFF, 0x14, 0x93),
        [Ck3Terrain.Farmlands]        = MagickColor.FromRgb(0xFF, 0x00, 0x00),
        [Ck3Terrain.Floodplains]      = MagickColor.FromRgb(0x40, 0x40, 0xC0),
        [Ck3Terrain.Forest]           = MagickColor.FromRgb(0x2E, 0xA0, 0x2E),
        [Ck3Terrain.Hills]            = MagickColor.FromRgb(0x80, 0x00, 0x00),
        [Ck3Terrain.Jungle]           = MagickColor.FromRgb(0x00, 0x64, 0x00),
        [Ck3Terrain.Mountains]        = MagickColor.FromRgb(0x80, 0x80, 0x80),
        [Ck3Terrain.Oasis]            = MagickColor.FromRgb(0xD8, 0xA8, 0xE8),
        [Ck3Terrain.Plains]           = MagickColor.FromRgb(0xD2, 0xB4, 0x8C),
        [Ck3Terrain.Steppe]           = MagickColor.FromRgb(0xE8, 0x82, 0x1C),
        [Ck3Terrain.Taiga]            = MagickColor.FromRgb(0x80, 0xE0, 0x60),
        [Ck3Terrain.Wetlands]         = MagickColor.FromRgb(0x40, 0xC0, 0xB0),
        [Ck3Terrain.TerracedHills]    = MagickColor.FromRgb(0xE0, 0x40, 0x40),
    };

    private static readonly MagickColor SeaColor       = MagickColor.FromRgb(0x30, 0x60, 0xE0);
    private static readonly MagickColor RiverColor     = MagickColor.FromRgb(0x0C, 0x20, 0x70);
    // Off-white — distinct from every Palette entry and from SeaColor at a glance.
    private static readonly MagickColor WastelandColor = MagickColor.FromRgb(0xE6, 0xE6, 0xE6);

    public static async Task Write(Map map)
    {
        if (!Settings.Instance.GenerateDebugImages) return;
        if (map.Baronies is null || map.Baronies.Count == 0) return;

        Logger.Info("Drawing terrain debug overview image...");
        using var canvas = Render(map);

        var debugRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AzgaarToCK3", "debug");
        var path = Helper.GetPath(debugRoot, GetDebugFolderName(), "9_terrain_overview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await canvas.WriteAsync(path);
        Logger.Info($"Saved terrain overview to '{path}'");
        ImageUtility.RegisterGeneratedImage(path);
    }

    // Synchronous — builds the rendered canvas in memory. All the CPU-heavy work lives here
    // so the public Write method's `async` is genuine (only the final disk write awaits).
    private static MagickImage Render(Map map)
    {
        var settings = new MagickReadSettings { Width = Map.MapWidth, Height = Map.MapHeight };
        var canvas = new MagickImage($"xc:#{SeaColor.R:X2}{SeaColor.G:X2}{SeaColor.B:X2}", settings);

        var byTerrain = map.Baronies!
            .GroupBy(b => b.Ck3Terrain)
            .ToList();

        var drawablesList = new List<Drawables>();
        foreach (var group in byTerrain)
        {
            var color = Palette.TryGetValue(group.Key, out var c) ? c : MagickColors.Magenta;
            var cells = group.SelectMany(b => b.Cells);
            drawablesList.Add(ImageUtility.GenerateCellPolygons(cells, color, map));
        }

        if (map.Wastelands is { Count: > 0 })
        {
            var cells = map.Wastelands.SelectMany(w => w.Cells);
            drawablesList.Add(ImageUtility.GenerateCellPolygons(cells, WastelandColor, map));
        }

        // Repaint sea bodies last so they reclaim pixels that coastal land-cell Voronoi
        // polygons bled into — otherwise wasteland "fingers" leak into the ocean.
        if (map.SeaZones is { Count: > 0 })
        {
            var cells = map.SeaZones.SelectMany(z => z.Cells);
            drawablesList.Add(ImageUtility.GenerateCellPolygons(cells, SeaColor, map));
        }
        if (map.FarSeaZones is { Count: > 0 })
        {
            var cells = map.FarSeaZones.SelectMany(z => z.Cells);
            drawablesList.Add(ImageUtility.GenerateCellPolygons(cells, SeaColor, map));
        }
        if (map.MajorRiverProvinces is { Count: > 0 })
        {
            var cells = map.MajorRiverProvinces.SelectMany(r => r.Cells);
            drawablesList.Add(ImageUtility.GenerateCellPolygons(cells, SeaColor, map));
        }

        canvas.Draw(drawablesList.SelectMany(d => d));

        // Outlines first so the rivers and legend draw on top of them.
        DrawProvinceOutlines(canvas);
        DrawRivers(canvas, map);
        DrawLegend(canvas);

        canvas.HasAlpha = false;
        return canvas;
    }

    // Edge-detect provinces.png (each province has a unique RGB), then composite the
    // resulting mask via Multiply so boundary pixels go black on the canvas. Radius 3 ⇒
    // ~5-px lines, visible after the ~13x downscale typical for thumbnail preview.
    private static void DrawProvinceOutlines(MagickImage canvas)
    {
        var provincesPath = Helper.GetPath(Settings.OutputDirectory, "map_data", "provinces.png");
        if (!File.Exists(provincesPath))
        {
            Logger.Verbose($"TerrainDebugImage — provinces.png not found at '{provincesPath}', skipping outlines");
            return;
        }

        using var outline = new MagickImage(provincesPath);
        outline.Edge(3);
        outline.ColorSpace = ColorSpace.Gray;
        outline.Threshold(new Percentage(2));
        outline.Negate();
        canvas.Composite(outline, CompositeOperator.Multiply);
    }

    private static void DrawRivers(MagickImage canvas, Map map)
    {
        if (map.Rivers is null || map.Rivers.Count == 0) return;

        var coords = map.JsonMap.mapCoordinates;
        float xOffset = coords.lonW;
        float yOffset = coords.latS;
        float xRatio  = Map.MapWidth  / coords.lonT;
        float yRatio  = Map.MapHeight / coords.latT;

        var d = new Drawables()
            .DisableStrokeAntialias()
            .FillColor(MagickColors.None)
            .StrokeColor(RiverColor);

        int drawn = 0;
        foreach (var river in map.Rivers)
        {
            if (river.ControlPoints is null || river.ControlPoints.Count < 2) continue;

            // Scaled and clamped so trunks dominate tributaries without drowning the map.
            float strokeWidth = Math.Clamp(river.Width * 0.9f, 5f, 24f);

            var points = river.ControlPoints.Select(cp => new PointD(
                (cp[0] - xOffset) * xRatio,
                Map.MapHeight - (cp[1] - yOffset) * yRatio));

            d = d.StrokeWidth(strokeWidth).Polyline(points);
            drawn++;
        }

        if (drawn > 0)
        {
            canvas.Draw(d);
            Logger.Info($"TerrainDebugImage — drew {drawn} river polyline(s)");
        }
    }

    private sealed record LegendEntry(string Label, MagickColor Color);

    private static readonly IReadOnlyList<LegendEntry> LegendEntries =
        Palette
            .OrderBy(kv => kv.Key.ToCk3String())
            .Select(kv => new LegendEntry(FormatLabel(kv.Key), kv.Value))
            .Append(new LegendEntry("Wasteland", WastelandColor))
            .ToList();

    private static void DrawLegend(MagickImage canvas)
    {
        const int rowsPerColumn = 8;                       // 16 entries → 8 + 8
        const int columns       = 2;
        const int swatchW       = 90;
        const int swatchH       = 60;
        const int rowGap        = 24;
        const int colGap        = 60;
        const int textPad       = 18;
        const int textWidth     = 360;                     // room for "Desert Mountains" at 40pt
        const int titleH        = 90;
        const int titlePad      = 30;
        const int panelPad      = 50;

        int colWidth   = swatchW + textPad + textWidth;
        int legendW    = columns * colWidth + (columns - 1) * colGap + 2 * panelPad;
        int legendH    = titleH + titlePad + rowsPerColumn * (swatchH + rowGap) - rowGap + 2 * panelPad;

        int x0 = Map.MapWidth  - legendW - 80;
        int y0 = Map.MapHeight - legendH - 80;

        var d = new Drawables()
            .DisableStrokeAntialias()
            .FillColor(MagickColor.FromRgb(0xF0, 0xF0, 0xF0))
            .StrokeColor(MagickColor.FromRgb(0x20, 0x20, 0x20))
            .StrokeWidth(4)
            .Rectangle(x0, y0, x0 + legendW, y0 + legendH);

        d = d.FillColor(MagickColors.Black)
             .StrokeColor(MagickColors.Black)
             .StrokeWidth(0)
             .Font("DejaVu-Sans")
             .FontPointSize(56)
             .TextAlignment(TextAlignment.Center)
             .Text(x0 + legendW / 2, y0 + panelPad + 60, "TERRAIN");

        d = d.FontPointSize(40).TextAlignment(TextAlignment.Left);

        for (int i = 0; i < LegendEntries.Count; i++)
        {
            int col = i / rowsPerColumn;
            int row = i % rowsPerColumn;

            int rowX = x0 + panelPad + col * (colWidth + colGap);
            int rowY = y0 + panelPad + titleH + titlePad + row * (swatchH + rowGap);

            var entry = LegendEntries[i];

            d = d.FillColor(entry.Color)
                 .StrokeColor(MagickColors.Black)
                 .StrokeWidth(2)
                 .Rectangle(rowX, rowY, rowX + swatchW, rowY + swatchH);

            d = d.FillColor(MagickColors.Black)
                 .StrokeColor(MagickColors.Black)
                 .StrokeWidth(0)
                 .Text(rowX + swatchW + textPad, rowY + swatchH - 14, entry.Label);
        }

        canvas.Draw(d);
    }

    private static string FormatLabel(Ck3Terrain t) => t switch
    {
        Ck3Terrain.DesertMountains => "Desert Mountains",
        Ck3Terrain.TerracedHills   => "Terraced Hills",
        _                           => Char.ToUpper(t.ToCk3String()[0]) + t.ToCk3String().Substring(1),
    };

    // Mirrors the private folder-naming logic in ImageUtility so all run artefacts cluster
    // under the same map-name + timestamp directory.
    private static string? _debugFolderName;
    private static string GetDebugFolderName()
    {
        if (_debugFolderName != null) return _debugFolderName;
        var mapName = Path.GetFileNameWithoutExtension(Settings.Instance.InputJsonPath);
        var timestamp = DateTime.Now.ToString("yyyy.MM.dd_HH.mm");
        _debugFolderName = $"{mapName}_{timestamp}";
        return _debugFolderName;
    }
}
