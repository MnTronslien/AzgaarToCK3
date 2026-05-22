using Converter.Lemur.Entities;

namespace Converter.Lemur.Fields;

// Per-cell surface roughness, p95-normalised, in [0..1]. Zero over sea cells.
//
// Math (carried over verbatim from HeightmapAlgorithm.Generate lines 100-116):
//   1. For each land cell, rawDiff = mean(|cell.GeoHeight - neighbour.GeoHeight|) over land neighbours.
//   2. Find the 95th percentile of rawDiff across all land cells.
//   3. autoNorm = p95 * roughnessNorm (RoughnessNorm is a user-tunable scale factor, default 1.0).
//   4. Normalise: each cell's roughness = clamp(rawDiff / autoNorm, 0, 1).
//
// Both HeightmapAlgorithm (terrain-node displacement input) and BaronyTerrainAssigner
// (province-terrain decision) consume this — single source of truth at cell resolution.
// Mirror of Lemur/Splats/SteepnessField.cs but operating on coarse cell data rather than
// rendered pixel data; cheap enough (a few thousand cells) to run unconditionally.
public static class CellRoughnessField
{
    /// <summary>
    /// Compute p95-normalised roughness for every land cell. Sea cells are omitted from the result.
    /// </summary>
    /// <param name="cells">All cells keyed by id.</param>
    /// <param name="roughnessNorm">Multiplier on the p95 normaliser. Default 1.0 (no extra scaling).</param>
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
