using Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public record TerrainMaskEntry(string FileName, IReadOnlyList<Cell> WhiteCells);

/// <summary>
/// Decides which cells paint white pixels into each CK3 terrain mask file.
///
/// ── WHAT THIS IS ────────────────────────────────────────────────────────────
/// CK3 terrain masks are greyscale PNGs in gfx/map/terrain/masks/.
/// Each file controls one visual layer of the map surface (e.g. where forest
/// texture appears, where snow appears). White = full texture weight, black = none.
/// CK3 blends multiple masks additively to produce the final map look.
///
/// ── WHAT THIS IS NOT ────────────────────────────────────────────────────────
/// These masks have NO effect on gameplay. Movement cost, development caps,
/// combat modifiers, and holding types are all controlled by province_terrain.txt.
/// Changing a mask only changes how the map looks.
///
/// ── INPUTS ──────────────────────────────────────────────────────────────────
/// • Cell.Biome     — Azgaar biome index (0=Marine … 12=Wetland). See BiomeMasks.
/// • Cell.GeoHeight — Azgaar elevation (0–100; sea ≤ 20, land > 20).
/// • Cell.Neighbors — adjacent cell IDs used to compute per-cell roughness.
///
/// ── ROUGHNESS ────────────────────────────────────────────────────────────────
/// Computed via Helper.ComputeRoughness — see that method for the formula.
/// Hills and mountains masks are mutually exclusive (a cell appears in at most one).
/// </summary>
public static class TerrainMaskPreparer
{
    // Maps mask filename → Azgaar biome ints that contribute white pixels to it.
    private static readonly Dictionary<string, int[]> BiomeMasks = new()
    {
        ["desert_01_mask.png"]            = [1],        // Hot desert
        ["mountain_02_d_desert_mask.png"] = [2],        // Cold desert
        ["plains_01_mask.png"]            = [3, 4, 10], // Savanna + Grassland + Tundra
        ["forest_leaf_01_mask.png"]       = [5, 6],     // Tropical seasonal + Temperate deciduous
        ["forest_jungle_01_mask.png"]     = [7],        // Tropical rainforest
        ["forest_pine_01_mask.png"]       = [8],        // Temperate rainforest
        ["plains_01_dry_mask.png"]        = [9],        // Taiga
        ["mountain_02_c_snow_mask.png"]   = [11],       // Glacier
        ["wetlands_02_mask.png"]          = [12],       // Wetland
        ["farmland_01_mask.png"]          = [],         // reserved for future population+biome logic
        ["oasis_mask.png"]               = [],          // reserved
    };

    public static IReadOnlyList<TerrainMaskEntry> Prepare(Map map)
    {
        var allCells = map.Cells!;
        var landCells = allCells.Values.Where(c => Cell.IsDryLand(c.Type)).ToList();

        var roughness = Helper.ComputeRoughness(allCells, Settings.Instance.RoughnessNormalisation);

        var entries = new List<TerrainMaskEntry>();

        foreach (var (fileName, biomes) in BiomeMasks)
        {
            var biomeSet = new HashSet<int>(biomes);
            entries.Add(new TerrainMaskEntry(
                fileName,
                biomeSet.Count > 0
                    ? landCells.Where(c => biomeSet.Contains(c.Biome)).ToList()
                    : []));
        }

        // Roughness-driven masks — mutually exclusive
        entries.Add(new TerrainMaskEntry(
            "hills_01_mask.png",
            landCells.Where(c => roughness.TryGetValue(c.Id, out var r)
                              && r >= Settings.Instance.HillsThreshold
                              && r <  Settings.Instance.MountainsThreshold).ToList()));

        entries.Add(new TerrainMaskEntry(
            "mountain_02_mask.png",
            landCells.Where(c => roughness.TryGetValue(c.Id, out var r)
                              && r >= Settings.Instance.MountainsThreshold).ToList()));

        return entries;
    }
}
