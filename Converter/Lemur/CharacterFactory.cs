namespace Converter.Lemur;
using Converter.Lemur.Entities;

/// <summary>
/// Creates and assigns characters to titles using a top-down de facto drill-down.
///
/// Algorithm:
///   Phase 1 — For each de facto tree root (DeFactoLiege == null, highest tier first),
///              drill down through de facto children to one county. The root holder also
///              holds every intermediate title along the drill-down path.
///   Phase 2 — Assign holders to any remaining titles that appear in county de facto chains
///              but were not reached by Phase 1 (e.g. vassal duchies the king didn't drill into).
///
/// Counties without holders are left for CK3 to auto-spawn at game start.
/// </summary>
public static class CharacterFactory
{
    public static void CreateAndAssignAll(Map map)
    {
        var claimed = new HashSet<County>();

        // Phase 1: de facto roots, top-down (kingdoms before independent duchies)
        var roots = CollectRoots(map);
        foreach (var root in roots)
        {
            if (root.Holder != null) continue;
            var pool = CollectCountiesUnder(root).Where(c => !claimed.Contains(c)).ToList();
            if (pool.Count == 0)
            {
                Logger.Warning($"[CharacterFactory] {root.Ck3_Id()} has no available counties — skipping.");
                continue;
            }
            DrillDown(root, pool, map, claimed);
        }

        // Phase 2: remaining titles in county de facto chains that Phase 1 didn't reach
        var duchiesNeedingHolders = map.Empires!
            .SelectMany(e => e.Kingdoms)
            .SelectMany(k => k.Duchies)
            .SelectMany(d => d.Counties)
            .Select(c => c.DeFactoLiege)
            .OfType<Duchy>()
            .Distinct()
            .Where(d => d.Holder == null);

        foreach (var duchy in duchiesNeedingHolders)
        {
            var pool = duchy.Counties.Where(c => !claimed.Contains(c)).ToList();
            if (pool.Count == 0)
            {
                Logger.Warning($"[CharacterFactory] {duchy.Ck3_Id()} has no available counties — skipping.");
                continue;
            }
            DrillDown(duchy, pool, map, claimed);
        }

        // Counties with no holder are left for CK3 to auto-spawn.
        // TODO: verify after CK3 test run that counties with a liege but no holder
        //       correctly receive auto-generated rulers. If not, add count generation here.

        Logger.Info($"Created {map.Characters.Count} characters " +
                    $"({map.Empires!.SelectMany(e => e.Kingdoms).Count(k => k.Holder != null)} kings, " +
                    $"{map.Empires!.SelectMany(e => e.Kingdoms).SelectMany(k => k.Duchies).Count(d => d.Holder != null)} dukes)");

        RunAssertions(map);
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
            Logger.Error($"[Assert] Multiple independent dukes for AzgaarStateId={group.Key}: " +
                         $"{string.Join(", ", group.Select(d => d.Name))}");

        // Assertion 2: kings do not hold counties from a different native state
        foreach (var empire in map.Empires!)
        foreach (var kingdom in empire.Kingdoms)
        {
            if (kingdom.Holder == null) continue;
            var foreignCounties = kingdom.Holder.HeldTitles
                .OfType<County>()
                .Where(c => ((Duchy)c.DeJureParent!).AzgaarStateId != kingdom.Id)
                .ToList();
            foreach (var county in foreignCounties)
                Logger.Error($"[Assert] King of {kingdom.Name} holds county {county.Name} " +
                             $"from state {((Duchy)county.DeJureParent!).AzgaarStateId} (expected {kingdom.Id})");
        }

        // Assertion 3: kings do not hold counties from absorbed duchies
        foreach (var empire in map.Empires!)
        foreach (var kingdom in empire.Kingdoms)
        {
            if (kingdom.Holder == null) continue;
            var absorbedCounties = kingdom.Holder.HeldTitles
                .OfType<County>()
                .Where(c => ((Duchy)c.DeJureParent!).IsAbsorbed)
                .ToList();
            foreach (var county in absorbedCounties)
                Logger.Error($"[Assert] King of {kingdom.Name} holds county {county.Name} " +
                             $"from absorbed duchy {((Duchy)county.DeJureParent!).Name}");
        }
    }

    /// <summary>
    /// Returns de facto tree roots (DeFactoLiege == null) ordered highest tier first.
    /// Currently: kingdoms, then independent duchies.
    /// When DeFactoHierarchyBuilder sets kingdom.DeFactoLiege = empire, empires will
    /// automatically appear as roots here and be processed before kingdoms.
    /// </summary>
    private static IEnumerable<ITitle> CollectRoots(Map map)
    {
        // Kingdoms whose DeFactoLiege is null are roots (no empire de facto hierarchy yet)
        var kingdoms = map.Empires!
            .SelectMany(e => e.Kingdoms)
            .Where(k => k.DeFactoLiege == null && k.Duchies.Any(d => d.Counties.Count > 0))
            .Cast<ITitle>();

        // Independent duchy roots (absorbed primaries: DeFactoLiege == null)
        var independentDuchies = map.Empires!
            .SelectMany(e => e.Kingdoms)
            .SelectMany(k => k.Duchies)
            .Where(d => d.DeFactoLiege == null && d.Counties.Count > 0)
            .Cast<ITitle>();

        return kingdoms.Concat(independentDuchies);
    }

    /// <summary>
    /// Collects all counties reachable from <paramref name="root"/> via de facto children.
    /// Tier-agnostic: uses GetDeFactoChildren at each level.
    /// </summary>
    private static IEnumerable<County> CollectCountiesUnder(ITitle root)
    {
        if (root is County county) return new[] { county };

        return GetDeFactoChildren(root)
            .SelectMany(CollectCountiesUnder);
    }

    /// <summary>
    /// Returns the immediate de facto children of a title.
    /// Generalised: adding kingdom→empire in DeFactoHierarchyBuilder will automatically
    /// make this work for empire roots without any changes here.
    /// </summary>
    private static IEnumerable<ITitle> GetDeFactoChildren(ITitle title) => title switch
    {
        Kingdom k  => k.Duchies.Where(d => !d.IsAbsorbed && d.DeFactoLiege == k).Cast<ITitle>(),
        Duchy   d  => d.Counties.Where(c => c.DeFactoLiege == d).Cast<ITitle>(),
        _          => Enumerable.Empty<ITitle>()
    };

    /// <summary>
    /// Creates a character for <paramref name="root"/>, then drills down through de facto
    /// children to pick the best county, assigning the character to every unheld title
    /// along the path (so a king who drills into duchy D and county C holds all three).
    /// </summary>
    private static void DrillDown(ITitle root, List<County> pool, Map map, HashSet<County> claimed)
    {
        var capital = PickCapital(pool);
        var character = MakeCharacter(root, map);

        // Build path from capital up to root via DeFactoLiege chain, then reverse.
        // e.g. kingdom root: [duchy, county] — assigns character to kingdom + duchy + county.
        var path = new List<ITitle>();
        ITitle? current = capital;
        while (current != null && current != root)
        {
            path.Add(current);
            current = current.DeFactoLiege;
        }
        path.Reverse();

        Assign(character, root, map);
        foreach (var title in path)
            if (title.Holder == null)
                Assign(character, title, map);

        claimed.Add(capital);
    }

    private static County PickCapital(List<County> pool) =>
        pool.OrderByDescending(c => c.Baronies!.Max(b => (double?)b.burg?.Population ?? 0)).First();

    private static void Assign(Character character, ITitle title, Map map)
    {
        title.Holder = character;
        character.HeldTitles.Add(title);
        if (!map.Characters.Contains(character))
            map.Characters.Add(character);
    }

    private static Character MakeCharacter(ITitle title, Map map)
    {
        var azCulture = title.GetDominantCulture(map);
        var culture = map.Cultures.GetValueOrDefault(azCulture.i) ?? map.Cultures.Values.First();

        var azFaith = title.GetDominantReligion(map);
        var faith = map.Faiths.GetValueOrDefault(azFaith.i) ?? map.Faiths.Values.First();

        return new Character(culture, faith);
    }
}
