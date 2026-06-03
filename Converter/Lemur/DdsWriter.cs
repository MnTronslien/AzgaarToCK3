namespace Converter.Lemur;

/// <summary>
/// Writes a solid-colour, uncompressed 32-bit BGRA <c>.dds</c> from code — no shipped binary, no
/// image library. Used for the map-render rasters that only need to be a single uniform value to
/// override vanilla (colormap, surround mask/fade, water flow/foam/colour). The values match what
/// TCS shipped (sampled — every pixel identical), so the in-game look is unchanged while the build
/// stays TCS-free and the repo carries no large DDS. Enrichment (real detail) is a logged follow-up.
/// </summary>
public static class DdsWriter
{
    // DDS header flag constants (see Microsoft DDS_HEADER / DDS_PIXELFORMAT docs).
    private const uint DDSD_CAPS = 0x1, DDSD_HEIGHT = 0x2, DDSD_WIDTH = 0x4, DDSD_PIXELFORMAT = 0x1000, DDSD_PITCH = 0x8;
    private const uint DDPF_ALPHAPIXELS = 0x1, DDPF_RGB = 0x40;
    private const uint DDSCAPS_TEXTURE = 0x1000;

    /// <summary>Write a width×height image where every pixel is (r,g,b,a), as an uncompressed BGRA DDS.</summary>
    public static async Task WriteSolidAsync(string path, int width, int height, byte r, byte g, byte b, byte a)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        w.Write(0x20534444u);                                              // "DDS " magic
        w.Write(124u);                                                     // dwSize
        w.Write(DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT | DDSD_PITCH);
        w.Write((uint)height);
        w.Write((uint)width);
        w.Write((uint)(width * 4));                                        // dwPitchOrLinearSize (bytes/row)
        w.Write(0u);                                                       // depth
        w.Write(0u);                                                       // mipMapCount (0 = single)
        for (int i = 0; i < 11; i++) w.Write(0u);                          // dwReserved1[11]
        // DDS_PIXELFORMAT (32 bytes) — A8R8G8B8 (BGRA in memory).
        w.Write(32u);
        w.Write(DDPF_RGB | DDPF_ALPHAPIXELS);
        w.Write(0u);                                                       // fourCC (0 = uncompressed)
        w.Write(32u);                                                      // RGBBitCount
        w.Write(0x00FF0000u);                                              // R mask
        w.Write(0x0000FF00u);                                              // G mask
        w.Write(0x000000FFu);                                              // B mask
        w.Write(0xFF000000u);                                              // A mask
        w.Write(DDSCAPS_TEXTURE);                                          // caps
        w.Write(0u); w.Write(0u); w.Write(0u);                             // caps2/3/4
        w.Write(0u);                                                       // dwReserved2

        // Pixel data, one uniform row reused for every scanline. Memory order is B,G,R,A.
        var row = new byte[width * 4];
        for (int x = 0; x < width; x++)
        {
            int o = x * 4;
            row[o] = b; row[o + 1] = g; row[o + 2] = r; row[o + 3] = a;
        }
        for (int y = 0; y < height; y++)
            fs.Write(row, 0, row.Length);
    }
}
