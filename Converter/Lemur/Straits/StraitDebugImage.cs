using Converter.Lemur.Entities;
using ImageMagick;

namespace Converter.Lemur.Straits;

/// <summary>
/// Tuning aid (PLAN_straits.md): land = white, ocean = blue, lake = pale blue, river = teal,
/// each accepted strait = a red line between the two cell centroids. Drawn straight from the
/// strait list via a caller-supplied geo→pixel projection, independent of the CSV writer.
/// </summary>
public static class StraitDebugImage
{
    private static readonly MagickColor Land = new("#ffffff");
    private static readonly MagickColor Ocean = new("#3a6ea5");
    private static readonly MagickColor Lake = new("#bcd4e6");
    private static readonly MagickColor River = new("#1f6f6f");
    private static readonly MagickColor StraitLine = new("#e00000");

    public static void Write(
        IReadOnlyDictionary<int, Cell> cells,
        Func<GeoPoint, ImagePixel> project,
        IReadOnlyList<Strait> straits,
        int oceanMinimumArea,
        string outputPath)
    {
        var cls = StraitGenerator.Classify(cells, oceanMinimumArea);

        var settings = new MagickReadSettings { Width = Map.MapWidth, Height = Map.MapHeight };
        using var img = new MagickImage("xc:white", settings);

        var fill = new Drawables().DisableStrokeAntialias();
        foreach (var (id, c) in cells)
        {
            var color = cls[id] switch
            {
                StraitGenerator.CellClass.Ocean => Ocean,
                StraitGenerator.CellClass.Lake => Lake,
                StraitGenerator.CellClass.River => River,
                _ => Land,
            };
            fill.FillColor(color).StrokeColor(color)
                .Polygon(c.GeoDataCoordinates.Select(n => project(new GeoPoint(n[0], n[1])).ToMagickPoint()));
        }
        img.Draw(fill);

        if (straits.Count > 0)
        {
            var lines = new Drawables().StrokeColor(StraitLine).StrokeWidth(4).FillOpacity(new Percentage(0));
            foreach (var s in straits)
            {
                var pa = project(StraitGenerator.CentroidGeo(s.FromCell)).ToMagickPoint();
                var pb = project(StraitGenerator.CentroidGeo(s.ToCell)).ToMagickPoint();
                lines.Line(pa.X, pa.Y, pb.X, pb.Y);
            }
            img.Draw(lines);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        img.Write(outputPath);
        Logger.Info($"Wrote strait debug image: {outputPath} ({straits.Count} straits)");
    }
}
