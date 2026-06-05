using Converter.Lemur.Entities;
using Converter.Lemur.Fields;
using static Converter.Lemur.TraditionData;

namespace Converter.Lemur;

/// <summary>
/// Assigns each culture its 4 traditions via a weighted, topological draw
/// (foundational → derived → hybrid), deferred out of <c>CultureManager.Build</c> so it can
/// read the canonical <c>Barony.Ck3Terrain</c> the pipeline already computed (exposed here as a
/// per-culture <see cref="CultureContext"/>).
///
/// <para>Two axes shape a <b>fresh</b> pick's weight:</para>
/// <list type="bullet">
/// <item><b>Terrain</b> — a HARD gate (<c>{0,1}</c>). A mismatched terrain tradition is removed
/// entirely. This is what differentiates cultures: a desert culture <i>cannot</i> roll
/// forest/jungle/maritime traditions.</item>
/// <item><b>Ethos</b> — a SOFT multiplier (<see cref="EthosPenalty"/> when the culture's ethos is
/// not in the tradition's favoured set, else 1). Never zeroes a survivor of the terrain gate.</item>
/// </list>
///
/// <para>Inherited traditions are NOT re-gated — derived cultures copy the parent list, hybrids
/// merge parents; gating applies to fresh picks only (foundational fill, mutation replacement,
/// hybrid pool-exhaustion fill).</para>
/// </summary>
public static class CultureTraditionAssigner
{
    private const int TraditionCount = 4;

    /// <summary>The one tunable magnitude. Soft ethos ×penalty; only re-weights terrain survivors.</summary>
    private const float EthosPenalty = 0.3f;

    // The pickable pool: everything except the held-out is_shown=always no placeholders.
    private static readonly TraditionEntry[] Pool =
        TraditionData.All.Where(t => !t.Special).ToArray();

    public static void Assign(Map map, IReadOnlyDictionary<int, CultureContext> terrain, int seed)
    {
        Logger.Section("Assigning culture traditions");

        float mutationRate = Converter.Settings.Instance.DoctrineMutationRate;

        // Topological walk over Culture.Parents: a culture is processed only after all its parents.
        var cultures = map.Cultures.Values.ToList();
        var visited = new HashSet<int>();
        var queue = new Queue<Culture>();

        foreach (var c in cultures)
            if (c.Parents.Count == 0)
                queue.Enqueue(c);

        while (queue.Count > 0)
        {
            var culture = queue.Dequeue();
            if (!visited.Add(culture.AzgaarId)) continue;

            var ctx = terrain.TryGetValue(culture.AzgaarId, out var t)
                ? t
                : new CultureContext(false, 0, new Dictionary<Provinces.Ck3Terrain, float>(), 0f);
            var ethos = ParseEthos(culture.Ethos);
            var rng = new Random(Helper.MixSeeds(seed, culture.AzgaarId, 99)); // same salt as old pass 3

            if (culture.Parents.Count == 0)
            {
                // Foundational: pick 4 distinct weighted traditions.
                culture.Traditions = WeightedPickDistinct(rng, TraditionCount, [], in ctx, ethos);
            }
            else if (culture.Parents.Count == 1)
            {
                // Derived: copy parent list, fill to 4 (weighted), mutate per slot (weighted).
                var parent = culture.Parents[0];
                var traditions = new List<string>(parent.Traditions);
                while (traditions.Count < TraditionCount)
                    traditions.Add(WeightedPickOne(rng, traditions, in ctx, ethos));
                for (int i = 0; i < traditions.Count; i++)
                    if (rng.NextDouble() < mutationRate)
                        traditions[i] = WeightedPickOne(rng,
                            traditions.Where((_, idx) => idx != i).ToList(), in ctx, ethos);
                culture.Traditions = traditions;
            }
            else
            {
                // Hybrid: 1 from A, 1 from B, fill remaining from merged parent pool (inherited,
                // not re-gated) else fresh weighted picks.
                var parentA = culture.Parents[0];
                var parentB = culture.Parents[1];
                var listA = parentA.Traditions;
                var listB = parentB.Traditions;
                var chosen = new List<string>();

                if (listA.Count > 0)
                    chosen.Add(listA[rng.Next(listA.Count)]);
                var bPool = listB.Where(x => !chosen.Contains(x)).ToList();
                if (bPool.Count > 0)
                    chosen.Add(bPool[rng.Next(bPool.Count)]);
                else if (listB.Count > 0)
                    chosen.Add(listB[rng.Next(listB.Count)]);

                var pool = listA.Concat(listB).Where(x => !chosen.Contains(x)).Distinct().ToList();
                while (chosen.Count < TraditionCount)
                {
                    if (pool.Count > 0 && rng.NextDouble() >= mutationRate)
                    {
                        var pick = pool[rng.Next(pool.Count)];
                        pool.Remove(pick);
                        chosen.Add(pick);
                    }
                    else
                    {
                        chosen.Add(WeightedPickOne(rng, chosen, in ctx, ethos));
                    }
                }
                culture.Traditions = chosen;
            }

            // Enqueue children (cultures whose Parents include this one) once not already visited.
            foreach (var c in cultures)
                if (!visited.Contains(c.AzgaarId) && c.Parents.Contains(culture))
                    queue.Enqueue(c);
        }

        // Any unvisited cultures (cycles / orphans) get fresh foundational-style picks.
        foreach (var culture in cultures)
        {
            if (visited.Contains(culture.AzgaarId)) continue;
            var ctx = terrain.TryGetValue(culture.AzgaarId, out var t)
                ? t
                : new CultureContext(false, 0, new Dictionary<Provinces.Ck3Terrain, float>(), 0f);
            var ethos = ParseEthos(culture.Ethos);
            var rng = new Random(Helper.MixSeeds(seed, culture.AzgaarId, 99));
            culture.Traditions = WeightedPickDistinct(rng, TraditionCount, [], in ctx, ethos);
        }

        LogSummary(map);
    }

    // ── weighted pickers ──────────────────────────────────────────────────────

    /// <summary>Roulette draw of <paramref name="count"/> distinct traditions (without replacement).</summary>
    private static List<string> WeightedPickDistinct(
        Random rng, int count, List<string> excluded, in CultureContext ctx, Ethos ethos)
    {
        var available = Pool.Where(t => !excluded.Contains(t.Key)).ToList();
        var result = new List<string>();
        // Snapshot ctx into a local because lambdas/closures can't capture an `in` parameter.
        var c = ctx;
        while (result.Count < count && available.Count > 0)
        {
            var idx = WeightedIndex(rng, available, in c, ethos);
            result.Add(available[idx].Key);
            available.RemoveAt(idx);
        }
        return result;
    }

    /// <summary>Single weighted draw excluding <paramref name="excluded"/> keys.</summary>
    private static string WeightedPickOne(
        Random rng, List<string> excluded, in CultureContext ctx, Ethos ethos)
    {
        var available = Pool.Where(t => !excluded.Contains(t.Key)).ToList();
        if (available.Count == 0) return Pool[rng.Next(Pool.Length)].Key; // fallback (should not happen)
        var c = ctx;
        return available[WeightedIndex(rng, available, in c, ethos)].Key;
    }

    // Roulette index over `available` by weight = terrainGate(ctx) × ethosFactor. Σw==0 → uniform.
    private static int WeightedIndex(Random rng, List<TraditionEntry> available, in CultureContext ctx, Ethos ethos)
    {
        var c = ctx;
        var weights = new double[available.Count];
        double total = 0;
        for (int i = 0; i < available.Count; i++)
        {
            double w = Weight(available[i], in c, ethos);
            if (w < 0) w = 0;
            weights[i] = w;
            total += w;
        }

        if (total <= 0) return rng.Next(available.Count); // all gated out → uniform fallback

        double roll = rng.NextDouble() * total;
        double acc = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            acc += weights[i];
            if (roll < acc) return i;
        }
        return weights.Length - 1;
    }

    private static float Weight(TraditionEntry entry, in CultureContext ctx, Ethos ethos)
    {
        // HasLand==false → terrain gate treated as 1 for all (ethos still applies).
        float terrain = (!ctx.HasLand || entry.TerrainGate is null) ? 1f : entry.TerrainGate(in ctx);
        float ethosF = entry.FavouredEthos.HasFlag(ethos) ? 1f : EthosPenalty;
        return terrain * ethosF; // 0 from the gate stays 0 (gate wins)
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    // CultureManager assigns "ethos_<name>"; map back to the flag for matching.
    private static Ethos ParseEthos(string ethos) => ethos switch
    {
        "ethos_bellicose"    => Ethos.Bellicose,
        "ethos_stoic"        => Ethos.Stoic,
        "ethos_bureaucratic" => Ethos.Bureaucratic,
        "ethos_spiritual"    => Ethos.Spiritual,
        "ethos_courtly"      => Ethos.Courtly,
        "ethos_egalitarian"  => Ethos.Egalitarian,
        "ethos_communal"     => Ethos.Communal,
        _                    => Ethos.None, // unknown → never matches favoured set (all penalised)
    };

    private static void LogSummary(Map map)
    {
        var sb = new System.Text.StringBuilder(
            $"Assigned traditions to {map.Cultures.Count} cultures (cultureId,name,ethos,traditions):");
        foreach (var c in map.Cultures.Values.OrderBy(c => c.AzgaarId))
            sb.Append($"\n- {c.Name} (id {c.AzgaarId}): ethos={c.Ethos}, traditions=[{string.Join(", ", c.Traditions)}]");
        Logger.Info(sb.ToString());
    }
}
