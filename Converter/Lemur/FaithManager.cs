using Converter.Lemur.Deserialization;
using Converter.Lemur.Entities;

namespace Converter.Lemur;

public static class FaithManager
{
    /// <summary>
    /// Build the faith graph. The <paramref name="religionCellCounts"/> dict must be derived
    /// from the GeoJSON cell graph (see <see cref="Converter.Lemur.Fields.CellDistribution"/>),
    /// not from the Azgaar JSON <c>cells</c> field which is unreliable across export variants.
    /// </summary>
    public static Dictionary<int, Faith> Build(
        AzgaarReligion[] religions,
        IReadOnlyDictionary<int, int> religionCellCounts)
    {
        Logger.Section("Building faiths");
        var faiths = new Dictionary<int, Faith>();

        // Pre-compute which religion IDs have at least one active (non-removed) child,
        // so zero-cell parent faiths needed for doctrine inheritance are not pruned.
        var hasActiveChildren = religions
            .Where(r => r.i != 0 && r.removed == 0
                        && r.origins != null && r.origins.Length > 0 && r.origins[0] != 0)
            .Select(r => r.origins![0])
            .ToHashSet();

        // Pass 1: construct Faith objects (skip sentinel, removed, and empty with no children)
        foreach (var r in religions)
        {
            if (r.i == 0) continue;
            if (r.removed != 0) continue;
            // Land-cell count derived from GeoJSON, NOT from r.cells. Azgaar may omit r.cells
            // entirely on customized exports, in which case the C# DTO defaults it to 0 and we'd
            // incorrectly prune religions that still appear on land cells — producing orphan
            // faith references in province history (CK3 crash on unpause).
            religionCellCounts.TryGetValue(r.i, out int landCellCount);
            if (landCellCount == 0 && !hasActiveChildren.Contains(r.i)) continue;

            int rootId = FindRoot(r.i, religions);
            faiths[r.i] = new Faith
            {
                AzgaarId          = r.i,
                Name              = r.name,
                CK3Key            = $"lemur_faith_{r.i}",
                CK3ReligionKey    = $"lemur_religion_{rootId}",
                HexColor          = r.color ?? "#808080",
                IconKey           = $"custom_faith_{(r.i % 10) + 1}",
                Type              = r.type ?? "",
                Deity             = r.deity ?? "",
                Expansion         = r.expansion ?? "",
                Expansionism      = r.expansionism,
                OriginCellId      = r.center,
                OriginalCultureId = r.culture,
                RuralPop          = r.rural,
                UrbanPop          = r.urban,
                CellCount         = r.cells,
            };
        }

        // Pass 2: wire Parent references (one step up, not root)
        foreach (var faith in faiths.Values)
        {
            var r = religions[faith.AzgaarId];
            if (r.origins != null && r.origins.Length > 0 && r.origins[0] != 0)
            {
                faiths.TryGetValue(r.origins[0], out var parent);
                faith.Parent = parent;
            }
        }

        // Pass 3: assign doctrines and tenets in topological order (parents before children)
        // so child faiths can inherit and mutate from an already-assigned parent.
        // Global seed is always resolved by ConversionManager before Build() is called.
        int seed = Settings.Instance.Seed!.Value;
        int tenetCount = Settings.Instance.TenetCount;
        float mutationRate = Settings.Instance.DoctrineMutationRate;

        var childrenOf = faiths.Values
            .Where(f => f.Parent != null)
            .GroupBy(f => f.Parent!.AzgaarId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var queue = new Queue<Faith>(faiths.Values.Where(f => f.Parent == null));
        while (queue.Count > 0)
        {
            var faith = queue.Dequeue();
            AssignDoctrinesAndTenets(faith, seed, tenetCount, mutationRate);
            if (childrenOf.TryGetValue(faith.AzgaarId, out var children))
                foreach (var child in children)
                    queue.Enqueue(child);
        }

        // Since we used topological order, all parents will have been processed before their children, so inheritance and mutation will work as intended.

        var sb = new System.Text.StringBuilder($"Assigned doctrines and tenets to {faiths.Count} faiths.");
        foreach (var f in faiths.Values)
            sb.Append($"\n- {f.Name} (id {f.AzgaarId}): tenets=[{string.Join(", ", f.Tenets)}]");
        Logger.Info(sb.ToString());
        Logger.Info("Faiths done.");

        return faiths;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Doctrine & tenet assignment
    // ─────────────────────────────────────────────────────────────────────────

    private static void AssignDoctrinesAndTenets(Faith faith, int seed, int tenetCount, float mutationRate)
    {
        var rng = new Random(Helper.MixSeeds(seed, faith.AzgaarId));
        faith.Doctrines = PickDoctrines(faith, rng, mutationRate);
        faith.Tenets    = PickTenets(faith, rng, tenetCount, mutationRate);
    }

    private static List<string> PickDoctrines(Faith faith, Random rng, float mutationRate)
    {
        var result = new List<string>(DoctrineData.Groups.Length);
        for (int i = 0; i < DoctrineData.Groups.Length; i++)
        {
            var options = DoctrineData.Groups[i].Options;
            // Inherit from parent unless this slot mutates (or faith has no parent)
            if (faith.Parent?.Doctrines.Count > i && rng.NextDouble() > mutationRate)
                result.Add(faith.Parent.Doctrines[i]);
            else
                result.Add(options[rng.Next(options.Length)]);
        }
        return result;
    }

    private static List<string> PickTenets(Faith faith, Random rng, int count, float mutationRate)
    {
        var chosen = new List<string>(faith.Parent?.Tenets ?? []);

        while (chosen.Count < count)
            chosen.Add(PickUniqueTenet(rng, chosen));
        while (chosen.Count > count)
            chosen.RemoveAt(chosen.Count - 1);

        for (int i = 0; i < chosen.Count; i++)
            if (faith.Parent == null || rng.NextDouble() < mutationRate)
                chosen[i] = PickUniqueTenet(rng, chosen.Where((_, idx) => idx != i).ToList());

        return chosen;
    }

    private static string PickUniqueTenet(Random rng, ICollection<string> excluded)
    {
        var pool = DoctrineData.AllTenets.Except(excluded).ToArray();
        return pool[rng.Next(pool.Length)];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static int FindRoot(int id, AzgaarReligion[] religions)
    {
        var visited = new HashSet<int>();
        int current = id;
        while (true)
        {
            if (!visited.Add(current)) return current; // cycle guard
            var r = religions[current];
            if (r.origins == null || r.origins.Length == 0 || r.origins[0] == 0)
                return current;
            current = r.origins[0];
        }
    }
}
