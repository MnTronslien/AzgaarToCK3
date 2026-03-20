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

    }

    // -------------------------------------------------------------------------
    // Character domain tree
    // -------------------------------------------------------------------------

    /// <summary>
    /// For each character, prints a small tree of the titles they directly hold,
    /// labelled by their highest-rank title. Use --log-level debug to see output.
    /// </summary>
    public static void PrintCharacterDomains(L.Map map)
    {
        if (map.Empires is null) return;
        Logger.Section("CHARACTER DOMAINS");

        foreach (var character in map.Characters)
        {
            // Highest-rank held title determines the label
            var topTitle = character.HeldTitles
                .OrderBy(t => TitleSortOrder(t))
                .FirstOrDefault();

            var label = topTitle != null
                ? $"{character.Id} [{TierName(topTitle)} · {topTitle.Name}]"
                : $"{character.Id} [no titles]";

            Logger.Debug(label);

            // Print held titles sorted by tier, skipping the top title used as the label
            var rest = character.HeldTitles
                .OrderBy(t => TitleSortOrder(t))
                .Skip(1)
                .ToList();

            Each(rest, "", (title, p, isLast) =>
                Logger.Debug($"{p}{Branch(isLast)}{title.Name} [{TierName(title)}]"));
        }

    }

    /// <summary>
    /// Sort key: lower = higher rank. Empire sorts before Kingdom sorts before Duchy etc.
    /// </summary>
    private static int TitleSortOrder(L.ITitle t) => t switch
    {
        L.Empire  _ => 0,
        L.Kingdom _ => 1,
        L.Duchy   _ => 2,
        L.County  _ => 3,
        _           => 4
    };

    private static string TierName(L.ITitle t) => t switch
    {
        L.Empire  _ => "Empire",
        L.Kingdom _ => "Kingdom",
        L.Duchy   _ => "Duchy",
        L.County  _ => "County",
        _           => "Title"
    };

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

    }
}
