namespace Converter.Lemur;

using Converter.Lemur.Entities;

/// <summary>
/// Resolved suzerain/vassal structure from Azgaar's per-state diplomacy table.
/// <see cref="Roots"/> are the top suzerains (no suzerain above them) that have a surviving kingdom;
/// <see cref="JuniorsByRoot"/> maps each root's state id to the transitive set of junior state ids that
/// answer to it (chains collapsed: a→b→c ⟹ c is a direct junior of root a). Consumed by
/// <c>MergeTinyKingdoms</c> (protect root kingdoms) and <c>EmpireDeFactoBuilder</c> (wire the empire tier).
/// </summary>
public record DiplomacyResult(
    IReadOnlyList<int> Roots,
    IReadOnlyDictionary<int, IReadOnlyList<int>> JuniorsByRoot);

/// <summary>
/// Parses <c>states[i].diplomacy</c> into a directed suzerain graph and collapses chains to roots.
/// Pure and deterministic: roots and junior lists are ordered, ties broken by (duchy count desc, state id asc).
/// Must run after <c>GenerateKingdoms</c> (needs kingdoms to know which states survived and their sizes)
/// and before <c>MergeTinyKingdoms</c> (which protects the root kingdoms).
/// </summary>
public static class DiplomacyResolver
{
    public static DiplomacyResult Resolve(Map map)
    {
        var states = map.JsonMap.pack.states;
        var hasKingdom = map.Kingdoms.Select(k => k.Id).ToHashSet();
        int DuchyCount(int stateId) => map.Kingdoms.FirstOrDefault(k => k.Id == stateId)?.Duchies.Count ?? 0;

        // Directed edges senior → junior, from each "Suzerain" entry. i is the senior (see semantics note).
        var juniorsBySenior = new Dictionary<int, SortedSet<int>>();
        var hasSuzerainAbove = new HashSet<int>();
        foreach (var s in states)
        {
            if (s.i < 1 || s.diplomacy == null) continue;
            for (int j = 1; j < s.diplomacy.Length && j < states.Length; j++)
            {
                if (j == s.i || s.diplomacy[j] != "Suzerain") continue;
                if (!juniorsBySenior.TryGetValue(s.i, out var set))
                    juniorsBySenior[s.i] = set = new SortedSet<int>();
                set.Add(j);
                hasSuzerainAbove.Add(j);
            }
        }

        // Roots: seniors with no suzerain above them and a surviving kingdom. Larger first (then id) so the
        // tie-break "larger root wins" falls out of first-claim ordering below.
        var roots = juniorsBySenior.Keys
            .Where(i => !hasSuzerainAbove.Contains(i) && hasKingdom.Contains(i))
            .OrderByDescending(DuchyCount).ThenBy(i => i)
            .ToList();

        // Transitive juniors per root (BFS over edges). First claim wins → larger root keeps a shared junior.
        var claimed = new HashSet<int>();
        var juniorsByRoot = new Dictionary<int, IReadOnlyList<int>>();
        foreach (var root in roots)
        {
            var collected = new List<int>();
            var queue = new Queue<int>();
            var visited = new HashSet<int> { root };
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!juniorsBySenior.TryGetValue(cur, out var juniors)) continue;
                foreach (var j in juniors)
                {
                    if (!visited.Add(j)) continue;          // cycle / re-visit guard
                    queue.Enqueue(j);                        // follow chains transitively
                    if (claimed.Add(j)) collected.Add(j);    // skip if a larger root already took it
                }
            }
            juniorsByRoot[root] = collected;
        }

        if (roots.Count > 0)
            Logger.Info($"Diplomacy: {roots.Count} suzerain root(s), " +
                        $"{juniorsByRoot.Values.Sum(v => v.Count)} junior(s) total.");
        return new DiplomacyResult(roots, juniorsByRoot);
    }
}
