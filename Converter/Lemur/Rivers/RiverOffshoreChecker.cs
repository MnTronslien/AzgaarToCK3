using System.Drawing;
using ImageMagick;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Checks and trims rivers that run too far offshore into the ocean.
/// Based on CK3 rules: rivers can extend a limited distance into ocean (typically 3 pixels).
/// </summary>
public static class RiverOffshoreChecker
{
    private const int DEFAULT_MAX_OFFSHORE_DISTANCE = 3;

    /// <summary>
    /// Trim river path to remove excessive offshore runoff.
    /// Checks from the mouth backwards and stops when we hit land or max offshore distance.
    /// </summary>
    public static List<PointD> TrimOffshoreRunoff(
        List<PointD> path,
        MagickImage image,
        string riverName,
        int maxOffshorePixels = DEFAULT_MAX_OFFSHORE_DISTANCE)
    {
        if (path.Count < 2)
            return path;

        // Check from the end (mouth) backwards
        int offshoreCount = 0;
        int lastLandIndex = path.Count - 1;

        // Magenta is ocean in CK3 convention
        var magentaOcean = new MagickColor(255, 0, 255);

        using var pixels = image.GetPixels();

        // Walk backwards from mouth to find where river enters ocean
        for (int i = path.Count - 1; i >= 0; i--)
        {
            var point = path[i];
            var pixelColor = pixels.GetPixel((int)point.X, (int)point.Y);

            if (pixelColor != null)
            {
                var color = pixelColor.ToColor();

                // Check if this pixel is in ocean (magenta)
                if (color != null && ColorsMatch(color, magentaOcean))
                {
                    offshoreCount++;

                    // If we've exceeded max offshore distance, trim here
                    if (offshoreCount > maxOffshorePixels)
                    {
                        lastLandIndex = i + maxOffshorePixels;
                        break;
                    }
                }
                else
                {
                    // We hit land, stop counting
                    lastLandIndex = i;
                    break;
                }
            }
        }

        // If we need to trim
        if (lastLandIndex < path.Count - 1)
        {
            var trimmed = path.Take(lastLandIndex + 1).ToList();

            if (Settings.Instance.Debug)
            {
                int removedCount = path.Count - trimmed.Count;
                Console.WriteLine($"  Offshore trimming for {riverName}: removed {removedCount} pixels (was {offshoreCount} pixels offshore)");
            }

            return trimmed;
        }

        return path;
    }

    /// <summary>
    /// Check if a river has excessive offshore runoff without trimming it.
    /// </summary>
    public static (bool hasViolation, int offshorePixels) CheckOffshoreRunoff(
        List<PointD> path,
        MagickImage image,
        int maxOffshorePixels = DEFAULT_MAX_OFFSHORE_DISTANCE)
    {
        if (path.Count < 2)
            return (false, 0);

        var magentaOcean = new MagickColor(255, 0, 255);
        using var pixels = image.GetPixels();

        int offshoreCount = 0;

        // Walk backwards from mouth
        for (int i = path.Count - 1; i >= 0; i--)
        {
            var point = path[i];
            var pixelColor = pixels.GetPixel((int)point.X, (int)point.Y);

            if (pixelColor != null)
            {
                var color = pixelColor.ToColor();

                if (color != null && ColorsMatch(color, magentaOcean))
                {
                    offshoreCount++;
                }
                else
                {
                    // Hit land, stop counting
                    break;
                }
            }
        }

        bool hasViolation = offshoreCount > maxOffshorePixels;
        return (hasViolation, offshoreCount);
    }

    /// <summary>
    /// Check if a river runs through ocean in the middle (not just at the mouth).
    /// This is typically invalid - rivers shouldn't cross ocean mid-stream.
    /// </summary>
    public static List<int> FindMidstreamOceanPixels(List<PointD> path, MagickImage image)
    {
        if (path.Count < 3)
            return new List<int>();

        var magentaOcean = new MagickColor(255, 0, 255);
        using var pixels = image.GetPixels();

        var violatingIndices = new List<int>();

        // Check all pixels except the last few (those are allowed to be in ocean)
        int checkUntil = Math.Max(0, path.Count - 5); // Don't check last 5 pixels (near mouth)

        for (int i = 0; i < checkUntil; i++)
        {
            var point = path[i];
            var pixelColor = pixels.GetPixel((int)point.X, (int)point.Y);

            if (pixelColor != null)
            {
                var color = pixelColor.ToColor();

                if (color != null && ColorsMatch(color, magentaOcean))
                {
                    violatingIndices.Add(i);
                }
            }
        }

        return violatingIndices;
    }

    private static bool ColorsMatch(IMagickColor<byte> a, IMagickColor<byte> b)
    {
        return a.R == b.R && a.G == b.G && a.B == b.B;
    }
}
