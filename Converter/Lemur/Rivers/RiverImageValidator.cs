using System.Drawing;
using ImageMagick;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Validates a CK3 rivers.png image against the game's structural requirements:
/// 1. Must be an 8-bit indexed (palette) PNG.
/// 2. Must contain only valid CK3 palette colors.
/// 3. River marker pixels must have the correct number of orthogonal river-body neighbours.
/// </summary>
public static class RiverImageValidator
{
    // CK3 palette indices 3–11: the 9 valid river-body shades (thinnest → widest)
    private static readonly (byte R, byte G, byte B)[] RiverBodyColors =
    [
        (0, 225, 255),  // #00e1ff  index  3 — thinnest
        (0, 200, 255),  // #00c8ff  index  4
        (0, 150, 255),  // #0096ff  index  5
        (0, 100, 255),  // #0064ff  index  6
        (0,   0, 255),  // #0000ff  index  7
        (0,   0, 225),  // #0000e1  index  8
        (0,   0, 200),  // #0000c8  index  9
        (0,   0, 150),  // #000096  index 10
        (0,   0, 100),  // #000064  index 11 — widest
    ];

    private enum PixelClass { Blue, Green, Red, Yellow, Skip, Invalid }

    /// <summary>
    /// Validates a loaded rivers image against CK3 requirements.
    /// </summary>
    /// <param name="image">The image to validate.</param>
    /// <param name="invalidPixels">
    ///   Populated with coordinates of invalid pixels, or <c>null</c> if all pixels are valid.
    /// </param>
    /// <param name="message">
    ///   Describes a non-pixel issue (e.g. wrong encoding), or <c>null</c> if none.
    /// </param>
    /// <returns><c>true</c> if the image passes all checks.</returns>
    public static bool ValidateRivers(
        MagickImage image,
        out List<Point>? invalidPixels,
        out string? message)
    {
        // Step 1 — Format check: must be 8-bit indexed palette
        bool isIndexed = image.ColorType is ColorType.Palette or ColorType.PaletteAlpha;
        bool is8Bit = image.Depth == 8;
        if (!isIndexed)
            message = $"Not color-indexed (found {image.ColorType}). CK3 requires an 8-bit palette PNG.";
        else if (!is8Bit)
            message = $"Palette depth is {image.Depth}-bit. CK3 requires 8-bit — ImageMagick auto-optimises to 1-bit when only 2 colours are present.";
        else
            message = null;

        var violations = new List<Point>();
        int w = (int)image.Width;
        int h = (int)image.Height;

        using var pixels = image.GetPixels();

        // Step 2 & 3 — Scan every pixel, classify, check connectivity rules
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var px = pixels.GetPixel(x, y);
                if (px == null) continue;
                var c = px.ToColor();
                if (c == null) continue;

                var kind = Classify(c.R, c.G, c.B);

                if (kind == PixelClass.Skip)
                    continue;

                if (kind == PixelClass.Invalid)
                {
                    violations.Add(new Point(x, y));
                    continue;
                }

                // Count orthogonal (N/S/E/W) river-body neighbours
                int blueNeighbors = CountRiverBodyNeighbors(x, y, pixels, w, h);

                bool violation = kind switch
                {
                    PixelClass.Blue   => blueNeighbors < 1 || blueNeighbors > 2,
                    PixelClass.Green  => blueNeighbors != 1,
                    PixelClass.Red    => blueNeighbors != 2,
                    PixelClass.Yellow => blueNeighbors != 2,
                    _                 => false
                };

                if (violation)
                    violations.Add(new Point(x, y));
            }
        }

        invalidPixels = violations.Count > 0 ? violations : null;
        return message == null && invalidPixels == null;
    }

    /// <summary>
    /// Overload that loads the image from a file path.
    /// </summary>
    public static bool ValidateRivers(
        string imagePath,
        out List<Point>? invalidPixels,
        out string? message)
    {
        if (!File.Exists(imagePath))
        {
            invalidPixels = null;
            message = $"File not found: {imagePath}";
            return false;
        }

        using var image = new MagickImage(imagePath);
        return ValidateRivers(image, out invalidPixels, out message);
    }

    // -------------------------------------------------------------------------

    private static PixelClass Classify(byte r, byte g, byte b)
    {
        if (IsRiverBody(r, g, b))        return PixelClass.Blue;
        if (r == 0   && g == 255 && b == 0)   return PixelClass.Green;
        if (r == 255 && g == 0   && b == 0)   return PixelClass.Red;
        if (r == 255 && g == 252 && b == 0)   return PixelClass.Yellow;
        if (r == 255 && g == 255 && b == 255) return PixelClass.Skip;  // land
        if (r == 255 && g == 0   && b == 128) return PixelClass.Skip;  // ocean #ff0080
        if (r == 255 && g == 0   && b == 255) return PixelClass.Skip;  // legacy magenta (tolerated)
        return PixelClass.Invalid;
    }

    private static bool IsRiverBody(byte r, byte g, byte b)
    {
        foreach (var c in RiverBodyColors)
            if (c.R == r && c.G == g && c.B == b) return true;
        return false;
    }

    private static int CountRiverBodyNeighbors(
        int x, int y, IPixelCollection<byte> pixels, int w, int h)
    {
        int count = 0;
        Span<(int dx, int dy)> dirs = stackalloc (int, int)[]
            { (0, -1), (0, 1), (-1, 0), (1, 0) };

        foreach (var (dx, dy) in dirs)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
            var np = pixels.GetPixel(nx, ny);
            if (np == null) continue;
            var nc = np.ToColor();
            if (nc != null && IsRiverBody(nc.R, nc.G, nc.B))
                count++;
        }
        return count;
    }
}
