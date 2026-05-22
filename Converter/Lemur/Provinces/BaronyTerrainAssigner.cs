using Converter.Lemur.Entities;
using Converter.Lemur.Splats;

namespace Converter.Lemur.Provinces;

/// <summary>
/// Pipeline step (not a writer) — computes <see cref="Barony.Ck3Terrain"/> for every barony
/// by evaluating <see cref="TerrainRegistry.All"/> against a <see cref="BaronyContext"/>
/// built per barony and picking the highest-scoring candidate. Runs after baronies + cells +
/// roughness are settled, before any writer.
/// </summary>
public static class BaronyTerrainAssigner
{
    public static void Assign(Map map)
    {
        if (map.Baronies is null || map.Baronies.Count == 0) return;
        if (map.Cells is null) return;

        // Pre-compute "is this cell river-adjacent" once per cell so the per-barony pass
        // doesn't repeat the neighbour walk per barony cell. River-adjacent = at least one
        // neighbour is a synthetic major-river cell OR a freshwater/lake feature cell. Lakes
        // count so an Oasis-style "fertile patch near water" rule fires near inland water,
        // not only along major rivers.
        var cellIsRiverAdjacent = ComputeRiverAdjacency(map.Cells);

        var histogram = new Dictionary<Ck3Terrain, int>();

        foreach (var barony in map.Baronies)
        {
            var ctx = BuildContext(barony, cellIsRiverAdjacent);
            var winner = PickWinner(in ctx);
            barony.Ck3Terrain = winner;
            histogram[winner] = histogram.GetValueOrDefault(winner, 0) + 1;
        }

        LogHistogram(histogram, map.Baronies.Count);
    }

    private static BaronyContext BuildContext(Barony barony, Dictionary<int, bool> cellIsRiverAdjacent)
    {
        var cells = barony.Cells;
        if (cells.Count == 0)
            return new BaronyContext(AzgaarBiome.None, new Dictionary<AzgaarBiome, float>(), 0f, false, 0f, 0f, 0);

        // Biome fractions and dominant biome by cell count.
        var biomeCounts = new Dictionary<AzgaarBiome, int>();
        foreach (var cell in cells)
        {
            var b = (AzgaarBiome)cell.Biome;
            biomeCounts[b] = biomeCounts.GetValueOrDefault(b, 0) + 1;
        }
        var biomeFraction = biomeCounts.ToDictionary(
            kv => kv.Key,
            kv => (float)kv.Value / cells.Count);
        var dominantBiome = biomeCounts.OrderByDescending(kv => kv.Value).First().Key;

        // p75 roughness across cells. A single rough cell in an otherwise flat barony shouldn't
        // tip the whole barony into Hills/Mountains; using the upper-quartile lets a clear
        // majority of rough cells drive the decision while the mean still respects outliers.
        float roughnessP75 = Percentile(cells.Select(c => c.Roughness).ToList(), 0.75);

        bool riverAdjacent = cells.Any(c => cellIsRiverAdjacent.GetValueOrDefault(c.Id, false));

        float population = barony.burg?.Population ?? 0f;
        float popDensity = cells.Count > 0 ? population / cells.Count : 0f;

        return new BaronyContext(
            dominantBiome:  dominantBiome,
            biomeFraction:  biomeFraction,
            roughness:      roughnessP75,
            riverAdjacent:  riverAdjacent,
            popDensity:     popDensity,
            population:     population,
            cellCount:      cells.Count);
    }

    private static Ck3Terrain PickWinner(in BaronyContext ctx)
    {
        // Stable argmax — registry order breaks ties (first-declared wins on equal scores).
        Ck3Terrain winner = Ck3Terrain.Plains;
        float bestScore = float.NegativeInfinity;
        foreach (var candidate in TerrainRegistry.All)
        {
            float s = candidate.Score(in ctx);
            if (s > bestScore)
            {
                bestScore = s;
                winner = candidate.Type;
            }
        }
        return winner;
    }

    private static Dictionary<int, bool> ComputeRiverAdjacency(IReadOnlyDictionary<int, Cell> cells)
    {
        var adj = new Dictionary<int, bool>(cells.Count);
        foreach (var (id, cell) in cells)
        {
            if (cell.Neighbors is null) { adj[id] = false; continue; }
            bool any = false;
            foreach (var nid in cell.Neighbors)
            {
                if (!cells.TryGetValue(nid, out var n) || n is null) continue;
                if (n.IsRiverCell) { any = true; break; }
                if (n.Type == Cell.FeatureType.freshwater) { any = true; break; }
                if (n.Type == Cell.FeatureType.lake) { any = true; break; }
            }
            adj[id] = any;
        }
        return adj;
    }

    private static float Percentile(List<float> values, double p)
    {
        if (values.Count == 0) return 0f;
        values.Sort();
        int idx = (int)Math.Clamp(Math.Floor(p * (values.Count - 1)), 0, values.Count - 1);
        return values[idx];
    }

    private static void LogHistogram(Dictionary<Ck3Terrain, int> histogram, int total)
    {
        Logger.Info($"BaronyTerrainAssigner — assigned terrain for {total} baronies:");
        foreach (var (terrain, count) in histogram.OrderByDescending(kv => kv.Value))
        {
            float pct = 100f * count / total;
            Logger.Info($"  {terrain.ToCk3String(),-18} {count,5}  ({pct,5:F1}%)");
        }
    }
}
