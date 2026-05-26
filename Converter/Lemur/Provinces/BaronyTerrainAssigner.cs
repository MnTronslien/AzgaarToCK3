using Converter.Lemur.Entities;
using Converter.Lemur.Splats;

namespace Converter.Lemur.Provinces;

public static class BaronyTerrainAssigner
{
    public static void Assign(Map map)
    {
        if (map.Baronies is null || map.Baronies.Count == 0) return;
        if (map.Cells is null) return;

        var cellIsRiverAdjacent = ComputeRiverAdjacency(map.Cells, map.Rivers);

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
            return new BaronyContext(AzgaarBiome.None, new Dictionary<AzgaarBiome, float>(), Array.Empty<float>(), false, 0f, 0f, 0);

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

        var cellRoughnesses = cells.Select(c => c.Roughness).ToArray();

        bool riverAdjacent = cells.Any(c => cellIsRiverAdjacent.GetValueOrDefault(c.Id, false));

        float population = barony.burg?.Population ?? 0f;
        float popDensity = cells.Count > 0 ? population / cells.Count : 0f;

        return new BaronyContext(
            dominantBiome:     dominantBiome,
            biomeFraction:     biomeFraction,
            cellRoughnesses:   cellRoughnesses,
            riverAdjacent:     riverAdjacent,
            popDensity:        popDensity,
            population:        population,
            cellCount:         cells.Count);
    }

    // First-declared wins on equal scores (registry order breaks ties).
    private static Ck3Terrain PickWinner(in BaronyContext ctx)
    {
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

    // Reads map.Rivers.CellIds directly rather than Cell.IsRiverCell — the latter is only
    // set by MajorRiverInserter for rivers above MajorRiverThreshold, so on minor-rivers-only
    // maps the flag is always false and Oasis/Floodplains would silently never see a river.
    private static Dictionary<int, bool> ComputeRiverAdjacency(
        IReadOnlyDictionary<int, Cell> cells,
        IReadOnlyList<River>? rivers)
    {
        var onRiver = new HashSet<int>();
        if (rivers is not null)
            foreach (var r in rivers)
                foreach (var cellId in r.CellIds)
                    onRiver.Add(cellId);

        var adj = new Dictionary<int, bool>(cells.Count);
        foreach (var (id, cell) in cells)
        {
            if (cell.Neighbors is null) { adj[id] = false; continue; }
            bool any = false;
            foreach (var nid in cell.Neighbors)
            {
                if (onRiver.Contains(nid)) { any = true; break; }
                if (!cells.TryGetValue(nid, out var n) || n is null) continue;
                if (n.IsRiverCell) { any = true; break; }
                if (n.Type == Cell.FeatureType.freshwater) { any = true; break; }
                if (n.Type == Cell.FeatureType.lake) { any = true; break; }
            }
            adj[id] = any;
        }
        return adj;
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
