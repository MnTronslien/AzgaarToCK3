using L = Converter.Lemur.Entities;

namespace Converter.Lemur;

/// <summary>
/// Diagnostic utility that prints de facto and de jure title trees at Debug log level.
/// Pass --log-level debug to see output.
/// </summary>
public static class TitleTreeDebugger
{
    private static string Branch(bool isLast) => isLast ? "└── " : "├── ";
    private static string Indent(bool isLast) => isLast ? "    " : "│   ";

    private static void Each<T>(IEnumerable<T> source, string prefix, Action<T, string, bool> print)
    {
        var items = source as IReadOnlyList<T> ?? source.ToList();
        for (int i = 0; i < items.Count; i++)
            print(items[i], prefix, i == items.Count - 1);
    }

    // -------------------------------------------------------------------------
    // De Facto tree
    // -------------------------------------------------------------------------

    /// <summary>
    /// Prints the de facto title tree. Intact kingdoms are roots with their vassal duchies
    /// nested beneath. Independent duchies (absorbed state primaries) are separate roots
    /// with their own counties and any secondary duchies nested beneath.
    /// </summary>
    public static void PrintTrees(L.Map map)
    {
        if (map.Empires is null) return;
        Logger.Section("DE FACTO TITLE TREE");

        foreach (var kingdom in map.Kingdoms)
        {
            Logger.Debug($"{kingdom.Name} [Kingdom]");
            var vassalDuchies = kingdom.Duchies.Where(d => !d.IsAbsorbed).ToList();
            Each(vassalDuchies, "", (duchy, p, isLast) =>
            {
                Logger.Debug($"{p}{Branch(isLast)}{duchy.Name} [Duchy]");
                Each(duchy.Counties, p + Indent(isLast), (county, cp, cl) =>
                    Logger.Debug($"{cp}{Branch(cl)}{county.Name} [County]"));
            });
        }

        var primaryAbsorbed = map.Kingdoms
            .SelectMany(k => k.Duchies)
            .Where(d => d.IsAbsorbed && d.DeFactoLiege == null)
            .ToList();

        foreach (var primary in primaryAbsorbed)
        {
            Logger.Debug($"{primary.Name} [Duchy]");

            var secondaries = map.Kingdoms
                .SelectMany(k => k.Duchies)
                .Where(d => d.DeFactoLiege == primary)
                .ToList();

            var ownCounties = primary.Counties.ToList();
            for (int i = 0; i < ownCounties.Count; i++)
            {
                bool isLast = i == ownCounties.Count - 1 && secondaries.Count == 0;
                Logger.Debug($"{Branch(isLast)}{ownCounties[i].Name} [County]");
            }

            for (int i = 0; i < secondaries.Count; i++)
            {
                bool isLast = i == secondaries.Count - 1;
                var secondary = secondaries[i];
                Logger.Debug($"{Branch(isLast)}{secondary.Name} [Duchy]");
                Each(secondary.Counties, Indent(isLast), (county, p, l) =>
                    Logger.Debug($"{p}{Branch(l)}{county.Name} [County]"));
            }
        }

        Logger.Debug("=== END DE FACTO TREE ===");
    }

    // -------------------------------------------------------------------------
    // De Jure tree
    // -------------------------------------------------------------------------

    /// <summary>
    /// Prints the complete de jure hierarchy: Empire > Kingdom > Duchy > County > Barony.
    /// </summary>
    public static void PrintDeJureTrees(L.Map map)
    {
        if (map.Empires is null) return;
        Logger.Section("DE JURE TITLE TREE");

        foreach (var empire in map.Empires)
        {
            Logger.Debug($"{empire.Name} [Empire]");
            Each(empire.Kingdoms, "", (kingdom, kp, kLast) =>
            {
                Logger.Debug($"{kp}{Branch(kLast)}{kingdom.Name} [Kingdom]");
                Each(kingdom.Duchies, kp + Indent(kLast), (duchy, dp, dLast) =>
                {
                    Logger.Debug($"{dp}{Branch(dLast)}{duchy.Name} [Duchy]");
                    Each(duchy.Counties, dp + Indent(dLast), (county, cp, cLast) =>
                    {
                        Logger.Debug($"{cp}{Branch(cLast)}{county.Name} [County]");
                        var baronies = county.Baronies ?? new List<L.Barony>();
                        Each(baronies, cp + Indent(cLast), (barony, bp, bLast) =>
                            Logger.Debug($"{bp}{Branch(bLast)}{barony.Name} [Barony]"));
                    });
                });
            });
        }

        Logger.Debug("=== END DE JURE TREE ===");
    }
}
