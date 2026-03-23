using System.Text;
using System.Text.RegularExpressions;
using ImageMagick;

namespace Converter.Lemur;

public static class Helper
{
    /// <summary>UTF-8 encoding with BOM — used by CK3 for most script/data text files.</summary>
    public static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>UTF-8 encoding without BOM — required by CK3 for map_data/definition.csv.</summary>
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Deterministic seed mixing for seeding <see cref="Random"/> instances.
    /// Unlike <see cref="System.HashCode.Combine"/>, this is stable across process runs.
    /// .NET's HashCode uses a per-AppDomain random seed, making HashCode.Combine non-deterministic.
    /// </summary>
    public static int MixSeeds(int a, int b) =>
        unchecked(a * 1664525 + b * 22695477 + 1013904223);

    /// <inheritdoc cref="MixSeeds(int,int)"/>
    public static int MixSeeds(int a, int b, int c) =>
        unchecked(MixSeeds(a, b) * 1664525 + c * 22695477 + 1013904223);

    public static PointD GeoToPixel(float lon, float lat, Entities.Map map)
    {
        return new PointD((lon - map.XOffset) * map.XRatio, Map.MapHeight - (lat - map.YOffset) * map.YRatio);
    }

    /// <summary>
    /// Converts Azgaar burg pixel coordinates (burg.Position.X/Y) to CK3 map pixel coordinates.
    /// Azgaar burgs store canvas pixel positions, not geo coordinates — use this, not GeoToPixel.
    /// </summary>
    public static PointD BurgToPixel(float x, float y, Entities.Map map)
    {
        double xRatio = (double)Entities.Map.MapWidth / map.JsonMap.info.width;
        double yRatio = (double)Entities.Map.MapHeight / map.JsonMap.info.height;
        return new PointD(x * xRatio, Entities.Map.MapHeight - y * yRatio);
    }

    public static string GeoToString(float[][] geo)
    {
        return $"({geo[0][0]} , {geo[0][1]})";
    }


    public static string GetPath(params string[] paths)
    {
        if (paths is null) return "";
        return Path.Combine(paths.SelectMany(n => n.Replace(@"\\", "/").Replace(@"\", "/").Split("/")).ToArray());
    }

    public static void PrintSectionHeader(string header)
    {
        string line = new('=', header.Length);
        Console.WriteLine(line);
        Console.WriteLine(header);
        Console.WriteLine(line);
    }
    /// <summary>
    /// Generate a unique color for i along the range of 0 to maxI.
    /// </summary>
    /// <param name="i"> Must be less than maxI</param>
    /// <param name="maxI"></param>
    /// <returns></returns>
    /// <exception cref="FormatException"></exception>
    public static bool PointInPolygon(PointD[] polygon, int px, int py)
    {
        bool inside = false;
        int j = polygon.Length - 1;
        for (int i = 0; i < polygon.Length; i++)
        {
            double xi = polygon[i].X, yi = polygon[i].Y;
            double xj = polygon[j].X, yj = polygon[j].Y;
            if ((yi > py) != (yj > py) &&
                px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                inside = !inside;
            j = i;
        }
        return inside;
    }

    /// <summary>
    /// Converts a name to a valid CK3 title identifier.
    /// Pattern: {prefix}_{lowercase_underscored_ascii_name}_{id}
    /// Example: ToCk3Id("e", "Roman Empire", 5) → "e_roman_empire_5"
    /// </summary>
    public static string ToCk3Id(string prefix, string name, int id)
    {
        var lower = name.ToLowerInvariant();
        var underscored = Regex.Replace(lower, @"[\s\-]+", "_");
        var ascii = Regex.Replace(underscored, @"[^a-z0-9_]", "");
        var clean = Regex.Replace(ascii, @"_+", "_").Trim('_');
        if (string.IsNullOrEmpty(clean)) clean = "unnamed";
        return $"{prefix}_{clean}_{id}";
    }

    /// <summary>
    /// Merges <paramref name="source"/> into <paramref name="target"/>, adding values for matching keys.
    /// </summary>
    public static void MergeAdd(this Dictionary<int, int> target, Dictionary<int, int> source)
    {
        foreach (var kvp in source)
            target[kvp.Key] = target.GetValueOrDefault(kvp.Key, 0) + kvp.Value;
    }

    public static MagickColor GetColor(int i, int maxI)
    {
        if (maxI >= 16777216)
        {
            throw new FormatException("MaxI is too big. MaxI must be less than 16777216, to ensure that the color is unique for each i");
        }
        if (i < 0 || i >= maxI)
        {
            throw new FormatException("i must be between 0 and maxI");
        }


        // max 24bit color
        const int maxColor = 256 * 256 * 256;
        var color = maxColor / maxI * i;

        byte r = (byte)((color & 0x0000FF) >> 0);
        byte g = (byte)((color & 0x00FF00) >> 8);
        byte b = (byte)((color & 0xFF0000) >> 16);

        var c = new MagickColor(r, g, b);

        return c;
    }

}