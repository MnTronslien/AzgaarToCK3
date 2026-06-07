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
                Form              = r.form ?? "",
                Deity             = r.deity ?? "",
                Expansion         = r.expansion ?? "",
                Expansionism      = r.expansionism,
                OriginCellId      = r.center,
                OriginalCultureId = r.culture,
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

        // Pass 2b: a Heresy inherits its parent's form (confirmed from Azgaar source — heresies do not
        // carry their own form). Resolved after Parent links exist so the parent's form is available.
        foreach (var faith in faiths.Values)
            if (faith.Type == "Heresy" && faith.Parent != null)
                faith.Form = faith.Parent.Form;

        // Pass 3: assign DOCTRINES in topological order (parents before children) so child faiths
        // can inherit and mutate from an already-assigned parent. TENETS are NOT assigned here —
        // they are deferred to FaithTenetAssigner (run after BaronyTerrainAssigner) so they can gate
        // on the canonical Barony.Ck3Terrain, which does not exist yet at Build time. Faith.Tenets
        // is left empty here; nothing between Build and the assigner reads it.
        // Global seed is always resolved by ConversionManager before Build() is called.
        int seed = Settings.Instance.Seed!.Value;
        float mutationRate = Settings.Instance.DoctrineMutationRate;

        var childrenOf = faiths.Values
            .Where(f => f.Parent != null)
            .GroupBy(f => f.Parent!.AzgaarId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var queue = new Queue<Faith>(faiths.Values.Where(f => f.Parent == null));
        while (queue.Count > 0)
        {
            var faith = queue.Dequeue();
            AssignDoctrines(faith, seed, mutationRate);
            if (childrenOf.TryGetValue(faith.AzgaarId, out var children))
                foreach (var child in children)
                    queue.Enqueue(child);
        }

        // Since we used topological order, all parents will have been processed before their children, so inheritance and mutation will work as intended.

        Logger.Info($"Assigned doctrines to {faiths.Count} faiths (tenets deferred to FaithTenetAssigner).");
        Logger.Info("Faiths done.");

        return faiths;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Doctrine assignment (tenets are assigned later by FaithTenetAssigner)
    // ─────────────────────────────────────────────────────────────────────────

    private static void AssignDoctrines(Faith faith, int seed, float mutationRate)
    {
        var rng = new Random(Helper.MixSeeds(seed, faith.AzgaarId));
        faith.Doctrines = PickDoctrines(faith, rng, mutationRate);
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
