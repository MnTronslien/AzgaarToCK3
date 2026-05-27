using Converter.Lemur.Entities;

namespace Converter.Lemur.Fields;

/// <summary>
/// Derives per-religion and per-culture land-cell counts directly from the GeoJSON cell graph.
///
/// Why this exists: Azgaar's JSON exports may omit the optional `cells` / `rural` / `urban`
/// fields on religion and culture entries (e.g. exports made without opening the Statistics
/// pane in the Azgaar editor first). Reading those JSON fields gives a false zero, which
/// historically caused us to filter out religions that the GeoJSON still tags onto land
/// cells — producing orphan faith references in province history. Counting cells from the
/// GeoJSON is the ground truth and is invariant across Azgaar export variants.
///
/// Sea cells are excluded — only <see cref="Cell.IsDryLand"/> cells contribute. River
/// cells (synthetic, <see cref="Cell.IsRiverCell"/>) are also excluded since they don't
/// represent populated territory.
/// </summary>
public static class CellDistribution
{
    /// <summary>
    /// Counts derived from the GeoJSON cell graph. Each dict maps an Azgaar id to a land-cell
    /// count. Keys with zero land cells are omitted (callers should treat a missing key as 0).
    /// </summary>
    public readonly record struct Result(
        Dictionary<int, int> ReligionCellCounts,
        Dictionary<int, int> CultureCellCounts);

    /// <summary>
    /// Computes both distributions in parallel via <see cref="Task.WhenAll"/>.
    /// On large maps (100k cells) the two scans run on independent threads; on small maps
    /// the task scheduling overhead is negligible.
    /// </summary>
    public static async Task<Result> ComputeAsync(IReadOnlyDictionary<int, Cell> cells)
    {
        var religionTask = Task.Run(() => CountBy(cells, c => c.Religion));
        var cultureTask  = Task.Run(() => CountBy(cells, c => c.Culture));
        await Task.WhenAll(religionTask, cultureTask);
        return new Result(religionTask.Result, cultureTask.Result);
    }

    private static Dictionary<int, int> CountBy(
        IReadOnlyDictionary<int, Cell> cells,
        Func<Cell, int> selector)
    {
        var counts = new Dictionary<int, int>();
        foreach (var cell in cells.Values)
        {
            if (!Cell.IsDryLand(cell.Type)) continue;
            if (cell.IsRiverCell) continue;
            int key = selector(cell);
            counts.TryGetValue(key, out int n);
            counts[key] = n + 1;
        }
        return counts;
    }
}
