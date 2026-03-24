using Converter.Lemur.Entities;

namespace Converter.Lemur;

public static class HolySiteFactory
{
    // Thematic modifier groups. Each entry is (key, value).
    private static readonly List<List<(string Key, string Value)>> Groups =
    [
        // Piety
        [("monthly_piety_gain_mult", "0.1"), ("clergy_opinion", "5"), ("same_faith_opinion", "5")],
        // Wisdom
        [("learning", "1"), ("learning_per_piety_level", "1"), ("monthly_lifestyle_xp_gain_mult", "0.15")],
        // War
        [("martial", "1"), ("prowess", "1"), ("knight_effectiveness_mult", "0.1"), ("defender_advantage", "5")],
        // Trade
        [("stewardship", "1"), ("tax_mult", "0.05"), ("development_growth_factor", "0.1")],
        // Diplomacy
        [("diplomacy", "1"), ("vassal_opinion", "3"), ("direct_vassal_opinion", "5"), ("same_culture_opinion", "5")],
        // Mysticism
        [("stress_loss_mult", "0.1"), ("health", "0.1"), ("fertility", "0.1"), ("monthly_piety_gain_mult", "0.15")],
        // Expansion
        [("different_faith_opinion", "5"), ("different_culture_opinion", "5"), ("title_creation_cost_mult", "-0.15")],
    ];

    public static List<HolySite> Build(Map map)
    {
        var sites = new List<HolySite>();
        // countyKey → existing HolySite, for deduplication across faiths
        var siteByCounty = new Dictionary<string, HolySite>();
        int siteIndex = 1;

        foreach (var faith in map.Faiths.Values)
        {
            var county = FindCounty(faith, map);
            if (county == null) continue;

            var countyKey = county.Ck3_Id();
            if (!siteByCounty.TryGetValue(countyKey, out var site))
            {
                var barony = FindBarony(county);
                site = new HolySite
                {
                    Key      = $"lemur_site_{siteIndex}",
                    County   = county,
                    Barony   = barony,
                    Modifiers = AssignModifiers(map.Settings.Seed ?? 0, siteIndex),
                };
                siteByCounty[countyKey] = site;
                sites.Add(site);
                siteIndex++;
            }

            faith.HolySites.Add(site);
        }

        // Give every faith the full site list so every holy site county is contestable.
        foreach (var faith in map.Faiths.Values)
            foreach (var site in sites)
                if (!faith.HolySites.Contains(site))
                    faith.HolySites.Add(site);

        return sites;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static County? FindCounty(Faith faith, Map map)
    {
        // Primary: barony that owns the origin cell → its de jure county
        if (faith.OriginCellId > 0 && map.Cells != null &&
            map.Cells.TryGetValue(faith.OriginCellId, out var originCell) &&
            originCell.Province is Barony originBarony &&
            originBarony.DeJureParent is County originCounty)
        {
            return originCounty;
        }

        // Fallback: county with the most cells matching this faith's Azgaar religion id
        if (map.Counties != null && map.Cells != null)
        {
            var best = map.Counties
                .Select(c => (county: c,
                    count: c.GetAllCells().Count(cell => cell.Religion == faith.AzgaarId)))
                .Where(x => x.count > 0)
                .OrderByDescending(x => x.count)
                .FirstOrDefault();

            if (best.county != null) return best.county;
        }

        // Last resort: first county
        return map.Counties?.FirstOrDefault();
    }

    private static Barony? FindBarony(County county)
    {
        // Capital barony: highest population burg
        return county.Baronies?
            .OrderByDescending(b => b.burg.Population)
            .FirstOrDefault();
    }

    private static List<(string Key, string Value)> AssignModifiers(int seed, int siteIndex)
    {
        var rng = new Random(seed ^ (siteIndex * 1_000_003));

        // Step 1: pick a thematic group uniformly
        var group = Groups[rng.Next(Groups.Count)];

        // Step 2: pick modifier count — 30% → 1, 50% → 2, 20% → 3
        int roll = rng.Next(100);
        int count = roll < 30 ? 1 : roll < 80 ? 2 : 3;
        count = Math.Min(count, group.Count);

        // Step 3: sample without replacement
        var indices = Enumerable.Range(0, group.Count).ToList();
        var result = new List<(string Key, string Value)>(count);
        for (int i = 0; i < count; i++)
        {
            int pick = rng.Next(indices.Count);
            result.Add(group[indices[pick]]);
            indices.RemoveAt(pick);
        }
        return result;
    }
}
