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

        Logger.Info("Validating provinces.png...");

        using var image = new MagickImage(provincesPath);

        // Check 1 — Format
        var colorType = image.ColorType;
        bool formatOk = colorType is ColorType.TrueColor or ColorType.Palette;
        if (!formatOk)
        {
            errors.Add($"Image has alpha channel ({colorType}). CK3 requires 24-bit RGB or 8-bit palette PNG — alpha will cause a CTD.");
            Logger.Info($"  Format: {colorType} (has alpha) ✗");
        }
        else
        {
            Logger.Info($"  Format: {colorType} ✓");
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
                Logger.Info("  Cross-checking with definition.csv...");
                CrossCheckColors(image, definitionCsvPath, errors);
            }
        }

        if (errors.Count == 0)
            Logger.Info("  ✓ Valid");
        else
            Logger.Info($"  ✗ Invalid ({errors.Count} error{(errors.Count == 1 ? "" : "s")})");

        return errors.Count == 0;
    }

    private static void CrossCheckColors(MagickImage image, string definitionCsvPath, List<string> errors)
    {
        // Parse definition.csv into (id, name, R, G, B) entries
        var definedEntries = new List<(int Id, string Name, byte R, byte G, byte B)>();
        var definedColorSet = new HashSet<(byte R, byte G, byte B)>();

        foreach (var line in File.ReadLines(definitionCsvPath))
        {
            var parts = line.Split(';');
            if (parts.Length < 5) continue;
            if (!int.TryParse(parts[0], out int index) || index == 0) continue;
            if (!byte.TryParse(parts[1], out byte r)) continue;
            if (!byte.TryParse(parts[2], out byte g)) continue;
            if (!byte.TryParse(parts[3], out byte b)) continue;
            var name = parts[4];
            definedEntries.Add((index, name, r, g, b));
            definedColorSet.Add((r, g, b));
        }

        Logger.Info($"  definition.csv: {definedEntries.Count} province entries");

        // Scan every pixel — count pixels per color
        var colorCounts = new Dictionary<(byte R, byte G, byte B), int>();
        int blackPixelCount = 0;
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
                if (key == (0, 0, 0))
                {
                    blackPixelCount++;
                    continue;
                }

                colorCounts.TryGetValue(key, out int cnt);
                colorCounts[key] = cnt + 1;
            }
        }

        if (blackPixelCount > 0)
        {
            errors.Add($"Black pixels (0,0,0): {blackPixelCount:N0} — undefined province color will crash CK3 map generator");
            Logger.Info($"  Black pixels: {blackPixelCount:N0} ✗");
        }
        else
        {
            Logger.Info($"  Black pixels: 0 ✓");
        }

        Logger.Info($"  provinces.png: {colorCounts.Count} unique non-black colors");

        // Report colors in image but not in definition.csv
        int undefinedColorCount = 0;
        foreach (var kvp in colorCounts)
        {
            if (!definedColorSet.Contains(kvp.Key))
            {
                errors.Add(
                    $"Undefined color in image: ({kvp.Key.R},{kvp.Key.G},{kvp.Key.B}) — {kvp.Value} pixel(s) not mapped to any province in definition.csv");
                undefinedColorCount++;
            }
        }
        if (undefinedColorCount > 0)
            Logger.Info($"  {undefinedColorCount} color(s) in image not in definition.csv ✗");

        // Report each province in definition.csv missing from image
        int missingCount = 0;
        foreach (var entry in definedEntries)
        {
            if (!colorCounts.ContainsKey((entry.R, entry.G, entry.B)))
            {
                errors.Add(
                    $"Province {entry.Id} '{entry.Name}' (color {entry.R},{entry.G},{entry.B}) has no pixels in provinces.png");
                missingCount++;
            }
        }

        if (missingCount > 0)
            Logger.Info($"  {missingCount} province(s) in definition.csv not found in image ✗");
        else if (undefinedColorCount == 0)
            Logger.Info($"  All {definedEntries.Count} provinces present in image ✓");
    }
}
