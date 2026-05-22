using Converter.Lemur.Entities;
using ImageMagick;

namespace Converter.Lemur.Provinces;

/// <summary>
/// Debug visualisation: render every barony coloured by its assigned <see cref="Ck3Terrain"/>
/// with a legend, so the gameplay-terrain decisions are inspectable at a glance without
/// loading the mod in CK3. Mirrors the format of vanilla terrain-overview screenshots the
/// CK3 community publishes — standard per-terrain colour palette.
/// </summary>
public static class TerrainDebugImage
{
    // Standard CK3-community terrain palette. Tweak here if the reference convention shifts.
    public static readonly IReadOnlyDictionary<Ck3Terrain, MagickColor> Palette = new Dictionary<Ck3Terrain, MagickColor>
    {
        [Ck3Terrain.Desert]           = MagickColor.FromRgb(0xFF, 0xFF, 0x00),   // yellow
        [Ck3Terrain.DesertMountains]  = MagickColor.FromRgb(0x1A, 0x1A, 0x1A),   // near-black (distinct from sea blue)
        [Ck3Terrain.Drylands]         = MagickColor.FromRgb(0xFF, 0x14, 0x93),   // deep pink / magenta
        [Ck3Terrain.Farmlands]        = MagickColor.FromRgb(0xFF, 0x00, 0x00),   // red
        [Ck3Terrain.Floodplains]      = MagickColor.FromRgb(0x40, 0x40, 0xC0),   // blue-violet
        [Ck3Terrain.Forest]           = MagickColor.FromRgb(0x2E, 0xA0, 0x2E),   // medium green
        [Ck3Terrain.Hills]            = MagickColor.FromRgb(0x80, 0x00, 0x00),   // maroon
        [Ck3Terrain.Jungle]           = MagickColor.FromRgb(0x00, 0x64, 0x00),   // dark green
        [Ck3Terrain.Mountains]        = MagickColor.FromRgb(0x80, 0x80, 0x80),   // grey
        [Ck3Terrain.Oasis]            = MagickColor.FromRgb(0xD8, 0xA8, 0xE8),   // lavender
        [Ck3Terrain.Plains]           = MagickColor.FromRgb(0xD2, 0xB4, 0x8C),   // tan / beige
        [Ck3Terrain.Steppe]           = MagickColor.FromRgb(0xE8, 0x82, 0x1C),   // orange
        [Ck3Terrain.Taiga]            = MagickColor.FromRgb(0x80, 0xE0, 0x60),   // lime green
        [Ck3Terrain.Wetlands]         = MagickColor.FromRgb(0x40, 0xC0, 0xB0),   // teal
        [Ck3Terrain.TerracedHills]    = MagickColor.FromRgb(0xE0, 0x40, 0x40),   // coral
    };

    // Bright saturated blue — must read as clearly "ocean" against the near-black DesertMountains
    // and WastelandColor swatches. The CK3 in-game ocean blue (#446BA3) is too muted; at a glance
    // it merges with dark land. This is a debug image, not the production splatmap — readability beats fidelity.
    private static readonly MagickColor SeaColor       = MagickColor.FromRgb(0x30, 0x60, 0xE0);
    // Wastelands need a colour distinct from every entry in the Palette AND from SeaColor.
    // Near-black (the previous choice) was indistinguishable from DesertMountains. Off-white
    // is the only remaining hue space that doesn't collide with any terrain (Mountains is medium
    // grey; Oasis is lavender; Plains is tan). Reads as "uninhabited" in the same way road maps
    // shade unsettled regions pale.
    private static readonly MagickColor WastelandColor = MagickColor.FromRgb(0xE6, 0xE6, 0xE6);

    public static async Task Write(Map map)
    {
        if (!Settings.Instance.GenerateDebugImages) return;
        if (map.Baronies is null || map.Baronies.Count == 0) return;

        Logger.Info("Drawing terrain debug overview image...");

        var settings = new MagickReadSettings { Width = Map.MapWidth, Height = Map.MapHeight };
        // Use xc:#rrggbb pseudo-format the way ImageUtility does — matches existing convention.
        using var canvas = new MagickImage($"xc:#{SeaColor.R:X2}{SeaColor.G:X2}{SeaColor.B:X2}", settings);

        // Group baronies by terrain so each terrain is one batched Drawables payload.
        var byTerrain = map.Baronies
            .GroupBy(b => b.Ck3Terrain)
            .ToList();

        var drawablesList = new List<Drawables>();
        foreach (var group in byTerrain)
        {
            var color = Palette.TryGetValue(group.Key, out var c) ? c : MagickColors.Magenta;
            var cells = group.SelectMany(b => b.Cells);
            drawablesList.Add(ImageUtility.GenerateCellPolygons(cells, color, map));
        }

        // Wastelands in their own colour so they don't blend with sea or with terrain choices.
        if (map.Wastelands is { Count: > 0 })
        {
            var cells = map.Wastelands.SelectMany(w => w.Cells);
            drawablesList.Add(ImageUtility.GenerateCellPolygons(cells, WastelandColor, map));
        }

        // Repaint sea/major-river/far-sea polygons LAST so they reclaim any pixels that
        // coastal land-cell Voronoi polygons bled into. We don't care about distinguishing
        // individual sea bodies — every sea-side pixel just needs to read as "sea." Without
        // this step, coastal wasteland cells (whose Voronoi polygons extend into the water)
        // paint pale-grey "fingers" reaching into the ocean. Mirrors the pattern used by
        // ImageUtility.DrawProvincesImage for the production provinces.png.
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

        DrawLegend(canvas);

        canvas.HasAlpha = false;

        var debugRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AzgaarToCK3", "debug");
        var path = Helper.GetPath(debugRoot, GetDebugFolderName(), "9_terrain_overview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await canvas.WriteAsync(path);
        Logger.Info($"Saved terrain overview to '{path}'");
        ImageUtility.RegisterGeneratedImage(path);
    }

    // Legend layout — sized for the full 8192×4096 canvas so it is readable when the image
    // is opened at any reasonable zoom. Two columns to mirror the reference screenshot.
    // Sentinel — wastelands aren't a CK3 terrain enum value but get their own legend row
    // so the viewer knows what the pale-grey swatch on the map means.
    private const Ck3Terrain WastelandSentinel = (Ck3Terrain)999;

    private static void DrawLegend(MagickImage canvas)
    {
        var entries = Palette.Keys
            .OrderBy(k => k.ToCk3String())
            .Append(WastelandSentinel)                     // appears last in the legend
            .ToList();

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

        for (int i = 0; i < entries.Count; i++)
        {
            int col = i / rowsPerColumn;
            int row = i % rowsPerColumn;

            int rowX = x0 + panelPad + col * (colWidth + colGap);
            int rowY = y0 + panelPad + titleH + titlePad + row * (swatchH + rowGap);

            var terrain = entries[i];
            var swatchColor = terrain == WastelandSentinel
                ? WastelandColor
                : Palette[terrain];

            d = d.FillColor(swatchColor)
                 .StrokeColor(MagickColors.Black)
                 .StrokeWidth(2)
                 .Rectangle(rowX, rowY, rowX + swatchW, rowY + swatchH);

            d = d.FillColor(MagickColors.Black)
                 .StrokeColor(MagickColors.Black)
                 .StrokeWidth(0)
                 .Text(rowX + swatchW + textPad, rowY + swatchH - 14, FormatLabel(terrain));
        }

        canvas.Draw(d);
    }

    private static string FormatLabel(Ck3Terrain t) => t switch
    {
        Ck3Terrain.DesertMountains => "Desert Mountains",
        Ck3Terrain.TerracedHills   => "Terraced Hills",
        WastelandSentinel          => "Wasteland",
        _                           => Char.ToUpper(t.ToCk3String()[0]) + t.ToCk3String().Substring(1),
    };

    // Local copy of ImageUtility.GetDebugFolderName logic (that method is private; we keep
    // this writer in Provinces/ to keep the namespace boundary clean rather than promote
    // the helper). Mirrors the same map-name + timestamp pattern so artifacts cluster in
    // the same debug folder as other run outputs.
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
