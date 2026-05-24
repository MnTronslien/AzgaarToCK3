using Converter.Lemur.Entities;

namespace Converter.Lemur.Fields;

// Per-cell roughness, p95-normalised to [0..1], computed from neighbour GeoHeight deltas.
// Shared by HeightmapAlgorithm (displacement input) and BaronyTerrainAssigner (terrain pick).
public static class CellRoughnessField
{
    public static Dictionary<int, float> Compute(
        IReadOnlyDictionary<int, Cell> cells,
        float roughnessNorm = 1f)
    {
        var rawDiffs = new Dictionary<int, float>(cells.Count);
        foreach (var cell in cells.Values)
        {
            if (!Cell.IsDryLand(cell.Type)) continue;
            var nh = cell.Neighbors
                .Select(id => cells.TryGetValue(id, out var n) ? n : null)
                .Where(n => n != null && Cell.IsDryLand(n!.Type))
                .Select(n => n!.GeoHeight).ToList();
            rawDiffs[cell.Id] = nh.Count > 0
                ? (float)nh.Average(h => Math.Abs(cell.GeoHeight - h))
                : 0f;
        }

        var sortedDiffs = rawDiffs.Values.OrderBy(x => x).ToList();
        float p95val = sortedDiffs.Count > 0 ? sortedDiffs[(int)(sortedDiffs.Count * 0.95f)] : 1f;
        float autoNorm = (p95val > 0f ? p95val : 1f) * roughnessNorm;
        var roughness = rawDiffs.ToDictionary(kv => kv.Key, kv => Math.Clamp(kv.Value / autoNorm, 0f, 1f));

        if (roughness.Count > 0)
            Logger.Info($"CellRoughnessField — p95raw={p95val:F1} autoNorm={autoNorm:F1} avg={roughness.Values.Average():F3}");

        return roughness;
    }
}
