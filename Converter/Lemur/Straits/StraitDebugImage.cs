using Converter.Lemur.Entities;
using ImageMagick;

namespace Converter.Lemur.Straits;

/// <summary>
/// Tuning aid (PLAN_straits.md): land = white, ocean = blue, lake = pale blue, river = teal,
/// each accepted strait = a red line between the two cell centroids.
///
/// Rendered by scanline-filling each cell polygon into a quarter-resolution pixel buffer rather
/// than drawing 50k+ Magick vector polygons (which costs ~25s on a large map). Work is bounded by
/// the covered pixel area — empty canvas margins outside the Azgaar map cost nothing and stay white.
/// </summary>
public static class StraitDebugImage
{
    private const int Downscale = 4; // 8192×4096 → 2048×1024

    private static readonly byte[] Land  = [0xff, 0xff, 0xff]; // background — never explicitly filled
    private static readonly byte[] Ocean = [0x3a, 0x6e, 0xa5];
    private static readonly byte[] Lake  = [0xbc, 0xd4, 0xe6];
    private static readonly byte[] River = [0x1f, 0x6f, 0x6f];

    public static void Write(
        IReadOnlyDictionary<int, Cell> cells,
        Func<GeoPoint, ImagePixel> project,
        IReadOnlyList<Strait> straits,
        int oceanMinimumArea,
        string outputPath)
    {
        var cls = StraitGenerator.Classify(cells, oceanMinimumArea);

        int w = Map.MapWidth / Downscale, h = Map.MapHeight / Downscale;
        var rgb = new byte[w * h * 3];
        Array.Fill(rgb, (byte)0xff); // white background = land + out-of-map margin

        var poly = new List<(double x, double y)>();
        foreach (var (id, c) in cells)
        {
            var color = cls.GetValueOrDefault(id, StraitGenerator.CellClass.Land) switch
            {
                StraitGenerator.CellClass.Ocean => Ocean,
                StraitGenerator.CellClass.Lake => Lake,
                StraitGenerator.CellClass.River => River,
                _ => (byte[]?)null, // Land → leave background
            };
            if (color == null) continue;

            poly.Clear();
            foreach (var v in c.GeoDataCoordinates)
            {
                var p = project(new GeoPoint(v[0], v[1]));
                poly.Add((p.X / Downscale, p.Y / Downscale));
            }
            FillPolygon(rgb, w, h, poly, color);
        }

        var prs = new PixelReadSettings(w, h, StorageType.Char, PixelMapping.RGB);
        using var img = new MagickImage(rgb, prs);

        if (straits.Count > 0)
        {
            var lines = new Drawables().StrokeColor(new MagickColor("#e00000")).StrokeWidth(2).FillOpacity(new Percentage(0));
            foreach (var s in straits)
            {
                var pa = project(StraitGenerator.CentroidGeo(s.FromCell));
                var pb = project(StraitGenerator.CentroidGeo(s.ToCell));
                lines.Line(pa.X / Downscale, pa.Y / Downscale, pb.X / Downscale, pb.Y / Downscale);
            }
            img.Draw(lines);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        img.Write(outputPath);
        Logger.Info($"Wrote strait debug image: {outputPath} ({straits.Count} straits, {w}×{h})");
    }

    /// <summary>Even-odd scanline fill of a (convex Voronoi) polygon into the RGB buffer.</summary>
    private static void FillPolygon(byte[] rgb, int w, int h, List<(double x, double y)> pts, byte[] color)
    {
        int n = pts.Count;
        if (n < 3) return;

        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (var p in pts) { if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y; }
        int y0 = Math.Max(0, (int)Math.Floor(minY));
        int y1 = Math.Min(h - 1, (int)Math.Ceiling(maxY));

        var xs = new List<double>(8);
        for (int y = y0; y <= y1; y++)
        {
            double yc = y + 0.5;
            xs.Clear();
            for (int i = 0; i < n; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % n];
                if ((a.y <= yc && b.y > yc) || (b.y <= yc && a.y > yc))
                    xs.Add(a.x + (yc - a.y) / (b.y - a.y) * (b.x - a.x));
            }
            if (xs.Count < 2) continue;
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int xa = Math.Max(0, (int)Math.Ceiling(xs[k] - 0.5));
                int xb = Math.Min(w - 1, (int)Math.Floor(xs[k + 1] - 0.5));
                int idx = (y * w + xa) * 3;
                for (int x = xa; x <= xb; x++, idx += 3)
                {
                    rgb[idx] = color[0];
                    rgb[idx + 1] = color[1];
                    rgb[idx + 2] = color[2];
                }
            }
        }
    }
}
