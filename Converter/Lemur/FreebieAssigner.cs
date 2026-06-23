using Converter.Lemur.Entities;

namespace Converter.Lemur;

/// <summary>
/// Post-pass over <see cref="FreebieData"/>: after <see cref="TechAssigner"/> has resolved every
/// culture's era + general-pool innovations, walk the freebie registry (in order) and grant each
/// innovation whose criterion passes and whose era the culture has reached. Order + the running
/// granted-set let dependent entries test against earlier ones (e.g. canoes vs longboats).
/// </summary>
public static class FreebieAssigner
{
    public static void Assign(Map map)
    {
        Logger.Section("Assigning freebie innovations");

        foreach (var c in map.Cultures.Values)
        {
            // Seed with what the random draw already gave the culture so we never double-grant.
            var granted = new HashSet<string>(c.Innovations.Select(i => i.Key));

            foreach (var f in FreebieData.All)
            {
                if ((int)f.Innovation.Era > (int)c.Era) continue;   // can't discover above its era
                if (granted.Contains(f.Innovation.Key)) continue;   // already has it
                if (!f.Criterion(c, granted)) continue;

                c.Innovations.Add(f.Innovation);
                granted.Add(f.Innovation.Key);
            }
        }

        LogSummary(map);
    }

    private static void LogSummary(Map map)
    {
        var freebieKeys = FreebieData.All.Select(f => f.Innovation.Key).ToHashSet();
        var sb = new System.Text.StringBuilder("Freebie innovations granted (culture → freebies):");
        int total = 0;
        foreach (var c in map.Cultures.Values.OrderBy(c => c.AzgaarId))
        {
            var got = c.Innovations.Where(i => freebieKeys.Contains(i.Key)).Select(i => i.Key).ToList();
            if (got.Count == 0) continue;
            total += got.Count;
            sb.Append($"\n- {c.Name} (id {c.AzgaarId}, {c.Type}): {string.Join(", ", got)}");
        }
        Logger.Info(total == 0 ? "No freebie innovations granted." : sb.ToString());
    }
}
