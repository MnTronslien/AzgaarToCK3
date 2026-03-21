using System.Drawing;
using ImageMagick;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Manually draws river pixels one by one for exact control.
/// No line drawing - we set each pixel individually.
/// </summary>
public static class RiverPixelDrawer
{
    /// <summary>
    /// Manually set a single pixel to a specific color.
    /// </summary>
    public static void SetPixel(MagickImage image, int x, int y, MagickColor color)
    {
        // Use pixel collection for direct pixel manipulation
        using var pixels = image.GetPixels();
        var pixel = pixels.GetPixel(x, y);

        if (pixel != null)
        {
            pixel.SetChannel(0, color.R); // Red
            pixel.SetChannel(1, color.G); // Green
            pixel.SetChannel(2, color.B); // Blue
        }
    }

    /// <summary>
    /// Draw a river path by setting each pixel individually.
    /// Returns the actual pixels that were drawn (for validation).
    /// </summary>
    public static List<Point> DrawRiverPath(MagickImage image, List<Point> path, MagickColor riverColor)
    {
        if (path.Count == 0)
            return new List<Point>();

        var drawnPixels = new List<Point>();

        using (var pixels = image.GetPixelsUnsafe())
        {
            foreach (var point in path)
            {
                // Bounds check
                if (point.X >= 0 && point.X < image.Width &&
                    point.Y >= 0 && point.Y < image.Height)
                {
                    var pixel = pixels[point.X, point.Y];
                    pixel?.SetChannel(0, riverColor.R);
                    pixel?.SetChannel(1, riverColor.G);
                    pixel?.SetChannel(2, riverColor.B);

                    drawnPixels.Add(point);
                }
            }
        }

        return drawnPixels;
    }

    private static bool ColorsMatch(IMagickColor<byte> a, IMagickColor<byte> b)
    {
        return a.R == b.R && a.G == b.G && a.B == b.B;
    }
}
