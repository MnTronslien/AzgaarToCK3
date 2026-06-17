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
    /// True if <paramref name="toId"/> is reachable from <paramref name="fromId"/> within
    /// <paramref name="maxHops"/> edges, traversing only passable cells (endpoints included).
    /// Used by rule 1: two cells reachable overland within the bound are too close to warrant a strait.
    /// </summary>
    public static bool ReachableWithin(
        IReadOnlyDictionary<int, Cell> cells, int fromId, int toId, int maxHops, Func<Cell, bool> passable)
    {
        if (maxHops < 0) return false;
        if (fromId == toId) return true;
        if (!cells.TryGetValue(fromId, out var from) || !passable(from)) return false;
        if (!cells.TryGetValue(toId, out var to) || !passable(to)) return false;

        var depth = new Dictionary<int, int> { [fromId] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(fromId);
        while (queue.Count > 0)
        {
            int curId = queue.Dequeue();
            int d = depth[curId];
            if (d >= maxHops) continue;
            foreach (var nId in cells[curId].Neighbors)
            {
                if (nId == toId) return true;
                if (depth.ContainsKey(nId)) continue;
                if (!cells.TryGetValue(nId, out var n) || !passable(n)) continue;
                depth[nId] = d + 1;
                queue.Enqueue(nId);
            }
        }
        return false;
    }
}
