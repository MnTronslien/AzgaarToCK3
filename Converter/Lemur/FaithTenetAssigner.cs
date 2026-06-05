using Converter.Lemur.Entities;
using Converter.Lemur.Fields;
using static Converter.Lemur.TenetData;

namespace Converter.Lemur;

/// <summary>
/// Assigns each faith its tenets via a weighted, topological draw (foundational → derived),
/// deferred out of <c>FaithManager.Build</c> so it can read the canonical <c>Barony.Ck3Terrain</c>
/// the pipeline already computed (exposed here as a per-faith <see cref="FaithContext"/>).
/// Faith twin of <see cref="CultureTraditionAssigner"/>.
///
/// <para>Weight of a <b>fresh</b> pick is shaped by four things, two mechanical (hard 0) and two
/// soft/authored:</para>
/// <list type="bullet">
/// <item><b>💀 Dud</b> — the six syncretic tenets are inert in a full conversion → weight 0 always.</item>
/// <item><b>🚫 Exclusivity</b> — if an already-chosen tenet is mutually exclusive (vanilla
/// <c>can_pick</c>) with the candidate, weight 0. Applied symmetrically.</item>
/// <item><b>🗺️ Terrain</b> — a HARD authored gate ({0,1}); a mismatched terrain tenet is removed.</item>
/// <item><b>🎭 FaithKind</b> — a SOFT ×<see cref="KindPenalty"/> when the faith's kind is not in
/// the tenet's favoured set. Never zeroes a survivor of the harder filters.</item>
/// </list>
///
/// <para>Inherited tenets are NOT re-gated: a derived faith copies its parent's list and only fresh
/// picks (foundational fill, mutation replacement) are gated. Faiths have a single
/// <see cref="Faith.Parent"/> today, so the derived branch covers them; no hybrid branch.</para>
/// </summary>
public static class FaithTenetAssigner
{
    /// <summary>The one tunable magnitude. Soft kind ×penalty; only re-weights survivors of the hard filters.</summary>
    private const float KindPenalty = 0.3f;

    // Inclusive pool: everything except held-out placeholders (there are none today).
    private static readonly TenetEntry[] Pool =
        TenetData.All.Where(t => !t.Special).ToArray();

    private static readonly Dictionary<string, TenetEntry> ByKey =
        Pool.ToDictionary(t => t.Key);

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

        // Any unvisited faiths (cycles / orphans) get fresh foundational-style picks.
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
            : new FaithContext(false, 0, new Dictionary<Provinces.Ck3Terrain, float>(), 0f,
                faith.Type, faith.IsUnreformed, faith.Doctrines);
        var kind = KindOf(in ctx);

        // 2-arg MixSeeds (no salt) — the documented form the faith code already used. Tenet
        // selection has no prior stream to preserve, so no continuity salt (unlike culture's 3-arg).
        var rng = new Random(Helper.MixSeeds(seed, faith.AzgaarId));

        if (faith.Parent == null)
        {
            faith.Tenets = WeightedPickDistinct(rng, tenetCount, [], in ctx, kind);
        }
        else
        {
            // Derived: copy parent list, fill to count (weighted), mutate per slot (weighted).
            var tenets = new List<string>(faith.Parent.Tenets);
            while (tenets.Count > tenetCount)
                tenets.RemoveAt(tenets.Count - 1);
            while (tenets.Count < tenetCount)
                tenets.Add(WeightedPickOne(rng, tenets, in ctx, kind));
            for (int i = 0; i < tenets.Count; i++)
                if (rng.NextDouble() < mutationRate)
                    tenets[i] = WeightedPickOne(rng,
                        tenets.Where((_, idx) => idx != i).ToList(), in ctx, kind);
            faith.Tenets = tenets;
        }
    }

    // ── weighted pickers ──────────────────────────────────────────────────────

    /// <summary>Roulette draw of <paramref name="count"/> distinct tenets (without replacement).</summary>
    private static List<string> WeightedPickDistinct(
        Random rng, int count, List<string> excluded, in FaithContext ctx, FaithKind kind)
    {
        var result = new List<string>(excluded);
        // Snapshot ctx into a local because lambdas/closures can't capture an `in` parameter.
        var c = ctx;
        while (result.Count < count)
        {
            var available = Pool.Where(t => !result.Contains(t.Key)).ToList();
            if (available.Count == 0) break;
            var idx = WeightedIndex(rng, available, result, in c, kind);
            result.Add(available[idx].Key);
        }
        return result;
    }

    /// <summary>Single weighted draw excluding <paramref name="excluded"/> keys.</summary>
    private static string WeightedPickOne(
        Random rng, List<string> excluded, in FaithContext ctx, FaithKind kind)
    {
        var available = Pool.Where(t => !excluded.Contains(t.Key)).ToList();
        if (available.Count == 0) return Pool[rng.Next(Pool.Length)].Key; // fallback (should not happen)
        var c = ctx;
        return available[WeightedIndex(rng, available, excluded, in c, kind)].Key;
    }

    // Roulette index over `available` by weight; `chosen` = already-picked keys (for exclusivity).
    private static int WeightedIndex(
        Random rng, List<TenetEntry> available, List<string> chosen, in FaithContext ctx, FaithKind kind)
    {
        var c = ctx;
        var weights = new double[available.Count];
        double total = 0;
        for (int i = 0; i < available.Count; i++)
        {
            double w = Weight(available[i], chosen, in c, kind);
            if (w < 0) w = 0;
            weights[i] = w;
            total += w;
        }

        if (total <= 0)
        {
            // All gated/dud/excluded → uniform fallback over the NON-dud, NON-excluded pool so a
            // dud or a forbidden combo is never the fallback pick.
            var legal = new List<int>();
            for (int i = 0; i < available.Count; i++)
                if (!available[i].Dud && !ExcludedByChosen(available[i], chosen))
                    legal.Add(i);
            if (legal.Count > 0) return legal[rng.Next(legal.Count)];
            return rng.Next(available.Count); // degenerate: nothing legal — pick anything
        }

        double roll = rng.NextDouble() * total;
        double acc = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            acc += weights[i];
            if (roll < acc) return i;
        }
        return weights.Length - 1;
    }

    private static float Weight(TenetEntry entry, List<string> chosen, in FaithContext ctx, FaithKind kind)
    {
        if (entry.Dud) return 0f;                                   // 💀 inert — never picked
        if (ExcludedByChosen(entry, chosen)) return 0f;             // 🚫 forbidden combo
        // HasLand==false → terrain gate treated as 1 for all (kind still applies).
        float terrain = (!ctx.HasLand || entry.TerrainGate is null) ? 1f : entry.TerrainGate(in ctx);
        // Favoured if the faith's kind shares ANY flag with the tenet's favoured set.
        float kindF = (entry.Favoured & kind) != FaithKind.None ? 1f : KindPenalty;
        return terrain * kindF; // 0 from a hard filter stays 0 (it wins)
    }

    // Symmetric exclusivity: candidate is forbidden if it lists any chosen key OR any chosen tenet
    // lists the candidate.
    private static bool ExcludedByChosen(TenetEntry entry, List<string> chosen)
    {
        if (entry.ExclusiveWith != null)
            foreach (var ex in entry.ExclusiveWith)
                if (chosen.Contains(ex)) return true;
        foreach (var ck in chosen)
            if (ByKey.TryGetValue(ck, out var ce) && ce.ExclusiveWith != null && ce.ExclusiveWith.Contains(entry.Key))
                return true;
        return false;
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    // Derive a FaithKind from Faith.Type + a doctrine scan for theism (faith twin of ParseEthos).
    private static FaithKind KindOf(in FaithContext ctx)
    {
        FaithKind k = ctx.FaithType switch
        {
            "Folk"      => FaithKind.Folk,
            "Organized" => FaithKind.Organized,
            "Cult"      => FaithKind.Cult,
            _           => FaithKind.None,
        };
        foreach (var d in ctx.Doctrines)
        {
            if (d == "doctrine_polytheist") k |= FaithKind.Polytheist;
            else if (d == "doctrine_monotheist") k |= FaithKind.Monotheist;
        }
        return k;
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
