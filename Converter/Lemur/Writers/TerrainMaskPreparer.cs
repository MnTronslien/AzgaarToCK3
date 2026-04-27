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
/// CK3 blends multiple active masks additively to produce the final map look.
///
/// ── WHAT THIS IS NOT ────────────────────────────────────────────────────────
/// These masks have NO effect on gameplay. Movement cost, development caps,
/// combat modifiers, and holding types are all controlled by province_terrain.txt
/// (written by ProvinceTerrainWriter). Changing a mask only changes how the map
/// looks — a province painted as forest here still plays as plains until
/// ProvinceTerrainWriter is updated.
///
/// ── INPUTS ──────────────────────────────────────────────────────────────────
/// • Cell.Biome     — Azgaar biome index (see BiomeMasks for full index→name mapping).
/// • Cell.GeoHeight — Azgaar normalised elevation (0–100; from pack.cells[i].h).
/// • Cell.Neighbors — adjacent cell IDs, used to compute per-cell roughness.
///
/// ── OUTPUTS (mask files) ─────────────────────────────────────────────────────
/// Biome-driven    : mask PNGs whitened for all cells of a given biome.
/// Roughness-driven: hills_01_mask and mountain_02_mask — mutually exclusive;
///                   a cell appears in at most one of these two.
/// Empty stubs     : farmland_01_mask, oasis_mask — reserved for future logic.
///
/// ── ROUGHNESS FORMULA ────────────────────────────────────────────────────────
/// roughness(cell) = clamp( avg(|cell.h − neighbour.h|) / RoughnessNormalisation, 0, 1 )
/// Only land neighbours (GeoHeight > 20) are included. Sea neighbours are excluded
/// so coastal cells are not penalised for bordering the ocean.
/// Thresholds and the normalisation constant are tunable via Settings.
///
/// ── FUTURE WORK ──────────────────────────────────────────────────────────────
/// Masks not yet generated: snow_mask, northern_plains_01_mask, steppe_01_mask,
/// beach_02_mask variants, medi_* variants. These are candidates for custom
/// shader-based writing in a future pass rather than cell-polygon painting.
/// farmland_01_mask will be driven by population + biome inference when implemented.
/// </summary>
public static class TerrainMaskPreparer
{
    // Azgaar biome index → CK3 mask filename(s).
    // Index: 0=Marine, 1=Hot desert, 2=Cold desert, 3=Savanna, 4=Grassland,
    //        5=Tropical seasonal forest, 6=Temperate deciduous forest,
    //        7=Tropical rainforest, 8=Temperate rainforest, 9=Taiga,
    //        10=Tundra, 11=Glacier, 12=Wetland
    private static readonly Dictionary<string, int[]> BiomeMasks = new()
    {
        ["desert_01_mask.png"]            = [1],        // Hot desert
        ["mountain_02_d_desert_mask.png"] = [2],        // Cold desert
        ["plains_01_mask.png"]            = [3, 4, 10], // Savanna + Grassland + Tundra
        ["forest_leaf_01_mask.png"]       = [5, 6],     // Tropical seasonal + Temperate deciduous
        ["forest_jungle_01_mask.png"]     = [7],        // Tropical rainforest
        ["forest_pine_01_mask.png"]       = [8],        // Temperate rainforest
        ["plains_01_dry_mask.png"]        = [9],        // Taiga — sparse, open northern character
        ["mountain_02_c_snow_mask.png"]   = [11],       // Glacier
        ["wetlands_02_mask.png"]          = [12],       // Wetland
        ["farmland_01_mask.png"]          = [],         // stub — future: population+biome inference
        ["oasis_mask.png"]               = [],          // stub — future
    };

    public static IReadOnlyList<TerrainMaskEntry> Prepare(Map map)
    {
        var allCells = map.Cells!;

        var landCells = allCells.Values
            .Where(c => Cell.IsDryLand(c.Type))
            .ToList();

        // Roughness: avg absolute elevation deviation from land neighbours only.
        // Sea neighbours excluded so coastal cells aren't falsely penalised.
        var roughness = new Dictionary<int, float>(landCells.Count);
        foreach (var cell in landCells)
        {
            var landNeighbourHeights = cell.Neighbors
                .Select(id => allCells.TryGetValue(id, out var n) ? n : null)
                .Where(n => n != null && Cell.IsDryLand(n!.Type))
                .Select(n => n!.GeoHeight)
                .ToList();

            float r = landNeighbourHeights.Count > 0
                ? (float)landNeighbourHeights.Average(h => Math.Abs(cell.GeoHeight - h))
                  / Settings.Instance.RoughnessNormalisation
                : 0f;

            roughness[cell.Id] = Math.Clamp(r, 0f, 1f);
        }

        var entries = new List<TerrainMaskEntry>();

        // Biome-driven masks
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
            landCells.Where(c => roughness[c.Id] >= Settings.Instance.HillsThreshold
                              && roughness[c.Id] <  Settings.Instance.MountainsThreshold)
                     .ToList()));

        entries.Add(new TerrainMaskEntry(
            "mountain_02_mask.png",
            landCells.Where(c => roughness[c.Id] >= Settings.Instance.MountainsThreshold)
                     .ToList()));

        return entries;
    }
}
