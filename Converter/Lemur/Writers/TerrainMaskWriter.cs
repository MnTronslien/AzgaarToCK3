using ImageMagick;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class TerrainMaskWriter
{
    public static async Task Write(
        IReadOnlyList<TerrainMaskEntry> masks,
        L.Map map,
        string tcsSandboxPath,
        string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing terrain mask files");

        var masksDir = Helper.GetPath(outputDirectory, "gfx", "map", "terrain", "masks");
        var terrainDir = Helper.GetPath(outputDirectory, "gfx", "map", "terrain");
        Directory.CreateDirectory(masksDir);
        Directory.CreateDirectory(terrainDir);

        var readSettings = new MagickReadSettings
        {
            Width = L.Map.MapWidth,
            Height = L.Map.MapHeight,
        };

        await Task.WhenAll(masks.Select(entry => WriteBiomeMask(entry, masksDir, readSettings, map)));

        await WriteColormapAsync(tcsSandboxPath, terrainDir);
        await WriteDetailIndexAsync(terrainDir);
        await WriteDetailIntensityAsync(terrainDir);

        Logger.Info($"Wrote {masks.Count} terrain mask PNGs + colormap.dds + detail TGAs to gfx/map/terrain/");
    }

    private static async Task WriteBiomeMask(
        TerrainMaskEntry entry, string masksDir,
        MagickReadSettings readSettings, L.Map map)
    {
        using var image = new MagickImage("xc:black", readSettings);
        if (entry.WhiteCells.Count > 0)
        {
            var drawables = ImageUtility.GenerateCellPolygons(entry.WhiteCells, MagickColors.White, map);
            image.Draw(drawables);
        }
        await image.WriteAsync(Path.Combine(masksDir, entry.FileName));
    }

    private static async Task WriteColormapAsync(string tcsSandboxPath, string terrainDir)
    {
        var src = Helper.GetPath(tcsSandboxPath, "gfx", "map", "terrain", "colormap.dds");
        var dst = Helper.GetPath(terrainDir, "colormap.dds");
        using var img = new MagickImage(src);
        img.Resize(L.Map.MapWidth / 4, L.Map.MapHeight / 4);
        await img.WriteAsync(dst);
    }

    private static async Task WriteDetailIndexAsync(string terrainDir)
    {
        var settings = new MagickReadSettings { Width = L.Map.MapWidth, Height = L.Map.MapHeight };
        using var img = new MagickImage("xc:white", settings);
        img.Alpha(AlphaOption.Set);
        img.Evaluate(Channels.Alpha, EvaluateOperator.Set, new Percentage(100));
        await img.WriteAsync(Helper.GetPath(terrainDir, "detail_index.tga"), MagickFormat.Tga);
    }

    private static async Task WriteDetailIntensityAsync(string terrainDir)
    {
        // Black fill with alpha=255 — prevents CK3 1.18 TGA reader from stripping the alpha channel
        var settings = new MagickReadSettings { Width = L.Map.MapWidth, Height = L.Map.MapHeight };
        using var img = new MagickImage("xc:black", settings);
        img.Alpha(AlphaOption.Set);
        img.Evaluate(Channels.Alpha, EvaluateOperator.Set, new Percentage(100));
        await img.WriteAsync(Helper.GetPath(terrainDir, "detail_intensity.tga"), MagickFormat.Tga);
    }
}
