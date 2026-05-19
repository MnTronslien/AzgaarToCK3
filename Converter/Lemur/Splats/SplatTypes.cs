namespace Converter.Lemur.Splats;

// CK3 terrain rendering reads detail_index.tga + detail_intensity.tga as a 4-layer per-pixel splat
// map (verified — see CK3_MAP_MODDING_FACTS.md). Each pixel stores up to 4 (biome index, blend
// weight) pairs across R/G/B/A of the two TGAs. The renderer composites the 4 textures weighted by
// the intensities.
//
// 255 in BiomeIndex is the "this layer unused" sentinel. Intensity must be 0 in unused slots —
// non-zero intensity in a slot whose index is 255 tells the renderer to blend texture #255 at
// non-zero weight, which produced the long-standing hex-cell artefact on our maps.

public readonly record struct SplatLayer(byte BiomeIndex, byte Intensity)
{
    public static SplatLayer Unused => new(255, 0);
}

public readonly record struct SplatPixel(
    SplatLayer L0,
    SplatLayer L1,
    SplatLayer L2,
    SplatLayer L3)
{
    public static SplatPixel AllUnused => new(
        SplatLayer.Unused, SplatLayer.Unused, SplatLayer.Unused, SplatLayer.Unused);
}

public sealed class Splatmap
{
    public readonly int Width;
    public readonly int Height;
    public readonly SplatPixel[] Pixels;

    public Splatmap(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new SplatPixel[width * height];
    }
}
