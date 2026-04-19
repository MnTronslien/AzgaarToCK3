using Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public record TerrainMaskEntry(string FileName, IReadOnlyList<Cell> WhiteCells);

public static class TerrainMaskPreparer
{
    // Maps mask filename → Azgaar biome ints that contribute white pixels to it.
    // Source: upstream BiomeConverter.cs + inspection of upstream gfx/map/terrain/.
    private static readonly Dictionary<string, int[]> BiomeMasks = new()
    {
        ["desert_01_mask.png"]          = [1],
        ["mountain_02_desert_mask.png"] = [2],
        ["plains_01_mask.png"]          = [3, 4],
        ["farmland_01_mask.png"]        = [5],
        ["forest_leaf_01_mask.png"]     = [6],
        ["forest_jungle_01_mask.png"]   = [7],
        ["forest_pine_01_mask.png"]     = [8, 9],
        ["mountain_02_snow_mask.png"]   = [10],
        ["mountain_02_c_snow_mask.png"] = [11],
        ["wetlands_02_mask.png"]        = [12],
    };

    public static IReadOnlyList<TerrainMaskEntry> Prepare(Map map)
    {
        var landCells = map.Cells!.Values
            .Where(c => Cell.IsDryLand(c.Type))
            .ToList();

        int maxHeight = landCells.Count > 0 ? landCells.Max(c => c.GeoHeight) : 1;
        if (maxHeight == 0) maxHeight = 1;

        var entries = new List<TerrainMaskEntry>();

        foreach (var (fileName, biomes) in BiomeMasks)
        {
            var biomeSet = new HashSet<int>(biomes);
            entries.Add(new TerrainMaskEntry(
                fileName,
                landCells.Where(c => biomeSet.Contains(c.Biome)).ToList()));
        }

        entries.Add(new TerrainMaskEntry(
            "hills_01_mask.png",
            landCells.Where(c => c.GeoHeight >= maxHeight * 0.40 && c.GeoHeight < maxHeight * 0.70).ToList()));

        entries.Add(new TerrainMaskEntry(
            "mountain_02_mask.png",
            landCells.Where(c => c.GeoHeight >= maxHeight * 0.70).ToList()));

        entries.Add(new TerrainMaskEntry("oasis_mask.png", []));

        return entries;
    }
}
