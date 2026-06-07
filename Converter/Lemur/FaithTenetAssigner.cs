using Converter.Lemur.Entities;
using Converter.Lemur.Fields;
using static Converter.Lemur.TenetData;

namespace Converter.Lemur;

/// <summary>
/// Assigns each faith its tenets via a weighted, topological draw (parent → child), deferred out of
/// <c>FaithManager.Build</c> so it can read the canonical <c>Barony.Ck3Terrain</c> the pipeline
/// already computed (exposed here as a per-faith <see cref="FaithContext"/>).
///
/// <para><b>Slot-fill.</b> A faith has <c>TenetCount</c> slots. One procedure fills the <i>open</i>
/// slots; a root opens all of them, a child copies its parent's tenets and opens a slot only on a
/// per-slot mutation roll. Each open slot is filled by a roulette over the candidate weights, where
/// every candidate's weight is the implicit-conflict wrap of its eval:</para>
/// <code>weight(E) = blocks(E.Name, faith.Tenets) ? 0 : E.Eval(in ctx)</code>
/// <para>so a candidate conflicting with an already-picked tenet is zeroed before its eval matters.
/// Each pick is appended to <c>faith.Tenets</c> so later slots in the same pass see it.</para>
/// </summary>
public static class FaithTenetAssigner
{
    private static readonly TenetEntry[] Pool = TenetData.All;

    public static void Assign(Map map, IReadOnlyDictionary<int, FaithContext> terrain, int seed)
    {
        Logger.Section("Assigning faith tenets");

        int tenetCount = Converter.Settings.Instance.TenetCount;
        float mutationRate = Converter.Settings.Instance.DoctrineMutationRate;

        // Topological walk over Faith.Parent: a faith is processed only after its parent.
        var faiths = map.Faiths.Values.ToList();
        var childrenOf = faiths
            .Where(f => f.Parent != null)
            .GroupBy(f => f.Parent!.AzgaarId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var visited = new HashSet<int>();
        var queue = new Queue<Faith>(faiths.Where(f => f.Parent == null));

        while (queue.Count > 0)
        {
            var faith = queue.Dequeue();
            if (!visited.Add(faith.AzgaarId)) continue;

            AssignOne(faith, terrain, seed, tenetCount, mutationRate);

            if (childrenOf.TryGetValue(faith.AzgaarId, out var children))
                foreach (var child in children)
                    if (!visited.Contains(child.AzgaarId))
                        queue.Enqueue(child);
        }

        // Any unvisited faiths (cycles / orphans) get fresh root-style picks.
        foreach (var faith in faiths)
            if (!visited.Contains(faith.AzgaarId))
                AssignOne(faith, terrain, seed, tenetCount, mutationRate);

        LogSummary(map);
    }

    private static void AssignOne(
        Faith faith, IReadOnlyDictionary<int, FaithContext> terrain,
        int seed, int tenetCount, float mutationRate)
    {
        var ctx = terrain.TryGetValue(faith.AzgaarId, out var t)
            ? t
            : new FaithContext(faith, new Dictionary<Provinces.Ck3Terrain, float>());

        // 2-arg MixSeeds (no salt): tenet selection has no prior stream to preserve.
        var rng = new Random(Helper.MixSeeds(seed, faith.AzgaarId));

        if (faith.Parent == null)
        {
            // Root: every slot is open.
            faith.Tenets = new List<string>();
            FillOpenSlots(faith, tenetCount, in ctx, rng, excludedPerSlot: null);
        }
        else
        {
            // Child: copy parent's list, trim to count, then roll each slot for mutation.
            var kept = new List<string>(faith.Parent.Tenets);
            while (kept.Count > tenetCount) kept.RemoveAt(kept.Count - 1);

            // Slots beyond the kept list are simply open (parent had fewer than tenetCount).
            int openFromShortfall = tenetCount - kept.Count;

            // Per-slot mutation: an opened slot drops its prior occupant and must NOT re-pick it.
            var excludedPerSlot = new List<string>();
            var survivors = new List<string>();
            foreach (var occupant in kept)
            {
                if (rng.NextDouble() < mutationRate)
                    excludedPerSlot.Add(occupant); // opened slot: exclude its own prior occupant
                else
                    survivors.Add(occupant);       // slot stays filled
            }
            for (int i = 0; i < openFromShortfall; i++)
                excludedPerSlot.Add(null!);        // genuinely empty slot, no prior occupant

            faith.Tenets = survivors;
            FillOpenSlots(faith, tenetCount, in ctx, rng, excludedPerSlot);
        }
    }

    /// <summary>
    /// Fills <paramref name="faith"/> up to <paramref name="tenetCount"/> by roulette over the
    /// implicit-conflict-wrapped weights. <paramref name="excludedPerSlot"/> (one entry per open
    /// slot) names a tenet that slot may not re-pick (a mutated slot's prior occupant); a null entry
    /// = no per-slot exclusion. When null, all open slots are unconstrained (root fill).
    /// </summary>
    private static void FillOpenSlots(
        Faith faith, int tenetCount, in FaithContext ctx, Random rng, List<string>? excludedPerSlot)
    {
        var c = ctx; // snapshot: lambdas/closures cannot capture an `in` parameter
        int slot = 0;
        while (faith.Tenets.Count < tenetCount)
        {
            string? selfExclude = excludedPerSlot != null && slot < excludedPerSlot.Count
                ? excludedPerSlot[slot]
                : null;
            slot++;

            var pick = PickOne(in c, faith.Tenets, selfExclude, rng);
            if (pick == null) break; // nothing legal to add
            faith.Tenets.Add(pick);
        }
    }

    /// <summary>One roulette draw. Candidates already picked (or the per-slot self-exclusion) are out
    /// of the pool; conflict with a picked tenet zeroes a candidate; duds eval to 0. Σ==0 → uniform
    /// over the non-zero, non-blocked, non-excluded remainder.</summary>
    private static string? PickOne(in FaithContext ctx, List<string> picked, string? selfExclude, Random rng)
    {
        var c = ctx;
        var available = Pool
            .Where(e => !picked.Contains(e.Name) && e.Name != selfExclude)
            .ToList();
        if (available.Count == 0) return null;

        var weights = new double[available.Count];
        double total = 0;
        for (int i = 0; i < available.Count; i++)
        {
            double w = Blocks(available[i].Name, picked) ? 0d : available[i].Eval(in c);
            if (w < 0) w = 0;
            weights[i] = w;
            total += w;
        }

        if (total <= 0)
        {
            // Uniform fallback over candidates that are neither a dud (eval 0) nor blocked.
            var legal = new List<int>();
            for (int i = 0; i < available.Count; i++)
                if (!Blocks(available[i].Name, picked) && available[i].Eval(in c) > 0)
                    legal.Add(i);
            if (legal.Count > 0) return available[legal[rng.Next(legal.Count)]].Name;
            return null; // nothing legal — leave the slot unfilled rather than pick a dud/conflict
        }

        double roll = rng.NextDouble() * total;
        double acc = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            acc += weights[i];
            if (roll < acc) return available[i].Name;
        }
        return available[^1].Name;
    }

    /// <summary>Implicit conflict: <paramref name="self"/> is blocked if any already-picked tenet is
    /// in its central symmetric <see cref="ConflictGraph"/> set.</summary>
    private static bool Blocks(string self, List<string> picked)
    {
        if (!ConflictGraph.TryGetValue(self, out var set) || set.Count == 0) return false;
        foreach (var p in picked)
            if (set.Contains(p)) return true;
        return false;
    }

    private static void LogSummary(Map map)
    {
        var sb = new System.Text.StringBuilder(
            $"Assigned tenets to {map.Faiths.Count} faiths (faithId,name,type,tenets):");
        foreach (var f in map.Faiths.Values.OrderBy(f => f.AzgaarId))
            sb.Append($"\n- {f.Name} (id {f.AzgaarId}): type={f.Type}, tenets=[{string.Join(", ", f.Tenets)}]");
        Logger.Info(sb.ToString());
    }
}
