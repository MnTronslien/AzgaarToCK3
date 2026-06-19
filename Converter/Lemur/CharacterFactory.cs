namespace Converter.Lemur;
using Converter.Lemur.Entities;

/// <summary>
/// Creates and assigns characters to titles using a top-down de facto drill-down.
///
/// Passes (each assigns the head of every realm at its tier the higher passes didn't already claim):
///   Empire + roots — de facto roots, highest tier first: diplomacy empires (emperor holds the empire +
///                    his CapitalKingdom's chain), then independent kingdoms, then independent duchies.
///   Kingdom pass   — vassal kingdoms under an empire (DeFactoLiege set) → their own kings.
///   Duchy pass     — remaining holderless duchies in county de facto chains → dukes.
///   County pass    — remaining holderless counties → counts.
///
/// Counties without holders are left for CK3 to auto-spawn at game start.
/// </summary>
public static class CharacterFactory
{
    // Generated rulers are born this many years before the game-start date. A placeholder until the
    // age bell-curve (notes-for-later.md) replaces it with a per-character age; relocated here from
    // the old hardcoded Character.BirthYear = 1033 so birth years track Map.StartDate.
    private const int RulerAgeOffset = 33;

    public static void CreateAndAssignAll(Map map)
    {
        var claimed = new HashSet<County>();

        // Empire + independent-realm pass: de facto roots, top-down (empires, then independent kingdoms,
        // then independent duchies). An empire root drills only into its CapitalKingdom, so the emperor
        // holds the empire + his own kingdom's capital chain; vassal kingdoms are left for the kingdom pass.
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

        // Kingdom pass: vassal kingdoms under an empire (DeFactoLiege set) the root pass didn't hold —
        // each gets its own king (a de facto vassal of the emperor). No-op on maps without diplomacy empires.
        foreach (var kingdom in map.Kingdoms
                     .Where(k => k.Holder == null && k.DeFactoLiege != null && k.Duchies.Any(d => d.Counties.Count > 0)))
        {
            var pool = CollectCountiesUnder(kingdom).Where(c => !claimed.Contains(c)).ToList();
            if (pool.Count == 0)
            {
                Logger.Warning($"[CharacterFactory] {kingdom.Ck3_Id()} has no available counties — skipping.");
                continue;
            }
            DrillDown(kingdom, pool, map, claimed);
        }

        // Duchy pass: remaining titles in county de facto chains that the passes above didn't reach
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

        // County pass: explicit counts for all remaining holder-less counties.
        // CK3 does not auto-spawn counts from liege-only history entries — without an
        // explicit holder the county falls to the nearest titled holder up the de jure chain.
        var allCounties = map.Empires!
            .SelectMany(e => e.Kingdoms)
            .SelectMany(k => k.Duchies)
            .SelectMany(d => d.Counties);

        foreach (var county in allCounties.Where(c => c.Holder == null))
            DrillDown(county, new List<County> { county }, map, claimed);

        var allTitles = map.Empires!.SelectMany(e => e.Kingdoms).ToList();
        Logger.Info($"Created {map.Characters.Count} characters " +
                    $"({map.Empires!.Count(e => e.Holder != null)} emperors, " +
                    $"{allTitles.Count(k => k.Holder != null)} kings, " +
                    $"{allTitles.SelectMany(k => k.Duchies).Count(d => d.Holder != null)} dukes, " +
                    $"{allTitles.SelectMany(k => k.Duchies).SelectMany(d => d.Counties).Count(c => c.Holder != null)} counts)");

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
            Logger.Warning($"[Assert] Multiple independent dukes for AzgaarStateId={group.Key}: " +
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
                Logger.Warning($"[Assert] King of {kingdom.Name} holds county {county.Name} " +
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
                Logger.Warning($"[Assert] King of {kingdom.Name} holds county {county.Name} " +
                             $"from absorbed duchy {((Duchy)county.DeJureParent!).Name}");
        }
    }

    /// <summary>
    /// Returns de facto tree roots (DeFactoLiege == null) ordered highest tier first:
    /// diplomacy-driven empires, then independent kingdoms, then independent duchies.
    /// An empire is a root only when EmpireDeFactoBuilder gave it a CapitalKingdom (it heads a de facto
    /// realm) — ordinary holderless culture/religion empires are skipped, avoiding empty-pool noise.
    /// </summary>
    private static IEnumerable<ITitle> CollectRoots(Map map)
    {
        // Empire roots: diplomacy empires that head a de facto realm (have a suzerain/capital kingdom).
        var empires = map.Empires!
            .Where(e => e.CapitalKingdom != null)
            .Cast<ITitle>();

        // Independent kingdoms (DeFactoLiege == null — i.e. not a vassal under an empire)
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

        return empires.Concat(kingdoms).Concat(independentDuchies);
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
    /// Returns the immediate de facto children of a title for the drill-down pool.
    /// An empire's only drill-child is its CapitalKingdom (the emperor's own demesne kingdom); its vassal
    /// kingdoms / absorbed-duchy vassals are deliberately NOT returned here — the emperor must not drill into
    /// and absorb a vassal's counties. Those vassals get their own rulers via the kingdom/duchy passes.
    /// </summary>
    private static IEnumerable<ITitle> GetDeFactoChildren(ITitle title) => title switch
    {
        Empire  e  => e.CapitalKingdom is { } ck ? new ITitle[] { ck } : Enumerable.Empty<ITitle>(),
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

        // Pick a name from the culture's ThemeBundle pool.
        // History-defined CK3 characters need explicit name= — CK3 does not auto-fill from name_list
        // (that's only for run-time-spawned characters). Without an explicit name, CK3 logs
        // "Missing loc for name ''" and the UI renders "King [blank] of [Kingdom]".
        // Pick deterministically by seeding from (global seed, character index, culture id) — same
        // convention used elsewhere in the converter (Helper.MixSeeds). Same world seed reproduces
        // the same names; different cultures get different picks at the same character index.
        // Gender doctrine is a separate next-release item; every ruler is male for now.
        var pool = culture.ThemeBundle.MaleNames;
        var rng = new Random(Helper.MixSeeds(
            Converter.Settings.Instance.Seed!.Value, map.Characters.Count, culture.AzgaarId));
        var name = pool[rng.Next(pool.Length)];

        // Floor at 0: CK3 dates cannot be below year 0, and a low start date could push the
        // birth year negative (StartDate itself is already floored, but 0 - RulerAgeOffset isn't).
        var birthYear = Math.Max(0, map.StartDate.Year - RulerAgeOffset);
        return new Character(culture, faith, birthYear, name);
    }
}
