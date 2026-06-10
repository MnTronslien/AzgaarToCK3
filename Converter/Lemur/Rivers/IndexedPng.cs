using System.IO.Compression;
using ImageMagick;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Minimal 8-bit indexed (colour-type 3) PNG writer with an explicit, exact palette.
/// ImageMagick can't emit a fixed 256-entry PLTE — it compacts the colour set and drops the
/// duplicate filler entries — but CK3 reads rivers.png by palette INDEX, so we must control the
/// PLTE and per-pixel indices byte-for-byte. This writer does exactly that and nothing else.
/// </summary>
public static class IndexedPng
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// Writes <paramref name="indices"/> (one palette index per pixel, row-major) as an 8-bit
    /// indexed PNG with the given 256-entry <paramref name="palette"/>.
    /// </summary>
    public static void Write(string path, byte[] indices, int width, int height, MagickColor[] palette)
    {
        if (indices.Length != width * height)
            throw new ArgumentException($"indices length {indices.Length} != {width}x{height}");
        if (palette.Length != 256)
            throw new ArgumentException($"palette must have 256 entries, got {palette.Length}");

        using var fs = File.Create(path);
        fs.Write(Signature, 0, Signature.Length);

        // IHDR
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)width);
        WriteBE(ihdr, 4, (uint)height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 3;   // colour type 3 = indexed
        ihdr[10] = 0;  // compression
        ihdr[11] = 0;  // filter
        ihdr[12] = 0;  // interlace
        WriteChunk(fs, "IHDR", ihdr);

        // PLTE — 256 RGB triples
        var plte = new byte[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            plte[i * 3] = palette[i].R;
            plte[i * 3 + 1] = palette[i].G;
            plte[i * 3 + 2] = palette[i].B;
        }
        WriteChunk(fs, "PLTE", plte);

        // IDAT — scanlines (filter byte 0 = None, then one index byte per pixel), zlib-compressed
        byte[] raw = new byte[height * (width + 1)];
        for (int y = 0; y < height; y++)
        {
            int dst = y * (width + 1);
            raw[dst] = 0; // filter: None
            Buffer.BlockCopy(indices, y * width, raw, dst + 1, width);
        }
        using (var ms = new MemoryStream())
        {
            using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw, 0, raw.Length);
            WriteChunk(fs, "IDAT", ms.ToArray());
        }

        WriteChunk(fs, "IEND", Array.Empty<byte>());
    }

    private static void WriteBE(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var lenBuf = new byte[4];
        WriteBE(lenBuf, 0, (uint)data.Length);
        s.Write(lenBuf, 0, 4);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes, 0, 4);
        s.Write(data, 0, data.Length);

        uint crc = Crc32(typeBytes, data);
        var crcBuf = new byte[4];
        WriteBE(crcBuf, 0, crc);
        s.Write(crcBuf, 0, 4);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}
