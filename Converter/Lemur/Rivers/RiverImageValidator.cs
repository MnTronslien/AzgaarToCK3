using System.Drawing;
using System.Linq;
using ImageMagick;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Validates a CK3 rivers.png image against the game's structural requirements:
/// 1. Must be an 8-bit indexed (palette) PNG.
/// 2. Palette must be in CK3's exact index order (CK3 reads rivers by palette INDEX, not RGB — a
///    scrambled/compacted order renders the rivers invisible even though the colours look right).
/// 3. Must contain only valid CK3 palette colors.
/// 4. River marker pixels must have the correct number of orthogonal river-body neighbours.
/// (The index-order check — #2 — only runs through the file-path overload, which can read the PLTE.)
/// </summary>
public static class RiverImageValidator
{
    // The 9 valid river-body shades (palette indices 3–11, thinnest→widest), taken from the single
    // canonical palette so the validator and generator can never disagree on what counts as a river.
    private static readonly (byte R, byte G, byte B)[] RiverBodyColors =
        RiverImageGenerator.RiverShades.Select(c => (c.R, c.G, c.B)).ToArray();

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
    /// Overload that loads the image from a file path. Also checks the palette INDEX ORDER, which
    /// the <see cref="MagickImage"/> overload cannot (it needs the raw PLTE chunk from the file).
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

        // Palette index order — the failure mode that makes rivers invisible in-game.
        string? paletteMsg = ValidatePaletteOrder(imagePath);

        using var image = new MagickImage(imagePath);
        bool pixelsOk = ValidateRivers(image, out invalidPixels, out string? imageMsg);

        // Surface the palette problem first (it's the more fundamental "won't render" issue).
        message = (paletteMsg, imageMsg) switch
        {
            (not null, not null) => $"{paletteMsg}; {imageMsg}",
            (not null, null)     => paletteMsg,
            _                    => imageMsg
        };
        return paletteMsg == null && pixelsOk;
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Checks that the PNG's palette matches CK3's canonical index order at every index the engine
    /// actually reads (0–15: markers + the 9 river widths + reserved greens; 254 = sea; 255 = land).
    /// Filler indices 16–253 are never referenced by the engine, so they are not checked. Returns a
    /// description of the first mismatches, or <c>null</c> if the order is correct.
    /// </summary>
    private static string? ValidatePaletteOrder(string imagePath)
    {
        var palette = ReadPngPalette(imagePath);
        if (palette == null)
            return "no PLTE chunk found — not an 8-bit indexed PNG (CK3 reads rivers by palette index).";

        var canonical = RiverImageGenerator.Ck3RiverPalette;
        var meaningful = Enumerable.Range(0, 16).Append(254).Append(255);
        var bad = new List<string>();

        foreach (int i in meaningful)
        {
            if (i >= palette.Length)
            {
                bad.Add($"index {i} missing (palette has only {palette.Length} entries)");
                continue;
            }
            var (r, g, b) = palette[i];
            var c = canonical[i];
            if (r != c.R || g != c.G || b != c.B)
                bad.Add($"index {i} is #{r:X2}{g:X2}{b:X2}, expected #{c.R:X2}{c.G:X2}{c.B:X2}");
        }

        if (bad.Count == 0)
            return null;

        string detail = string.Join("; ", bad.Take(6)) + (bad.Count > 6 ? $"; +{bad.Count - 6} more" : "");
        return $"palette index order is wrong (CK3 reads rivers by index, not RGB — a scrambled or "
             + $"compacted palette makes rivers invisible): {detail}";
    }

    /// <summary>
    /// Reads the raw PLTE (palette) chunk from a PNG, in file order — i.e. exactly the index→colour
    /// table CK3 reads. Returns null if the file has no PLTE chunk (truecolour / not indexed).
    /// </summary>
    private static (byte r, byte g, byte b)[]? ReadPngPalette(string path)
    {
        byte[] d;
        try { d = File.ReadAllBytes(path); }
        catch { return null; }
        if (d.Length < 8) return null;

        int pos = 8; // skip the 8-byte PNG signature
        while (pos + 8 <= d.Length)
        {
            int len = (d[pos] << 24) | (d[pos + 1] << 16) | (d[pos + 2] << 8) | d[pos + 3];
            string type = System.Text.Encoding.ASCII.GetString(d, pos + 4, 4);
            int dataStart = pos + 8;

            if (type == "PLTE")
            {
                if (len % 3 != 0 || dataStart + len > d.Length) return null;
                int n = len / 3;
                var pal = new (byte, byte, byte)[n];
                for (int i = 0; i < n; i++)
                    pal[i] = (d[dataStart + i * 3], d[dataStart + i * 3 + 1], d[dataStart + i * 3 + 2]);
                return pal;
            }
            if (type == "IDAT" || type == "IEND") break; // PLTE always precedes image data

            pos = dataStart + len + 4; // advance past chunk data + 4-byte CRC
        }
        return null;
    }

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
