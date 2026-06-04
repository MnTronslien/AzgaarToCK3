using Converter.Lemur.Entities;
using Converter.Lemur.Provinces;

namespace Converter.Lemur.Fields;

/// <summary>
/// Builds a <see cref="CultureContext"/> per Azgaar culture from the canonical land the
/// pipeline already computed: each barony's cells (bucketed by <c>cell.Culture</c>) and
/// that barony's already-decided <see cref="Ck3Terrain"/>. Runs <b>after</b> baronies and
/// terrain exist. Wasteland cells (cells with no barony) are skipped.
/// </summary>
public static class CultureTerrainProfiler
{
    public static Dictionary<int, CultureContext> Compute(Map map)
    {
        var terrainCounts = new Dictionary<int, Dictionary<Ck3Terrain, int>>();
        var cellCounts = new Dictionary<int, int>();
        var coastalCounts = new Dictionary<int, int>();

        var cells = map.Cells;

        if (map.Baronies is not null)
        {
            foreach (var barony in map.Baronies)
            {
                var terrain = barony.Ck3Terrain;
                foreach (var cell in barony.Cells)
                {
                    int culture = cell.Culture;
                    if (!map.Cultures.ContainsKey(culture)) continue; // skip sentinel / removed cultures

                    cellCounts[culture] = cellCounts.GetValueOrDefault(culture, 0) + 1;

                    if (!terrainCounts.TryGetValue(culture, out var perTerrain))
                        terrainCounts[culture] = perTerrain = new Dictionary<Ck3Terrain, int>();
                    perTerrain[terrain] = perTerrain.GetValueOrDefault(terrain, 0) + 1;

                    if (IsCoastal(cell, cells))
                        coastalCounts[culture] = coastalCounts.GetValueOrDefault(culture, 0) + 1;
                }
            }
        }

        var result = new Dictionary<int, CultureContext>();
        foreach (var (culture, count) in cellCounts)
        {
            var perTerrain = terrainCounts[culture];
            var fraction = perTerrain.ToDictionary(kv => kv.Key, kv => (float)kv.Value / count);
            float coastal = count > 0 ? (float)coastalCounts.GetValueOrDefault(culture, 0) / count : 0f;
            result[culture] = new CultureContext(hasLand: count > 0, cellCount: count, fraction, coastal);
        }

        // Cultures with no land at all (zero baronies/cells) still need an entry so the assigner
        // can look them up — HasLand=false makes their terrain gates all pass (uniform fallback).
        foreach (var culture in map.Cultures.Keys)
            if (!result.ContainsKey(culture))
                result[culture] = new CultureContext(hasLand: false, cellCount: 0,
                    new Dictionary<Ck3Terrain, float>(), 0f);

        LogProfiles(map, result);
        return result;
    }

    // A land cell is coastal if any neighbour cell is sea (not dry land).
    private static bool IsCoastal(Cell cell, IReadOnlyDictionary<int, Cell>? cells)
    {
        if (cell.Neighbors is null || cells is null) return false;
        foreach (var nid in cell.Neighbors)
        {
            if (!cells.TryGetValue(nid, out var n) || n is null) continue;
            if (!Cell.IsDryLand(n.Type)) return true;
        }
        return false;
    }

    // Parseable, one line per culture (feedback_debug_outputs_parseable):
    //   cultureId,name,cells,coastal%,topTerrain,topTerrain%
    private static void LogProfiles(Map map, Dictionary<int, CultureContext> profiles)
    {
        var sb = new System.Text.StringBuilder(
            "Culture terrain profiles (cultureId,name,cells,coastal%,topTerrain,topTerrain%):");
        foreach (var (id, ctx) in profiles.OrderBy(kv => kv.Key))
        {
            string name = map.Cultures.TryGetValue(id, out var c) ? c.Name : "?";
            string topTerrain = "none";
            float topFrac = 0f;
            if (ctx.TerrainFraction.Count > 0)
            {
                var top = ctx.TerrainFraction.OrderByDescending(kv => kv.Value).First();
                topTerrain = top.Key.ToCk3String();
                topFrac = top.Value;
            }
            sb.Append($"\n{id},{name},{ctx.CellCount},{ctx.CoastalFraction * 100f:F1},{topTerrain},{topFrac * 100f:F1}");
        }
        Logger.Info(sb.ToString());
    }
}
