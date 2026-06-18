using Converter.Lemur.Entities;

namespace Converter.Lemur.Straits;

/// <summary>
/// Generalized traversal over the Azgaar cell-neighbour graph: connected components and bounded
/// reachability. The single primitive behind landmass labelling, water-body labelling, and the
/// rule-1 self-separation test (PLAN_straits.md). BFS, not A* — there is no weighted shortest
/// path here, only connectivity and hop-bounded reachability.
/// </summary>
internal static class CellGraph
{
    /// <summary>
    /// Label connected components over cells satisfying <paramref name="passable"/>.
    /// Returns cellId → componentId for passable cells only. Seeds iterate in id order so
    /// component ids are deterministic across runs.
    /// </summary>
    public static Dictionary<int, int> ConnectedComponents(
        IReadOnlyDictionary<int, Cell> cells, Func<Cell, bool> passable)
    {
        var label = new Dictionary<int, int>();
        var queue = new Queue<int>();
        int next = 0;

        foreach (var seedId in cells.Keys.OrderBy(x => x))
        {
            if (label.ContainsKey(seedId) || !passable(cells[seedId])) continue;

            int comp = next++;
            label[seedId] = comp;
            queue.Enqueue(seedId);
            while (queue.Count > 0)
            {
                var cur = cells[queue.Dequeue()];
                foreach (var nId in cur.Neighbors)
                {
                    if (label.ContainsKey(nId)) continue;
                    if (!cells.TryGetValue(nId, out var n) || !passable(n)) continue;
                    label[nId] = comp;
                    queue.Enqueue(nId);
                }
            }
        }
        return label;
    }

    /// <summary>
    /// True if <paramref name="toId"/> is reachable from <paramref name="fromId"/> by an overland path
    /// whose accumulated cost (sum of <paramref name="centroid"/>-to-centroid pixel distances along the
    /// path) is ≤ <paramref name="maxCost"/>, traversing only passable cells. Bounded Dijkstra.
    ///
    /// Used by rule 1: two cells joined by a short overland detour are too close to warrant a strait.
    /// Cost is in CK3 image pixels (not hops) so the threshold is independent of Azgaar cell density —
    /// a 10k-cell and a 100k-cell map at the same resolution behave the same.
    /// </summary>
    public static bool ReachableWithinCost(
        IReadOnlyDictionary<int, Cell> cells, int fromId, int toId, double maxCost,
        Func<int, (double x, double y)> centroid, Func<Cell, bool> passable)
    {
        if (maxCost < 0) return false;
        if (fromId == toId) return true;
        if (!cells.TryGetValue(fromId, out var from) || !passable(from)) return false;
        if (!cells.TryGetValue(toId, out var to) || !passable(to)) return false;

        var best = new Dictionary<int, double> { [fromId] = 0 };
        var pq = new PriorityQueue<int, double>();
        pq.Enqueue(fromId, 0);
        while (pq.TryDequeue(out int curId, out double curCost))
        {
            if (curCost > best.GetValueOrDefault(curId, double.MaxValue)) continue; // stale entry
            if (curCost > maxCost) return false; // cheapest frontier already over budget — to unreachable in budget
            var (cx, cy) = centroid(curId);
            foreach (var nId in cells[curId].Neighbors)
            {
                if (!cells.TryGetValue(nId, out var n) || !passable(n)) continue;
                var (nx, ny) = centroid(nId);
                double nc = curCost + Math.Sqrt((nx - cx) * (nx - cx) + (ny - cy) * (ny - cy));
                if (nc > maxCost) continue;
                if (nc < best.GetValueOrDefault(nId, double.MaxValue))
                {
                    best[nId] = nc;
                    if (nId == toId) return true;
                    pq.Enqueue(nId, nc);
                }
            }
        }
        return false;
    }
}
