using Converter.Lemur.Entities;
using Converter.Lemur.Provinces;

namespace Converter.Lemur.Fields;

/// <summary>
/// Builds a <see cref="FaithContext"/> per Azgaar religion from the canonical land the pipeline
/// already computed: each barony's cells (bucketed by <c>cell.Religion</c>) and that barony's
/// already-decided <see cref="Ck3Terrain"/>. Runs <b>after</b> baronies and terrain exist.
/// Wasteland cells (cells with no barony) are skipped.
///
/// <para>Produces only the cached per-faith terrain distribution; the context reads the faith's
/// type/doctrines/tenets directly from the live <see cref="Faith"/> it carries. Faiths with no land
/// (zero-cell parents kept for doctrine inheritance) still get an entry with an empty distribution
/// so the assigner can look them up.</para>
/// </summary>
public static class FaithTerrainProfiler
{
    public static Dictionary<int, FaithContext> Compute(Map map)
    {
        var terrainCounts = new Dictionary<int, Dictionary<Ck3Terrain, int>>();
        var cellCounts = new Dictionary<int, int>();

        if (map.Baronies is not null)
        {
            foreach (var barony in map.Baronies)
            {
                var terrain = barony.Ck3Terrain;
                foreach (var cell in barony.Cells)
                {
                    int faithId = cell.Religion;
                    if (!map.Faiths.ContainsKey(faithId)) continue; // skip sentinel / pruned faiths

                    cellCounts[faithId] = cellCounts.GetValueOrDefault(faithId, 0) + 1;

                    if (!terrainCounts.TryGetValue(faithId, out var perTerrain))
                        terrainCounts[faithId] = perTerrain = new Dictionary<Ck3Terrain, int>();
                    perTerrain[terrain] = perTerrain.GetValueOrDefault(terrain, 0) + 1;
                }
            }
        }

        var result = new Dictionary<int, FaithContext>();
        foreach (var (faithId, count) in cellCounts)
        {
            var faith = map.Faiths[faithId];
            var perTerrain = terrainCounts[faithId];
            var fraction = perTerrain.ToDictionary(kv => kv.Key, kv => (float)kv.Value / count);
            result[faithId] = new FaithContext(faith, fraction);
        }

        // Faiths with no land at all still need an entry so the assigner can look them up.
        var empty = new Dictionary<Ck3Terrain, float>();
        foreach (var (faithId, faith) in map.Faiths)
            if (!result.ContainsKey(faithId))
                result[faithId] = new FaithContext(faith, empty);

        LogProfiles(map, result, cellCounts);
        return result;
    }

    // Parseable, one line per faith (feedback_debug_outputs_parseable):
    //   faithId,name,type,cells,topTerrain,topTerrain%,fullBreakdown[terrain:pct|...]
    private static void LogProfiles(Map map, Dictionary<int, FaithContext> profiles, Dictionary<int, int> cellCounts)
    {
        var sb = new System.Text.StringBuilder(
            "Faith terrain profiles (faithId,name,type,cells,topTerrain,topTerrain%,fullBreakdown[terrain:pct|...]):");
        foreach (var (id, ctx) in profiles.OrderBy(kv => kv.Key))
        {
            var faith = map.Faiths.TryGetValue(id, out var f) ? f : null;
            string name = faith?.Name ?? "?";
            string type = string.IsNullOrEmpty(faith?.Type) ? "?" : faith!.Type;
            int cells = cellCounts.GetValueOrDefault(id, 0);
            string topTerrain = "none";
            float topFrac = 0f;
            string breakdown = "";
            if (ctx.TerrainFraction.Count > 0)
            {
                var ordered = ctx.TerrainFraction.OrderByDescending(kv => kv.Value).ToList();
                topTerrain = ordered[0].Key.ToCk3String();
                topFrac = ordered[0].Value;
                breakdown = string.Join("|", ordered.Select(kv => $"{kv.Key.ToCk3String()}:{kv.Value * 100f:F1}"));
            }
            sb.Append($"\n{id},{name},{type},{cells},{topTerrain},{topFrac * 100f:F1},{breakdown}");
        }
        Logger.Info(sb.ToString());
    }
}
