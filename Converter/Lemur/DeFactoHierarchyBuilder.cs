namespace Converter.Lemur;
using Converter.Lemur.Entities;

/// <summary>
/// Builds the de facto title hierarchy — sets DeFactoLiege on every duchy and county.
/// Must run after MergeTinyKingdoms (which sets DeJureParent and finalises IsAbsorbed),
/// and before CharacterFactory (which reads DeFactoLiege to determine county pools).
/// </summary>
public static class DeFactoHierarchyBuilder
{
    public static void Build(Map map)
    {
        foreach (var empire in map.Empires!)
        foreach (var kingdom in empire.Kingdoms)
        {
            // Non-absorbed duchies are vassals of their kingdom
            foreach (var duchy in kingdom.Duchies.Where(d => !d.IsAbsorbed))
                duchy.DeFactoLiege = kingdom;

            // Absorbed duchies: one independent duke per state group.
            // Pick primary (most counties; tie-break by Id), mark the rest as secondary.
            // TODO: primary should be the duchy containing the Azgaar state capital burg
            //       (de jure capital of the old kingdom). Blocked by de jure capitals bug.
            foreach (var group in kingdom.Duchies.Where(d => d.IsAbsorbed).GroupBy(d => d.AzgaarStateId))
            {
                var primary = group.OrderByDescending(d => d.Counties.Count).ThenBy(d => d.Id).First();
                // primary.DeFactoLiege stays null = independent
                foreach (var secondary in group.Where(d => d != primary))
                    secondary.DeFactoLiege = primary;
            }

            // County liege: secondary duchy → its primary duchy; primary/intact → own duchy
            foreach (var duchy in kingdom.Duchies)
            {
                var countyLiege = duchy.DeFactoLiege as Duchy ?? duchy;
                foreach (var county in duchy.Counties)
                    county.DeFactoLiege = countyLiege;
            }
        }
    }
}
