namespace Converter.Lemur;
using Converter.Lemur.Entities;

public static class CharacterFactory
{
    private const int KingDemesne  = 3;
    private const int DukeDemesne  = 2;
    private const int CountDemesne = 1;

    public static void CreateAndAssignAll(Map map)
    {
        var claimed = new HashSet<County>();

        // Step 1 — Kings for intact kingdoms (all duchies share the kingdom's AzgaarStateId)
        foreach (var empire in map.Empires!)
        {
            foreach (var kingdom in empire.Kingdoms)
            {
                bool intact = kingdom.Duchies.All(d => !d.IsAbsorbed);
                if (!intact) continue;

                var king = MakeCharacter(kingdom, map);
                map.Characters.Add(king);
                kingdom.Holder = king;
                king.HeldTitles.Add(kingdom);

                var pool = kingdom.Duchies
                    .SelectMany(d => d.Counties)
                    .Where(c => !claimed.Contains(c))
                    .ToList();
                ClaimCounties(king, pool, KingDemesne, claimed);
            }
        }

        // Step 2 — Independent dukes: one per absorbed state (group by AzgaarStateId)
        foreach (var empire in map.Empires!)
        {
            foreach (var kingdom in empire.Kingdoms)
            {
                var absorbedGroups = kingdom.Duchies
                    .Where(d => d.IsAbsorbed)
                    .GroupBy(d => d.AzgaarStateId);

                foreach (var group in absorbedGroups)
                {
                    // Primary = most counties; tie-break by Id for determinism
                    var primary = group
                        .OrderByDescending(d => d.Counties.Count)
                        .ThenBy(d => d.Id)
                        .First();

                    foreach (var secondary in group.Where(d => d != primary))
                        secondary.PrimaryDuchy = primary;

                    var duke = MakeCharacter(primary, map);
                    map.Characters.Add(duke);
                    primary.Holder = duke;
                    duke.HeldTitles.Add(primary);

                    // Pool from all duchies in the group
                    var pool = group
                        .SelectMany(d => d.Counties)
                        .Where(c => !claimed.Contains(c))
                        .ToList();
                    ClaimCounties(duke, pool, DukeDemesne, claimed);
                }
            }
        }

        // Step 3 — Counts for all remaining unclaimed counties
        foreach (var empire in map.Empires!)
        {
            foreach (var kingdom in empire.Kingdoms)
            {
                foreach (var duchy in kingdom.Duchies)
                {
                    foreach (var county in duchy.Counties)
                    {
                        if (claimed.Contains(county)) continue;

                        var count = MakeCharacter(county, map);
                        map.Characters.Add(count);
                        county.Holder = count;
                        count.HeldTitles.Add(county);
                        claimed.Add(county);
                    }
                }
            }
        }

        // Step 4 — Commit liege decisions to entity graph so writers only serialize
        foreach (var empire in map.Empires!)
        {
            foreach (var kingdom in empire.Kingdoms)
            {
                foreach (var duchy in kingdom.Duchies)
                {
                    duchy.LiegeId = duchy.IsAbsorbed ? null : kingdom.Ck3_Id();

                    if (duchy.PrimaryDuchy != null)
                        foreach (var county in duchy.Counties)
                            county.LiegeDuchy = duchy.PrimaryDuchy;
                }
            }
        }

        RunAssertions(map);
        Logger.Info($"Created {map.Characters.Count} characters " +
                    $"({map.Empires!.SelectMany(e => e.Kingdoms).Count(k => k.Holder != null)} kings, " +
                    $"{map.Empires!.SelectMany(e => e.Kingdoms).SelectMany(k => k.Duchies).Count(d => d.Holder != null)} dukes, " +
                    $"{map.Empires!.SelectMany(e => e.Kingdoms).SelectMany(k => k.Duchies).SelectMany(d => d.Counties).Count(c => c.Holder != null && c.Holder != c.Parent?.Holder)} counts)");
    }

    /// <summary>
    /// Assigns up to <paramref name="n"/> counties from <paramref name="pool"/> to
    /// <paramref name="character"/>. The most-populous county is claimed first (capital),
    /// then remaining counties in descending population order.
    /// </summary>
    private static void ClaimCounties(Character character, List<County> pool, int n, HashSet<County> claimed)
    {
        if (pool.Count == 0) return;

        // Capital = county whose highest-population barony has the most population
        // TODO: use actual capital burg flag once de jure capitals bug is fixed
        var capital = pool
            .OrderByDescending(c => c.Baronies!.Max(b => (double)(b.burg?.Population ?? 0)))
            .First();

        var rest = pool
            .Where(c => c != capital)
            .OrderByDescending(c => c.Baronies!.Sum(b => (double)(b.burg?.Population ?? 0)))
            .ToList();

        var toAssign = new List<County>(n) { capital };
        toAssign.AddRange(rest.Take(n - 1));

        foreach (var county in toAssign)
        {
            county.Holder = character;
            character.HeldTitles.Add(county);
            claimed.Add(county);
        }
    }

    private static Character MakeCharacter(ITitle title, Map map)
    {
        var azCulture = title.GetDominantCulture(map);
        var culture = map.Cultures.GetValueOrDefault(azCulture.i) ?? map.Cultures.Values.First();

        var azFaith = title.GetDominantReligion(map);
        var faith = map.Faiths.GetValueOrDefault(azFaith.i) ?? map.Faiths.Values.First();

        return new Character(culture, faith);
    }

    private static void RunAssertions(Map map)
    {
        // Assertion 1: no two independent dukes share the same AzgaarStateId
        var independentDukes = map.Empires!
            .SelectMany(e => e.Kingdoms)
            .SelectMany(k => k.Duchies)
            .Where(d => d.Holder != null && d.IsAbsorbed)
            .ToList();

        foreach (var group in independentDukes.GroupBy(d => d.AzgaarStateId).Where(g => g.Count() > 1))
            Logger.Error($"Assert fail: multiple independent dukes for AzgaarStateId={group.Key}: " +
                         $"{string.Join(", ", group.Select(d => d.Name))}");

        // Assertion 2: kings do not hold counties outside their kingdom's native state
        foreach (var empire in map.Empires!)
        {
            foreach (var kingdom in empire.Kingdoms)
            {
                if (kingdom.Holder == null) continue;
                var badCounties = kingdom.Holder.HeldTitles
                    .OfType<County>()
                    .Where(c => ((Duchy)c.Parent!).AzgaarStateId != kingdom.Id)
                    .ToList();
                foreach (var county in badCounties)
                    Logger.Error($"Assert fail: king of {kingdom.Name} holds county {county.Name} " +
                                 $"from state {((Duchy)county.Parent!).AzgaarStateId} (expected {kingdom.Id})");
            }
        }
    }
}
