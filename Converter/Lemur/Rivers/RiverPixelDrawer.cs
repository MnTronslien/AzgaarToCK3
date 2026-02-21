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

    /// <summary>
    /// Read back actual blue river pixels from a region of the image.
    /// This lets us validate what was ACTUALLY drawn, not what we intended to draw.
    /// </summary>
    public static List<Point> ReadBluePixelsInRegion(
        MagickImage image,
        int minX, int minY,
        int maxX, int maxY,
        MagickColor riverColor)
    {
        var bluePixels = new List<Point>();

        // Add padding to region
        int padding = 5;
        minX = Math.Max(0, minX - padding);
        minY = Math.Max(0, minY - padding);
        maxX = Math.Min((int)image.Width - 1, maxX + padding);
        maxY = Math.Min((int)image.Height - 1, maxY + padding);

        using (var pixels = image.GetPixelsUnsafe())
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var pixel = pixels[x, y];
                    if (pixel != null)
                    {
                        var color = pixel.ToColor();
                        if (color != null && ColorsMatch(color, riverColor))
                        {
                            bluePixels.Add(new Point(x, y));
                        }
                    }
                }
            }
        }

        return bluePixels;
    }

    /// <summary>
    /// Read back ALL blue river pixels from the entire image.
    /// Slower but comprehensive - use for final validation.
    /// </summary>
    public static List<Point> ReadAllBluePixels(MagickImage image, MagickColor riverColor)
    {
        var bluePixels = new List<Point>();

        using (var pixels = image.GetPixelsUnsafe())
        {
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var pixel = pixels[x, y];
                    if (pixel != null)
                    {
                        var color = pixel.ToColor();
                        if (color != null && ColorsMatch(color, riverColor))
                        {
                            bluePixels.Add(new Point(x, y));
                        }
                    }
                }
            }
        }

        return bluePixels;
    }

    private static bool ColorsMatch(IMagickColor<byte> a, IMagickColor<byte> b)
    {
        return a.R == b.R && a.G == b.G && a.B == b.B;
    }
}
