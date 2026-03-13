using Converter.Lemur.Deserialization;
using Converter.Lemur.Entities;

namespace Converter.Lemur;

public static class FaithManager
{
    public static Dictionary<int, Faith> Build(AzgaarReligion[] religions)
    {
        var faiths = new Dictionary<int, Faith>();

        // Pass 1: build Faith objects (skip index 0 and removed entries)
        foreach (var r in religions)
        {
            if (r.i == 0) continue;
            if (r.removed != 0) continue;

            int rootId = FindRoot(r.i, religions);
            var faith = new Faith
            {
                AzgaarId          = r.i,
                Name              = r.name,
                CK3Key            = $"lemur_faith_{r.i}",
                CK3ReligionKey    = $"lemur_religion_{rootId}",
                HexColor          = r.color ?? "#808080",
                IconKey           = $"custom_faith_{(r.i % 10) + 1}",
                Type              = r.type ?? "",
                IsUnreformed      = r.type == "Folk" || r.type == "Cult",
                Deity             = r.deity ?? "",
                Expansion         = r.expansion ?? "",
                Expansionism      = r.expansionism,
                OriginCellId      = r.center,
                OriginalCultureId = r.culture,
                RuralPop          = r.rural,
                UrbanPop          = r.urban,
                CellCount         = r.cells,
            };
            faiths[r.i] = faith;
        }

        // Pass 2: wire Parent references
        foreach (var faith in faiths.Values)
        {
            var r = religions[faith.AzgaarId];
            if (r.origins != null && r.origins.Length > 0 && r.origins[0] != 0)
            {
                faiths.TryGetValue(r.origins[0], out var parent);
                faith.Parent = parent;
            }
        }

        return faiths;
    }

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
