using ImageMagick;

namespace Converter.Lemur.Provinces;

public static class ProvinceImageValidator
{
    public static bool Validate(
        string provincesPath,
        string? definitionCsvPath,
        out List<string> errors)
    {
        errors = new List<string>();

        if (!File.Exists(provincesPath))
        {
            errors.Add($"File not found: {provincesPath}");
            return false;
        }

        Console.WriteLine("Validating provinces.png...");

        using var image = new MagickImage(provincesPath);

        // Check 1 — Format
        var colorType = image.ColorType;
        bool formatOk = colorType is ColorType.TrueColor or ColorType.Palette;
        if (!formatOk)
        {
            errors.Add($"Image has alpha channel ({colorType}). CK3 requires 24-bit RGB or 8-bit palette PNG — alpha will cause a CTD.");
            Console.WriteLine($"  Format: {colorType} (has alpha) ✗");
        }
        else
        {
            Console.WriteLine($"  Format: {colorType} ✓");
        }

        // Check 2 — Color cross-check (only when definition.csv provided)
        if (!string.IsNullOrWhiteSpace(definitionCsvPath))
        {
            if (!File.Exists(definitionCsvPath))
            {
                errors.Add($"definition.csv not found: {definitionCsvPath}");
            }
            else
            {
                Console.WriteLine("  Cross-checking with definition.csv...");
                CrossCheckColors(image, definitionCsvPath, errors);
            }
        }

        if (errors.Count == 0)
            Console.WriteLine("  ✓ Valid");
        else
            Console.WriteLine($"  ✗ Invalid ({errors.Count} error{(errors.Count == 1 ? "" : "s")})");

        return errors.Count == 0;
    }

    private static void CrossCheckColors(MagickImage image, string definitionCsvPath, List<string> errors)
    {
        // Parse definition.csv
        var definedColors = new HashSet<(byte R, byte G, byte B)>();
        int definedCount = 0;
        foreach (var line in File.ReadLines(definitionCsvPath))
        {
            var parts = line.Split(';');
            if (parts.Length < 4) continue;
            if (!int.TryParse(parts[0], out int index) || index == 0) continue;
            if (!byte.TryParse(parts[1], out byte r)) continue;
            if (!byte.TryParse(parts[2], out byte g)) continue;
            if (!byte.TryParse(parts[3], out byte b)) continue;
            definedColors.Add((r, g, b));
            definedCount++;
        }

        // Scan every pixel
        var colorCounts = new Dictionary<(byte R, byte G, byte B), int>();
        int w = (int)image.Width;
        int h = (int)image.Height;

        using var pixels = image.GetPixels();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var px = pixels.GetPixel(x, y);
                if (px == null) continue;
                var c = px.ToColor();
                if (c == null) continue;

                var key = (c.R, c.G, c.B);
                // Skip black (impassable/background)
                if (key == (0, 0, 0)) continue;

                colorCounts.TryGetValue(key, out int cnt);
                colorCounts[key] = cnt + 1;
            }
        }

        // Report colors in image but not in definition.csv
        foreach (var kvp in colorCounts)
        {
            if (!definedColors.Contains(kvp.Key))
            {
                errors.Add(
                    $"Found {kvp.Value} pixels with color ({kvp.Key.R},{kvp.Key.G},{kvp.Key.B}) not in definition.csv — likely antialiasing or missing definition");
            }
        }

        // Report provinces in definition.csv missing from image
        int foundCount = colorCounts.Count;
        if (foundCount < definedCount)
        {
            int missing = definedCount - foundCount;
            errors.Add(
                $"definition.csv has {definedCount} entries but only {foundCount} unique colors found in provinces.png — {missing} provinces not painted");
        }
    }
}
