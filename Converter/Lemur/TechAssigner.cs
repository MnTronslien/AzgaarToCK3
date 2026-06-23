using Converter.Lemur.Entities;

namespace Converter.Lemur;

/// <summary>
/// Resolves each culture's start <see cref="CultureEra"/> and starting <see cref="Innovation"/> set.
/// Deferred out of <c>CultureManager.Build</c> (like <see cref="CultureTraditionAssigner"/>) so it can
/// read the formed counties (for development-based variance) and the assigned traditions (for the
/// freebie pairing). See PLAN_tech_levels.md. Deterministic for a fixed seed.
///
/// <para><b>Era</b> = world <c>WorldTechLevel</c> baseline (or, for a hybrid, the highest parent era),
/// shifted ±1 by rank-based development variance (bottom/top band of cultures by average territory
/// development), clamped to the era range.</para>
///
/// <para><b>Innovations</b> — per era from tribal up to the resolved era, draw a count from that era's
/// general pool via the gradient <c>fill(d) = min(1, FrontierFraction + FillStep × d)</c> where
/// <c>d</c> is the era's distance below the frontier (full in the deep past, ~3 at the frontier).
/// Hybrids instead draw imperfectly-additively from the union of their parents' innovations. Then any
/// tradition-paired freebie whose era the culture has reached is added on top.</para>
/// </summary>
public static class TechAssigner
{
    public static void Assign(Map map, int seed)
    {
        Logger.Section("Assigning culture tech (eras + innovations)");
        var s = Converter.Settings.Instance;

        // Per-culture era shift ∈ {-1,0,+1} from development rank.
        var shift = ComputeVarianceShifts(map, s.TechVarianceBandFraction);

        // Resolve parents before children (DAG fixed-point); leftover cycles resolved arbitrarily.
        var cultures = map.Cultures.Values.ToList();
        var resolved = new HashSet<int>();
        bool progress = true;
        while (progress)
        {
            progress = false;
            foreach (var c in cultures)
            {
                if (resolved.Contains(c.AzgaarId)) continue;
                if (!c.Parents.All(p => resolved.Contains(p.AzgaarId))) continue;
                Resolve(c, s, shift.GetValueOrDefault(c.AzgaarId, 0), seed);
                resolved.Add(c.AzgaarId);
                progress = true;
            }
        }
        foreach (var c in cultures)
            if (!resolved.Contains(c.AzgaarId))
            {
                Resolve(c, s, shift.GetValueOrDefault(c.AzgaarId, 0), seed);
                resolved.Add(c.AzgaarId);
            }

        LogSummary(map);

        // Region-gated freebies, post-pass (needs every culture's resolved era + traditions).
        FreebieAssigner.Assign(map);
    }

    private static void Resolve(Culture c, Converter.Settings s, int shift, int seed)
    {
        var rng = new Random(Helper.MixSeeds(seed, c.AzgaarId, 7)); // tech salt

        var baseEra = c.Parents.Count >= 2
            ? (CultureEra)Math.Max((int)c.Parents[0].Era, (int)c.Parents[1].Era) // hybrid: highest parent
            : s.WorldTechLevel;                                                  // else: world baseline
        var era = ClampEra((int)baseEra + shift);
        c.Era = era;

        // General-pool innovations only. Region-gated freebies are added afterwards by FreebieAssigner.
        c.Innovations = c.Parents.Count >= 2
            ? HybridDraw(c, era, rng)
            : GeneralDraw(era, rng, s);
    }

    // Per-era gradient draw from the general pool (tribal up to the culture's era).
    private static List<Innovation> GeneralDraw(CultureEra era, Random rng, Converter.Settings s)
    {
        var result = new List<Innovation>();
        for (int e = 0; e <= (int)era; e++)
        {
            var pool = InnovationData.GeneralPool((CultureEra)e).ToList();
            if (pool.Count == 0) continue;
            int d = (int)era - e;
            double fill = Math.Min(1.0, s.TechFrontierFraction + s.TechFillStep * d);
            int count = Math.Clamp(
                (int)Math.Round(fill * pool.Count, MidpointRounding.AwayFromZero), 1, pool.Count);
            result.AddRange(PickDistinct(pool, count, rng));
        }
        return result;
    }

    // Hybrid: imperfectly additive on the parents' (general) innovation sets.
    // count = max(A,B) + floor(min(A,B) × 0.5); drawn from the union, era-capped, filled from lower
    // eras of the general pool if the union is short.
    private static List<Innovation> HybridDraw(Culture c, CultureEra era, Random rng)
    {
        // Freebies are added in a later pass, so parent Innovations are general-pool only here.
        var a = c.Parents[0].Innovations.ToList();
        var b = c.Parents[1].Innovations.ToList();
        int A = a.Count, B = b.Count;
        int count = Math.Max(A, B) + (int)Math.Floor(Math.Min(A, B) * 0.5);

        var union = a.Concat(b)
            .Where(i => (int)i.Era <= (int)era)
            .GroupBy(i => i.Key).Select(g => g.First())
            .ToList();
        var result = PickDistinct(union, Math.Min(count, union.Count), rng);

        if (result.Count < count)
        {
            var have = result.Select(r => r.Key).ToHashSet();
            var filler = new List<Innovation>();
            for (int e = 0; e <= (int)era; e++)
                filler.AddRange(InnovationData.GeneralPool((CultureEra)e).Where(i => !have.Contains(i.Key)));
            result.AddRange(PickDistinct(filler, Math.Min(count - result.Count, filler.Count), rng));
        }
        return result;
    }

    /// <summary>Average territory development per culture → rank → bottom band demotes, top band promotes.</summary>
    private static Dictionary<int, int> ComputeVarianceShifts(Map map, double bandFraction)
    {
        var dev = new Dictionary<int, (double sum, int n)>();
        foreach (var county in map.Counties ?? new List<County>())
        {
            int cid = ((ITitle)county).GetDominantCulture(map).i;
            int d = county.GetDevelopmentLevel();
            var cur = dev.GetValueOrDefault(cid, (0.0, 0));
            dev[cid] = (cur.Item1 + d, cur.Item2 + 1);
        }

        // Rank only cultures with a development signal; the rest stay at baseline (shift 0).
        var ranked = dev.Where(kv => kv.Value.n > 0)
            .OrderBy(kv => kv.Value.sum / kv.Value.n)
            .ThenBy(kv => kv.Key) // stable tie-break for determinism
            .Select(kv => kv.Key)
            .ToList();
        int n = ranked.Count;
        int band = (int)Math.Floor(n * bandFraction);

        var shifts = new Dictionary<int, int>();
        for (int i = 0; i < n; i++)
            shifts[ranked[i]] = i < band ? -1 : (i >= n - band ? +1 : 0);
        return shifts;
    }

    // Deterministic partial Fisher–Yates: pick `count` distinct entries using `rng`.
    private static List<Innovation> PickDistinct(List<Innovation> pool, int count, Random rng)
    {
        var copy = new List<Innovation>(pool);
        count = Math.Clamp(count, 0, copy.Count);
        var result = new List<Innovation>(count);
        for (int i = 0; i < count; i++)
        {
            int j = rng.Next(i, copy.Count);
            (copy[i], copy[j]) = (copy[j], copy[i]);
            result.Add(copy[i]);
        }
        return result;
    }

    private static CultureEra ClampEra(int v) => (CultureEra)Math.Clamp(v, 0, 3);

    private static void LogSummary(Map map)
    {
        var sb = new System.Text.StringBuilder(
            $"Assigned tech to {map.Cultures.Count} cultures (id, name, era, #general innovations):");
        foreach (var c in map.Cultures.Values.OrderBy(c => c.AzgaarId))
            sb.Append($"\n- {c.Name} (id {c.AzgaarId}): era={c.Era}, innovations={c.Innovations.Count}");
        Logger.Info(sb.ToString());
    }
}
