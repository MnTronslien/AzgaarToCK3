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
    /// path) is ≤ <paramref name="maxCost"/>, traversing only passable cells. Bounded A* — the priority
    /// is path-cost-so-far plus the straight-line distance to the target (an admissible heuristic), so
    /// the search beelines toward the target and the common "reachable by a short walk" case resolves
    /// almost immediately instead of expanding a full maxCost-radius disk.
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

        var (tx, ty) = centroid(toId);
        double Heuristic(int id) { var (x, y) = centroid(id); return Math.Sqrt((x - tx) * (x - tx) + (y - ty) * (y - ty)); }

        var best = new Dictionary<int, double> { [fromId] = 0 };
        var pq = new PriorityQueue<int, double>(); // priority = g + h
        pq.Enqueue(fromId, Heuristic(fromId));
        while (pq.TryDequeue(out int curId, out double curPriority))
        {
            // A* lower bound: once the cheapest f = g+h exceeds the budget, no path ≤ maxCost remains
            // (h is admissible, so g_to ≥ f_here for everything still queued).
            if (curPriority > maxCost) return false;
            double g = best[curId];
            if (curPriority > g + Heuristic(curId) + 1e-6) continue; // stale: a cheaper path to curId was queued later
            var (cx, cy) = centroid(curId);
            foreach (var nId in cells[curId].Neighbors)
            {
                if (!cells.TryGetValue(nId, out var n) || !passable(n)) continue;
                var (nx, ny) = centroid(nId);
                double ng = g + Math.Sqrt((nx - cx) * (nx - cx) + (ny - cy) * (ny - cy));
                if (ng > maxCost) continue;
                if (ng < best.GetValueOrDefault(nId, double.MaxValue))
                {
                    best[nId] = ng;
                    if (nId == toId) return true;
                    pq.Enqueue(nId, ng + Heuristic(nId));
                }
            }
        }
        return false;
    }
}
