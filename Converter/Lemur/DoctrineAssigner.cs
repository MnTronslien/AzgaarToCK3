using Converter.Lemur.Entities;

namespace Converter.Lemur;

/// <summary>
/// Assigns Doctrines and Tenets onto each Faith using a seeded, deterministic algorithm.
/// Child faiths inherit from their parent with a configurable mutation rate.
/// Call after FaithManager.Build() has wired Parent references.
/// </summary>
public static class DoctrineAssigner
{
    /// <summary>
    /// Assigns doctrines and tenets to all faiths.
    /// The global seed must be resolved (non-null) before calling — see ConversionManager.Run().
    /// </summary>
    public static void Assign(IEnumerable<Faith> faiths, int globalSeed, int tenetCount, float mutationRate)
    {
        var all = faiths.ToList();

        // Build children lookup: parentAzgaarId → list of child faiths
        var childrenOf = all
            .Where(f => f.Parent != null)
            .GroupBy(f => f.Parent!.AzgaarId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // BFS in topological order: roots first, then children once their parent is done
        var queue = new Queue<Faith>(all.Where(f => f.Parent == null));
        while (queue.Count > 0)
        {
            var faith = queue.Dequeue();
            AssignFaith(faith, globalSeed, tenetCount, mutationRate);

            if (childrenOf.TryGetValue(faith.AzgaarId, out var children))
                foreach (var child in children)
                    queue.Enqueue(child);
        }

        // Safety net: any faiths that were skipped due to broken parent refs
        foreach (var faith in all.Where(f => f.Doctrines.Count == 0))
            AssignFaith(faith, globalSeed, tenetCount, mutationRate);
    }

    private static void AssignFaith(Faith faith, int globalSeed, int tenetCount, float mutationRate)
    {
        var rng = new Random(HashCode.Combine(globalSeed, faith.AzgaarId));
        faith.Doctrines = AssignDoctrines(faith, rng, mutationRate);
        faith.Tenets    = AssignTenets(faith, rng, tenetCount, mutationRate);
    }

    private static List<string> AssignDoctrines(Faith faith, Random rng, float mutationRate)
    {
        var result = new List<string>(DoctrineData.Groups.Length);
        for (int i = 0; i < DoctrineData.Groups.Length; i++)
        {
            var options = DoctrineData.Groups[i].Options;
            // Inherit from parent unless mutating (or root faith with no parent)
            if (faith.Parent?.Doctrines.Count > i && rng.NextDouble() > mutationRate)
                result.Add(faith.Parent.Doctrines[i]);
            else
                result.Add(options[rng.Next(options.Length)]);
        }
        return result;
    }

    private static List<string> AssignTenets(Faith faith, Random rng, int count, float mutationRate)
    {
        // Start from parent tenets (copy), then mutate individual slots
        var chosen = new List<string>(faith.Parent?.Tenets ?? []);

        // Ensure we have exactly `count` slots
        while (chosen.Count < count)
            chosen.Add(PickUniqueTenet(rng, chosen));
        while (chosen.Count > count)
            chosen.RemoveAt(chosen.Count - 1);

        // Mutate each slot independently
        for (int i = 0; i < chosen.Count; i++)
        {
            if (faith.Parent == null || rng.NextDouble() < mutationRate)
            {
                var excluded = chosen.Where((_, idx) => idx != i).ToList();
                chosen[i] = PickUniqueTenet(rng, excluded);
            }
        }

        return chosen;
    }

    private static string PickUniqueTenet(Random rng, ICollection<string> excluded)
    {
        var pool = DoctrineData.AllTenets.Except(excluded).ToArray();
        return pool[rng.Next(pool.Length)];
    }
}
