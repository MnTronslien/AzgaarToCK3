using L = Converter.Lemur.Entities;

namespace Converter.Lemur;

/// <summary>
/// Diagnostic utility that prints the de facto and de jure title trees to the
/// console at Debug log level. Pass --log-level debug to see output.
/// </summary>
public static class TitleTreeDebugger
{
    // -------------------------------------------------------------------------
    // De Facto tree
    // -------------------------------------------------------------------------

    /// <summary>
    /// Prints the de facto title tree. Kings are roots; their vassal duchies and
    /// counties are nested beneath. Independent duchies (those absorbed via
    /// MergeTinyKingdoms whose AzgaarStateId != parent kingdom.Id) appear as
    /// separate root trees.
    /// </summary>
    public static void PrintTrees(L.Map map)
    {
        if (map.Empires is null) return;

        Logger.Debug("=== DE FACTO TITLE TREE ===");

        // Kingdom roots
        foreach (var kingdom in map.Kingdoms)
        {
            Logger.Debug($"[KING] {kingdom.Ck3_Id()} ({kingdom.Name}) -- {FormatHolder(kingdom.Holder)}");

            foreach (var duchy in kingdom.Duchies)
            {
                if (duchy.IsAbsorbed) continue;

                Logger.Debug($"  [DUCHY-VASSAL] {duchy.Ck3_Id()} ({duchy.Name}) -- {FormatHolder(duchy.Holder)}");
                PrintCounties(duchy, "    ");
            }
        }

        // Independent duchy roots
        foreach (var kingdom in map.Kingdoms)
        {
            foreach (var duchy in kingdom.Duchies)
            {
                if (!duchy.IsAbsorbed) continue;

                Logger.Debug($"[DUKE-INDEP] {duchy.Ck3_Id()} ({duchy.Name}) -- {FormatHolder(duchy.Holder)}");
                PrintCounties(duchy, "  ");
            }
        }

        Logger.Debug("=== END DE FACTO TREE ===");
    }

    private static void PrintCounties(L.Duchy duchy, string indent)
    {
        foreach (var county in duchy.Counties)
            Logger.Debug($"{indent}[COUNTY] {county.Ck3_Id()} ({county.Name}) -- {FormatHolder(county.Holder)}");
    }

    private static string FormatHolder(L.Character? holder)
    {
        if (holder is null) return "(unowned)";
        return $"{holder.Id} [{holder.Culture.CK3Key}/{holder.Faith.CK3Key}]";
    }

    // -------------------------------------------------------------------------
    // De Jure tree (bonus)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Prints the complete de jure hierarchy: Empire > Kingdom > Duchy > County > Barony.
    /// Each node shows its CK3 ID and current holder if any.
    /// </summary>
    public static void PrintDeJureTrees(L.Map map)
    {
        if (map.Empires is null) return;

        Logger.Debug("=== DE JURE TITLE TREE ===");

        foreach (var empire in map.Empires)
        {
            Logger.Debug($"[EMPIRE] {empire.Ck3_Id()} ({empire.Name}) -- {FormatHolder(empire.Holder)}");

            foreach (var kingdom in empire.Kingdoms)
            {
                Logger.Debug($"  [KINGDOM] {kingdom.Ck3_Id()} ({kingdom.Name}) -- {FormatHolder(kingdom.Holder)}");

                foreach (var duchy in kingdom.Duchies)
                {
                    Logger.Debug($"    [DUCHY] {duchy.Ck3_Id()} ({duchy.Name}) -- {FormatHolder(duchy.Holder)}");

                    foreach (var county in duchy.Counties)
                    {
                        Logger.Debug($"      [COUNTY] {county.Ck3_Id()} ({county.Name}) -- {FormatHolder(county.Holder)}");

                        foreach (var barony in county.Baronies ?? Enumerable.Empty<L.Barony>())
                            Logger.Debug($"        [BARONY] {barony.Ck3_Id()} ({barony.Name}) -- {FormatHolder(barony.Holder)}");
                    }
                }
            }
        }

        Logger.Debug("=== END DE JURE TREE ===");
    }
}
