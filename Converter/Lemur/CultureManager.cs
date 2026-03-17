using Converter.Lemur.Deserialization;

namespace Converter.Lemur.Entities;

public static class CultureManager
{
    private static readonly string[] Ethoses =
    [
        "ethos_bellicose", "ethos_stoic", "ethos_bureaucratic",
        "ethos_communal", "ethos_egalitarian", "ethos_spiritual"
    ];

    private static readonly string[] MartialCustoms =
    [
        "martial_custom_male_only", "martial_custom_equal", "martial_custom_female_only"
    ];

    // 4 non-DLC GFX bundles: [coa_gfx, building_gfx, clothing_gfx, unit_gfx]
    private static readonly string[][] GfxBundles =
    [
        ["western_coa_gfx",         "western_building_gfx",   "western_clothing_gfx",   "western_unit_gfx"],
        ["byzantine_group_coa_gfx", "byzantine_building_gfx", "byzantine_clothing_gfx", "eastern_unit_gfx"],
        ["mena_coa_gfx",            "african_building_gfx",   "mena_clothing_gfx",       "eastern_unit_gfx"],
        ["western_coa_gfx",         "western_building_gfx",   "northern_clothing_gfx",  "western_unit_gfx"],
    ];

    public static Dictionary<int, Culture> Build(AzgaarCulture[] cultures, int seed)
    {
        Converter.Lemur.Logger.Section("Building cultures");
        var result = new Dictionary<int, Culture>();

        // Pass 1: create all Culture objects, skip sentinel (i == 0)
        foreach (var azc in cultures)
        {
            if (azc.i == 0) continue;

            var rng = new Random(HashCode.Combine(seed, azc.i));
            var culture = new Culture
            {
                AzgaarId    = azc.i,
                Name        = azc.name,
                HexColor    = azc.color ?? "#808080",
                Ethos        = Ethoses[rng.Next(Ethoses.Length)],
                MartialCustom = MartialCustoms[rng.Next(MartialCustoms.Length)],
                GfxBundle    = GfxBundles[rng.Next(GfxBundles.Length)],
            };
            result[azc.i] = culture;
        }

        // Pass 2: assign Heritage and Language in BFS topological order
        // (parents before children so children can inherit)
        AssignPillarsTopological(cultures, result, seed);

        // Pass 3: assign traditions
        float mutationRate = Converter.Settings.Instance.DoctrineMutationRate;
        AssignTraditionsTopological(cultures, result, seed, mutationRate);

        // Pass 4: wire Parents list (CK3 keys of direct parents)
        foreach (var azc in cultures)
        {
            if (azc.i == 0) continue;
            if (!result.TryGetValue(azc.i, out var culture)) continue;

            var realOrigins = GetRealOrigins(azc.origins);
            foreach (var pid in realOrigins)
                if (result.TryGetValue(pid, out var parentCulture))
                    culture.Parents.Add(parentCulture.CK3Key);
        }

        var sb = new System.Text.StringBuilder($"Assigned pillars and traditions to {result.Count} cultures.");
        foreach (var c in result.Values.OrderBy(c => c.AzgaarId))
            sb.Append($"\n- {c.Name} (id {c.AzgaarId}): heritage={c.Heritage}, language={c.Language}, traditions=[{string.Join(", ", c.Traditions)}]");
        Converter.Lemur.Logger.Info(sb.ToString());
        Converter.Lemur.Logger.Info("Cultures done.");

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pillar assignment
    // ─────────────────────────────────────────────────────────────────────────

    private static void AssignPillarsTopological(
        AzgaarCulture[] cultures,
        Dictionary<int, Culture> result,
        int seed)
    {
        // Build parent lookup: cultureId → direct parent ids
        var parentIds = new Dictionary<int, int[]>();
        foreach (var azc in cultures)
        {
            if (azc.i == 0) continue;
            parentIds[azc.i] = GetRealOrigins(azc.origins);
        }

        // BFS from foundational (no real parents) outward
        var visited = new HashSet<int>();
        var queue = new Queue<int>();

        // Seed with foundational cultures
        foreach (var azc in cultures)
        {
            if (azc.i == 0) continue;
            if (parentIds[azc.i].Length == 0)
                queue.Enqueue(azc.i);
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!visited.Add(id)) continue;
            if (!result.TryGetValue(id, out var culture)) continue;

            var origins = parentIds[id];
            var rng = new Random(HashCode.Combine(seed, id, 42)); // different salt for pillars

            if (origins.Length == 0)
            {
                // Foundational: own heritage and language
                culture.Heritage = $"lemur_heritage_{id}";
                culture.Language = $"lemur_language_{id}";
            }
            else if (origins.Length == 1)
            {
                // Derived: inherit from parent
                if (result.TryGetValue(origins[0], out var parent))
                {
                    culture.Heritage = parent.Heritage;
                    culture.Language = parent.Language;
                    // Inherit ethos + martial custom with mutation chance
                    float mutationRate = Converter.Settings.Instance.DoctrineMutationRate;
                    if (rng.NextDouble() < mutationRate)
                        culture.Ethos = Ethoses[rng.Next(Ethoses.Length)];
                    else
                        culture.Ethos = parent.Ethos;
                    if (rng.NextDouble() < mutationRate)
                        culture.MartialCustom = MartialCustoms[rng.Next(MartialCustoms.Length)];
                    else
                        culture.MartialCustom = parent.MartialCustom;
                }
                else
                {
                    culture.Heritage = $"lemur_heritage_{id}";
                    culture.Language = $"lemur_language_{id}";
                }
            }
            else
            {
                // Hybrid: heritage from one parent, language from the other
                if (origins.Length > 2)
                    Converter.Lemur.Logger.Warning($"Culture {culture.Name} (id {id}) has {origins.Length} origins; taking first 2.");

                var pa = origins[0];
                var pb = origins[1];
                result.TryGetValue(pa, out var parentA);
                result.TryGetValue(pb, out var parentB);

                culture.Heritage = parentA?.Heritage ?? $"lemur_heritage_{id}";
                culture.Language = parentB?.Language ?? $"lemur_language_{id}";
                // Hybrid ethos: from one of the parents
                culture.Ethos = (rng.Next(2) == 0 ? parentA?.Ethos : parentB?.Ethos) ?? Ethoses[rng.Next(Ethoses.Length)];
                culture.MartialCustom = (rng.Next(2) == 0 ? parentA?.MartialCustom : parentB?.MartialCustom) ?? MartialCustoms[rng.Next(MartialCustoms.Length)];
            }

            // Enqueue children
            foreach (var candidate in result.Keys)
            {
                if (visited.Contains(candidate)) continue;
                if (parentIds.TryGetValue(candidate, out var pids) && pids.Contains(id))
                    queue.Enqueue(candidate);
            }
        }

        // Any unvisited cultures (cycles or missing parents) get own pillars
        foreach (var culture in result.Values)
        {
            if (string.IsNullOrEmpty(culture.Heritage))
                culture.Heritage = $"lemur_heritage_{culture.AzgaarId}";
            if (string.IsNullOrEmpty(culture.Language))
                culture.Language = $"lemur_language_{culture.AzgaarId}";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tradition assignment
    // ─────────────────────────────────────────────────────────────────────────

    private static void AssignTraditionsTopological(
        AzgaarCulture[] cultures,
        Dictionary<int, Culture> result,
        int seed,
        float mutationRate)
    {
        var parentIds = new Dictionary<int, int[]>();
        foreach (var azc in cultures)
        {
            if (azc.i == 0) continue;
            parentIds[azc.i] = GetRealOrigins(azc.origins);
        }

        var visited = new HashSet<int>();
        var queue = new Queue<int>();

        foreach (var azc in cultures)
        {
            if (azc.i == 0) continue;
            if (parentIds[azc.i].Length == 0)
                queue.Enqueue(azc.i);
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!visited.Add(id)) continue;
            if (!result.TryGetValue(id, out var culture)) continue;

            var origins = parentIds[id];
            var rng = new Random(HashCode.Combine(seed, id, 99)); // distinct salt

            if (origins.Length == 0)
            {
                // Foundational: pick 4 distinct random traditions
                culture.Traditions = PickDistinct(rng, 4, Converter.Lemur.TraditionData.All.Select(t => t.Key).ToList(), []);
            }
            else if (origins.Length == 1)
            {
                // Derived: copy parent list, mutate per slot
                result.TryGetValue(origins[0], out var parent);
                var traditions = new List<string>(parent?.Traditions ?? []);
                while (traditions.Count < 4)
                    traditions.Add(PickOneNew(rng, Converter.Lemur.TraditionData.All.Select(t => t.Key).ToList(), traditions));
                for (int i = 0; i < traditions.Count; i++)
                    if (rng.NextDouble() < mutationRate)
                        traditions[i] = PickOneNew(rng, Converter.Lemur.TraditionData.All.Select(t => t.Key).ToList(), traditions.Where((_, idx) => idx != i).ToList());
                culture.Traditions = traditions;
            }
            else
            {
                // Hybrid: 1 from A, 1 from B, fill 2 more from pool
                result.TryGetValue(origins[0], out var parentA);
                result.TryGetValue(origins[1], out var parentB);

                var listA = parentA?.Traditions ?? [];
                var listB = parentB?.Traditions ?? [];
                var chosen = new List<string>();

                // Pick 1 from A
                if (listA.Count > 0)
                    chosen.Add(listA[rng.Next(listA.Count)]);
                // Pick 1 from B (different)
                var bPool = listB.Where(t => !chosen.Contains(t)).ToList();
                if (bPool.Count > 0)
                    chosen.Add(bPool[rng.Next(bPool.Count)]);
                else if (listB.Count > 0)
                    chosen.Add(listB[rng.Next(listB.Count)]);

                // Fill remaining slots
                var pool = listA.Concat(listB).Where(t => !chosen.Contains(t)).Distinct().ToList();
                while (chosen.Count < 4)
                {
                    if (pool.Count > 0 && rng.NextDouble() >= mutationRate)
                    {
                        var pick = pool[rng.Next(pool.Count)];
                        pool.Remove(pick);
                        chosen.Add(pick);
                    }
                    else
                    {
                        chosen.Add(PickOneNew(rng, Converter.Lemur.TraditionData.All.Select(t => t.Key).ToList(), chosen));
                    }
                }
                culture.Traditions = chosen;
            }

            // Enqueue children
            foreach (var candidate in result.Keys)
            {
                if (visited.Contains(candidate)) continue;
                if (parentIds.TryGetValue(candidate, out var pids) && pids.Contains(id))
                    queue.Enqueue(candidate);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Extract non-zero, non-null origin IDs. Returns at most 2.</summary>
    private static int[] GetRealOrigins(int[]? origins)
    {
        if (origins == null) return [];
        return origins.Where(o => o != 0).Take(2).ToArray();
    }

    private static List<string> PickDistinct(Random rng, int count, List<string> pool, List<string> excluded)
    {
        var available = pool.Where(t => !excluded.Contains(t)).ToList();
        var result = new List<string>();
        while (result.Count < count && available.Count > 0)
        {
            var idx = rng.Next(available.Count);
            result.Add(available[idx]);
            available.RemoveAt(idx);
        }
        return result;
    }

    private static string PickOneNew(Random rng, List<string> pool, List<string> excluded)
    {
        var available = pool.Where(t => !excluded.Contains(t)).ToList();
        if (available.Count == 0) return pool[rng.Next(pool.Count)]; // fallback
        return available[rng.Next(available.Count)];
    }
}
