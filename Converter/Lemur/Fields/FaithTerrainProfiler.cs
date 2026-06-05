using Converter.Lemur.Entities;
using Converter.Lemur.Provinces;

namespace Converter.Lemur.Fields;

/// <summary>
/// Builds a <see cref="FaithContext"/> per Azgaar religion from the canonical land the pipeline
/// already computed: each barony's cells (bucketed by <c>cell.Religion</c>) and that barony's
/// already-decided <see cref="Ck3Terrain"/>. Faith twin of <see cref="CultureTerrainProfiler"/>.
/// Runs <b>after</b> baronies and terrain exist. Wasteland cells (cells with no barony) are skipped.
///
/// <para>Keyed by Azgaar religion id. Cells reference only surviving faiths (zero-cell faiths were
/// pruned in <c>FaithManager.Build</c>, unless kept for doctrine inheritance — those get a
/// HasLand=false entry so their terrain gates all pass, matching today's uniform behaviour).</para>
/// </summary>
public static class FaithTerrainProfiler
{
    public static Dictionary<int, FaithContext> Compute(Map map)
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
                    int faithId = cell.Religion;
                    if (!map.Faiths.ContainsKey(faithId)) continue; // skip sentinel / pruned faiths

                    cellCounts[faithId] = cellCounts.GetValueOrDefault(faithId, 0) + 1;

                    if (!terrainCounts.TryGetValue(faithId, out var perTerrain))
                        terrainCounts[faithId] = perTerrain = new Dictionary<Ck3Terrain, int>();
                    perTerrain[terrain] = perTerrain.GetValueOrDefault(terrain, 0) + 1;

                    if (IsCoastal(cell, cells))
                        coastalCounts[faithId] = coastalCounts.GetValueOrDefault(faithId, 0) + 1;
                }
            }
        }

        var result = new Dictionary<int, FaithContext>();
        foreach (var (faithId, count) in cellCounts)
        {
            var faith = map.Faiths[faithId];
            var perTerrain = terrainCounts[faithId];
            var fraction = perTerrain.ToDictionary(kv => kv.Key, kv => (float)kv.Value / count);
            float coastal = count > 0 ? (float)coastalCounts.GetValueOrDefault(faithId, 0) / count : 0f;
            result[faithId] = new FaithContext(
                hasLand: count > 0, cellCount: count, fraction, coastal,
                faithType: faith.Type, isUnreformed: faith.IsUnreformed, doctrines: faith.Doctrines);
        }

        // Faiths with no land at all (zero-cell parents kept for doctrine inheritance) still need an
        // entry so the assigner can look them up — HasLand=false makes their terrain gates all pass.
        foreach (var (faithId, faith) in map.Faiths)
            if (!result.ContainsKey(faithId))
                result[faithId] = new FaithContext(
                    hasLand: false, cellCount: 0, new Dictionary<Ck3Terrain, float>(), 0f,
                    faithType: faith.Type, isUnreformed: faith.IsUnreformed, doctrines: faith.Doctrines);

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

    // Parseable, one line per faith (feedback_debug_outputs_parseable):
    //   faithId,name,type,cells,coastal%,topTerrain,topTerrain%,fullBreakdown[terrain:pct|...]
    private static void LogProfiles(Map map, Dictionary<int, FaithContext> profiles)
    {
        var sb = new System.Text.StringBuilder(
            "Faith terrain profiles (faithId,name,type,cells,coastal%,topTerrain,topTerrain%,fullBreakdown[terrain:pct|...]):");
        foreach (var (id, ctx) in profiles.OrderBy(kv => kv.Key))
        {
            string name = map.Faiths.TryGetValue(id, out var f) ? f.Name : "?";
            string type = string.IsNullOrEmpty(ctx.FaithType) ? "?" : ctx.FaithType;
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
            sb.Append($"\n{id},{name},{type},{ctx.CellCount},{ctx.CoastalFraction * 100f:F1},{topTerrain},{topFrac * 100f:F1},{breakdown}");
        }
        Logger.Info(sb.ToString());
    }
}
