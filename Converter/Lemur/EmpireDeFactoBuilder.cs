namespace Converter.Lemur;

using Converter.Lemur.Entities;

/// <summary>
/// Wires the empire tier into the de facto hierarchy from the resolved diplomacy graph
/// (<see cref="DiplomacyResolver"/>). Touches only titles — <c>DeFactoLiege</c>, <c>Empire.Capital</c>,
/// <c>Empire.CapitalKingdom</c>, <c>Empire.Government</c>, and titular-empire minting. No holders: those are
/// assigned by <c>CharacterFactory</c>, which (once this has run) sees empires as de facto roots and populates
/// the emperor + vassal kings down the tree.
///
/// Must run AFTER <c>DeFactoHierarchyBuilder</c> (needs the absorbed-state primary duchies) and BEFORE
/// <c>CharacterFactory</c> (so the de facto hierarchy is complete through the empire tier).
/// </summary>
public static class EmpireDeFactoBuilder
{
    // Minted titular-empire ids start high so they never collide with culture/religion empire ids
    // (which equal a small culture/religion index) or with each other.
    private const int MintedEmpireIdBase = 9_000_000;

    public static void Run(Map map)
    {
        if (map.Diplomacy is null || map.Diplomacy.Roots.Count == 0) return;

        // Resolve each root to its surviving kingdom + de jure empire, grouped by empire for collision detection.
        var byEmpire = new Dictionary<Empire, List<(int rootId, Kingdom kingdom)>>();
        foreach (var rootId in map.Diplomacy.Roots)
        {
            var kingdom = map.Kingdoms.FirstOrDefault(k => k.Id == rootId);
            if (kingdom is null)
            {
                Logger.Warning($"[EmpireDeFacto] root state {rootId} has no surviving kingdom — skipping.");
                continue;
            }
            if (kingdom.DeJureParent is not Empire empire)
            {
                Logger.Warning($"[EmpireDeFacto] root {kingdom.Name} has no de jure empire (orphan) — skipping.");
                continue;
            }
            if (!byEmpire.TryGetValue(empire, out var list)) byEmpire[empire] = list = new();
            list.Add((rootId, kingdom));
        }

        int mintedCounter = 0;
        foreach (var (empire, group) in byEmpire.OrderBy(kv => kv.Key.Id))
        {
            if (group.Count == 1)
            {
                var (rootId, kingdom) = group[0];
                WireEmperor(map, empire, kingdom, rootId, minted: false);
            }
            else
            {
                // Collision ("if both can't have it, neither can"): the shared de jure empire stays
                // holderless; each colliding root gets its own de-facto-only titular empire.
                Logger.Info($"[EmpireDeFacto] collision in {empire.Name}: {group.Count} suzerains → each gets a titular empire.");
                foreach (var (rootId, kingdom) in group)
                {
                    var minted = new Empire(MintedEmpireIdBase + mintedCounter++, kingdom.Name, null);
                    map.Empires!.Add(minted);
                    WireEmperor(map, minted, kingdom, rootId, minted: true);
                }
            }
        }
    }

    private static void WireEmperor(Map map, Empire empire, Kingdom rootKingdom, int rootId, bool minted)
    {
        // The suzerain's own kingdom is the emperor's demesne (capital child); same character holds both.
        rootKingdom.DeFactoLiege = empire;
        empire.CapitalKingdom = rootKingdom;
        empire.Capital = rootKingdom.Capital;          // override AssignCapitals — seat the emperor in his own kingdom
        empire.Government = rootKingdom.Government;     // empire inherits the suzerain kingdom's government
        Logger.Debug($"[EmpireDeFacto] {(minted ? "titular" : "reuse")} {empire.Ck3_Id()} ← suzerain {rootKingdom.Name}");

        foreach (var juniorId in map.Diplomacy!.JuniorsByRoot[rootId])
        {
            var juniorKingdom = map.Kingdoms.FirstOrDefault(k => k.Id == juniorId);
            if (juniorKingdom != null)
            {
                juniorKingdom.DeFactoLiege = empire;    // surviving junior → vassal king under the emperor
            }
            else
            {
                // Junior kingdom merged away: its absorbed state's primary duchy is the independent de facto
                // root DeFactoHierarchyBuilder created. Re-point it at the empire → direct imperial vassal duke.
                var primary = map.Duchies!.FirstOrDefault(d =>
                    d.AzgaarStateId == juniorId && d.IsAbsorbed && d.DeFactoLiege == null);
                if (primary != null)
                    primary.DeFactoLiege = empire;
                else
                    Logger.Warning($"[EmpireDeFacto] junior state {juniorId} has neither kingdom nor primary duchy — dropped.");
            }
        }
    }
}
