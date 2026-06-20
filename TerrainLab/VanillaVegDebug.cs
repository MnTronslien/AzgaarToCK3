using System.Globalization;
using ImageMagick;

namespace TerrainLab;

// Sanity check: parse VANILLA CK3's baked tree generators and plot them, so our density/distribution
// can be compared against the base game's. Reads gfx/map/map_object_data/generated/*.txt directly
// (no Azgaar data needed). Each object block has transform="x y z  qx qy qz qw  sx sy sz" rows; we
// take (x, z) per row. Coordinates are auto-fit to the canvas from their own bounds.
static class VanillaVegDebug
{
    public static void Render(string ck3Dir, string outPath)
    {
        var gen = Path.Combine(ck3Dir, "game", "gfx", "map", "map_object_data", "generated");
        if (!Directory.Exists(gen)) { Console.Error.WriteLine($"Not found: {gen}"); return; }

        var pts = new List<(float x, float z, byte r, byte g, byte b)>();
        var files = Directory.GetFiles(gen, "*.txt");
        foreach (var file in files)
        {
            var (r, g, b) = ColorFor(Path.GetFileName(file).ToLowerInvariant());
            var text = File.ReadAllText(file);
            int idx = 0;
            while (true)
            {
                int t = text.IndexOf("transform=\"", idx, StringComparison.Ordinal);
                if (t < 0) break;
                int start = t + 11;
                int end = text.IndexOf('"', start);
                if (end < 0) break;
                var toks = text.Substring(start, end - start)
                               .Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i + 9 < toks.Length; i += 10)
                    if (float.TryParse(toks[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                        float.TryParse(toks[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                        pts.Add((x, z, r, g, b));
                idx = end + 1;
            }
        }
        Console.WriteLine($"Vanilla: parsed {pts.Count} instances from {files.Length} generator files.");
        if (pts.Count == 0) return;

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in pts)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
        }
        float xr = MathF.Max(1, maxX - minX), zr = MathF.Max(1, maxZ - minZ);
        Console.WriteLine($"Bounds: x[{minX:F0}..{maxX:F0}] z[{minZ:F0}..{maxZ:F0}]  → density {pts.Count / (xr * zr):F4} trees/px²");

        int cw = 2048, ch = Math.Max(1, (int)(cw * zr / xr));
        var buf = new byte[cw * ch * 3];
        for (int i = 0; i < buf.Length; i += 3) { buf[i] = 235; buf[i + 1] = 232; buf[i + 2] = 225; }
        foreach (var p in pts)
        {
            int x = (int)((p.x - minX) / xr * (cw - 1));
            int y = (int)(ch - 1 - (p.z - minZ) / zr * (ch - 1));
            if (x < 0 || y < 0 || x >= cw || y >= ch) continue;
            int o = (y * cw + x) * 3; buf[o] = p.r; buf[o + 1] = p.g; buf[o + 2] = p.b;
        }
        var settings = new MagickReadSettings { Width = cw, Height = ch, Format = MagickFormat.Rgb };
        using var img = new MagickImage(buf, settings);
        var full = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        img.Write(full, MagickFormat.Png);
        Console.WriteLine($"Wrote vanilla veg debug: {full} ({cw}x{ch})");
    }

    static (byte, byte, byte) ColorFor(string f)
    {
        if (f.Contains("pine")) return (30, 80, 40);
        if (f.Contains("leaf")) return (60, 140, 50);
        if (f.Contains("jungle")) return (15, 90, 60);
        if (f.Contains("palm")) return (120, 160, 40);
        if (f.Contains("reeds")) return (90, 120, 80);
        if (f.Contains("bush") || f.Contains("steppe")) return (181, 160, 70);
        if (f.Contains("cypress")) return (70, 110, 70);
        if (f.Contains("sakura")) return (210, 120, 170);
        return (40, 120, 45);
    }
}
