using Converter.Lemur.Entities;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class FaithWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing faith files");

        var faiths = map.Faiths;
        if (faiths.Count == 0)
        {
            Logger.Warning("FaithWriter: no faiths found, skipping.");
            return;
        }

        // Delete old hardcoded stub files if present
        var oldGermanic = Helper.GetPath(outputDirectory, "common", "religion", "religions", "00_germanic.txt");
        if (File.Exists(oldGermanic)) File.Delete(oldGermanic);
        var oldHolySites = Helper.GetPath(outputDirectory, "common", "religion", "holy_sites", "00_holy_sites.txt");
        if (File.Exists(oldHolySites)) File.Delete(oldHolySites);

        // Group faiths by religion container key.
        // Skip sentinel/placeholder faiths inserted by CharacterFactory (AzgaarId <= 0).
        var byReligion = faiths.Values
            .Where(f => f.AzgaarId > 0)
            .GroupBy(f => f.CK3ReligionKey)
            .OrderBy(g => g.Key)
            .ToList();

        // Build a lookup: cellId → barony, to find holy site counties
        var cellIdToBarony = BuildCellToBaronyLookup(map);

        var t1 = WriteReligionsFile(byReligion, outputDirectory);
        var t2 = WriteHolySitesFile(faiths.Values.Where(f => f.AzgaarId > 0).ToList(), map, cellIdToBarony, outputDirectory);
        var t3 = WriteLocalizationFile(byReligion, outputDirectory);

        await Task.WhenAll(t1, t2, t3);

        Logger.Info($"Wrote {faiths.Count} faiths in {byReligion.Count} religion containers.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // File 1: common/religion/religions/lemur_religions.txt
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task WriteReligionsFile(
        List<IGrouping<string, Faith>> byReligion,
        string outputDirectory)
    {
        var dir = Helper.GetPath(outputDirectory, "common", "religion", "religions");
        Directory.CreateDirectory(dir);

        var lines = new List<string>
        {
            "# Lemur conversion: generated religion/faith definitions.",
            ""
        };

        foreach (var group in byReligion)
        {
            bool anyUnreformed = group.Any(f => f.IsUnreformed);
            var rootFaith = group.OrderBy(f => f.AzgaarId).First();

            lines.Add($"# {rootFaith.Name}");
            lines.Add($"{group.Key} = {{");
            lines.Add("\tfamily = rf_other");
            if (anyUnreformed)
                lines.Add("\tpagan_roots = yes");
            lines.Add("");
            foreach (var (label, doctrine) in PickDoctrines(rootFaith.AzgaarId))
            {
                lines.Add($"\t# {label}");
                lines.Add($"\tdoctrine = {doctrine}");
            }
            lines.Add("");
            lines.Add("\tfaiths = {");

            foreach (var faith in group.OrderBy(f => f.AzgaarId))
            {
                var (r, g, b) = ParseHexColor(faith.HexColor);
                var holySiteKey = $"lemur_site_{faith.AzgaarId}";

                lines.Add($"\t\t# {faith.Name}");
                lines.Add($"\t\t{faith.CK3Key} = {{");
                lines.Add($"\t\t\tcolor = rgb {{ {r} {g} {b} }}");
                lines.Add($"\t\t\ticon = {faith.IconKey}");
                lines.Add($"\t\t\treformed_icon = {faith.IconKey}");
                lines.Add("");
                lines.Add($"\t\t\tholy_site = {holySiteKey}");
                lines.Add("");

                if (faith.IsUnreformed)
                    lines.Add("\t\t\tdoctrine = unreformed_faith_doctrine");

                foreach (var tenet in PickTenets(faith.AzgaarId))
                    lines.Add($"\t\t\tdoctrine = {tenet}");
                lines.Add("\t\t}");
            }

            lines.Add("\t}");
            lines.Add("}");
            lines.Add("");
        }

        var path = Helper.GetPath(dir, "lemur_religions.txt");
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // File 2: common/religion/holy_sites/lemur_holy_sites.txt
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task WriteHolySitesFile(
        List<Faith> faiths,
        L.Map map,
        Dictionary<int, L.Barony> cellIdToBarony,
        string outputDirectory)
    {
        var dir = Helper.GetPath(outputDirectory, "common", "religion", "holy_sites");
        Directory.CreateDirectory(dir);

        var lines = new List<string>
        {
            "# Lemur conversion: generated holy sites.",
            ""
        };

        foreach (var faith in faiths.OrderBy(f => f.AzgaarId))
        {
            var countyId = FindHolySiteCountyId(faith, map, cellIdToBarony);
            var countyKey = $"c_{countyId}";

            lines.Add($"# {faith.Name}");
            lines.Add($"lemur_site_{faith.AzgaarId} = {{");
            lines.Add($"\tcounty = {countyKey}");
            lines.Add("\tcharacter_modifier = {");
            lines.Add("\t}");
            lines.Add("}");
            lines.Add("");
        }

        var path = Helper.GetPath(dir, "lemur_holy_sites.txt");
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // File 3: localization/english/lemur_faiths_l_english.yml
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task WriteLocalizationFile(
        List<IGrouping<string, Faith>> byReligion,
        string outputDirectory)
    {
        var dir = Helper.GetPath(outputDirectory, "localization", "english");
        Directory.CreateDirectory(dir);

        var lines = new List<string> { "l_english:" };

        // Track which religion containers we've already written
        var writtenReligions = new HashSet<string>();

        foreach (var group in byReligion)
        {
            // Write religion container name once (use root faith name)
            var rootFaith = group.OrderBy(f => f.AzgaarId).First();
            if (writtenReligions.Add(group.Key))
            {
                lines.Add($" {group.Key}: \"{rootFaith.Name}\"");
            }

            foreach (var faith in group.OrderBy(f => f.AzgaarId))
            {
                lines.Add($" {faith.CK3Key}: \"{faith.Name}\"");
                lines.Add($" {faith.CK3Key}_adj: \"{faith.Name}\"");
                if (!string.IsNullOrEmpty(faith.Deity))
                    lines.Add($" {faith.CK3Key}_HighGodName: \"{faith.Deity}\"");
            }
        }

        var path = Helper.GetPath(dir, "lemur_faiths_l_english.yml");
        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Doctrine & tenet data
    // Keys sourced from game files: common/religion/doctrines/ (CK3 v1.12.5)
    // Long-term TODO: parse these from Ck3Directory at runtime instead of hardcoding.
    // ─────────────────────────────────────────────────────────────────────────

    // Each entry is (comment label, valid doctrine keys for that group).
    // Order matches the 21 required groups every faith must declare.
    private static readonly (string Label, string[] Options)[] DoctrineGroups =
    [
        ("head of faith",       ["doctrine_no_head", "doctrine_spiritual_head", "doctrine_temporal_head"]),
        ("gender",              ["doctrine_gender_male_dominated", "doctrine_gender_equal", "doctrine_gender_female_dominated"]),
        ("clerical marriage",   ["doctrine_clerical_marriage_allowed", "doctrine_clerical_marriage_disallowed"]),
        ("clerical succession", ["doctrine_clerical_succession_temporal_appointment", "doctrine_clerical_succession_spiritual_appointment", "doctrine_clerical_succession_temporal_fixed_appointment", "doctrine_clerical_succession_spiritual_fixed_appointment"]),
        ("clerical gender",     ["doctrine_clerical_gender_male_only", "doctrine_clerical_gender_female_only", "doctrine_clerical_gender_either"]),
        ("clerical function",   ["doctrine_clerical_function_taxation", "doctrine_clerical_function_alms_and_pacification", "doctrine_clerical_function_recruitment"]),
        ("consanguinity",       ["doctrine_consanguinity_dynastic", "doctrine_consanguinity_restricted", "doctrine_consanguinity_cousins", "doctrine_consanguinity_aunt_nephew_and_uncle_niece", "doctrine_consanguinity_unrestricted"]),
        ("bastardry",           ["doctrine_bastardry_none", "doctrine_bastardry_legitimization", "doctrine_bastardry_all"]),
        ("divorce",             ["doctrine_divorce_disallowed", "doctrine_divorce_approval", "doctrine_divorce_allowed"]),
        ("adultery men",        ["doctrine_adultery_men_crime", "doctrine_adultery_men_shunned", "doctrine_adultery_men_accepted"]),
        ("adultery women",      ["doctrine_adultery_women_crime", "doctrine_adultery_women_shunned", "doctrine_adultery_women_accepted"]),
        ("pluralism",           ["doctrine_pluralism_fundamentalist", "doctrine_pluralism_righteous", "doctrine_pluralism_pluralistic"]),
        ("theism",              ["doctrine_monotheist", "doctrine_polytheist"]),
        ("theocracy",           ["doctrine_theocracy_temporal", "doctrine_theocracy_lay_clergy"]),
        ("kinslaying",          ["doctrine_kinslaying_any_dynasty_member_crime", "doctrine_kinslaying_extended_family_crime", "doctrine_kinslaying_close_kin_crime", "doctrine_kinslaying_shunned", "doctrine_kinslaying_accepted"]),
        ("deviancy",            ["doctrine_deviancy_crime", "doctrine_deviancy_shunned", "doctrine_deviancy_accepted", "doctrine_deviancy_virtuous"]),
        ("homosexuality",       ["doctrine_homosexuality_crime", "doctrine_homosexuality_shunned", "doctrine_homosexuality_accepted"]),
        ("witchcraft",          ["doctrine_witchcraft_crime", "doctrine_witchcraft_shunned", "doctrine_witchcraft_accepted", "doctrine_witchcraft_virtuous"]),
        ("pilgrimage",          ["doctrine_pilgrimage_forbidden", "doctrine_pilgrimage_encouraged", "doctrine_pilgrimage_local_rites", "doctrine_pilgrimage_mandatory"]),
        ("coronation",          ["doctrine_no_anointment", "doctrine_anointment_permitted"]),
        ("funeral",             ["doctrine_funeral_stoic", "doctrine_funeral_bewailment", "doctrine_funeral_cremation", "doctrine_funeral_sky_burial", "doctrine_funeral_mummification", "doctrine_family_rites"]),
    ];

    // All valid tenet keys (CK3 v1.12.5, group doctrine_core_tenets).
    // Long-term TODO: parse from Ck3Directory/game/common/religion/doctrines/30_core_tenets.txt.
    private static readonly string[] AllTenets =
    [
        "tenet_aniconism", "tenet_alexandrian_catechism", "tenet_armed_pilgrimages",
        "tenet_carnal_exaltation", "tenet_communal_identity", "tenet_communion",
        "tenet_consolamentum", "tenet_divine_marriage", "tenet_gnosticism",
        "tenet_mendicant_preachers", "tenet_monasticism", "tenet_pacifism",
        "tenet_pentarchy", "tenet_unrelenting_faith", "tenet_vows_of_poverty",
        "tenet_pastoral_isolation", "tenet_rite", "tenet_adaptive",
        "tenet_esotericism", "tenet_legalism", "tenet_literalism",
        "tenet_reincarnation", "tenet_religious_legal_pronouncements", "tenet_struggle_submission",
        "tenet_false_conversion_sanction", "tenet_tax_nonbelievers", "tenet_asceticism",
        "tenet_bhakti", "tenet_dharmic_pacifism", "tenet_inner_journey",
        "tenet_ritual_hospitality", "tenet_adorcism", "tenet_ancestor_worship",
        "tenet_astrology", "tenet_hedonistic", "tenet_human_sacrifice",
        "tenet_mystical_birthright", "tenet_ritual_celebrations", "tenet_sacred_childbirth",
        "tenet_sanctity_of_nature", "tenet_sun_worship", "tenet_warmonger",
        "tenet_gruesome_festivals", "tenet_cthonic_redoubts", "tenet_household_gods",
        "tenet_sinitic_syncretism", "tenet_eastern_syncretism", "tenet_unreformed_syncretism",
        "tenet_christian_syncretism", "tenet_islamic_syncretism", "tenet_jewish_syncretism",
        "tenet_exaltation_of_pain", "tenet_natural_primitivism", "tenet_pursuit_of_power",
        "tenet_ritual_cannibalism", "tenet_sacred_shadows", "tenet_polyamory",
        "tenet_sacrificial_ceremonies", "tenet_megaliths", "tenet_fp3_fedayeen",
        "tenet_communal_possessions", "tenet_pure_land", "tenet_no_mind",
        "tenet_mountain_worship", "tenet_extinction_of_dharma", "tenet_cranial_trophies",
        "tenet_pursuit_of_knowledge", "tenet_benevolent_governance", "tenet_filial_piety",
        "tenet_harmonious_society", "tenet_sacred_destruction", "tenet_preservation",
        "tenet_takamin",
    ];

    /// <summary>
    /// Picks one doctrine key from each of the 21 required groups, seeded deterministically.
    /// Seed should be derived from the religion container's root faith ID.
    /// </summary>
    private static IEnumerable<(string Label, string Doctrine)> PickDoctrines(int seed)
    {
        var rng = new Random(seed);
        foreach (var (label, options) in DoctrineGroups)
            yield return (label, options[rng.Next(options.Length)]);
    }

    /// <summary>
    /// Picks <paramref name="count"/> unique tenets, seeded deterministically.
    /// Seed should be derived from the individual faith's AzgaarId.
    /// </summary>
    private static string[] PickTenets(int seed, int count = 3)
    {
        var rng = new Random(seed);
        // Fisher-Yates shuffle on a copy, take first <count>
        var pool = (string[])AllTenets.Clone();
        for (int i = pool.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool[..count];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Dictionary<int, L.Barony> BuildCellToBaronyLookup(L.Map map)
    {
        var dict = new Dictionary<int, L.Barony>();
        if (map.Baronies == null) return dict;

        foreach (var barony in map.Baronies)
        {
            foreach (var cell in barony.Cells)
            {
                dict.TryAdd(cell.Id, barony);
            }
        }
        return dict;
    }

    private static int FindHolySiteCountyId(
        Faith faith,
        L.Map map,
        Dictionary<int, L.Barony> cellIdToBarony)
    {
        // Primary: find the barony that owns the origin cell
        if (faith.OriginCellId > 0 && cellIdToBarony.TryGetValue(faith.OriginCellId, out var originBarony))
        {
            if (originBarony.Parent is L.County county)
                return county.Id;
        }

        // Fallback: find the county with the most cells matching this faith's AzgaarId
        if (map.Counties != null && map.Cells != null)
        {
            var bestCounty = map.Counties
                .Select(c => (county: c,
                    count: c.GetAllCells().Count(cell => cell.Religion == faith.AzgaarId)))
                .Where(x => x.count > 0)
                .OrderByDescending(x => x.count)
                .FirstOrDefault();

            if (bestCounty.county != null)
                return bestCounty.county.Id;
        }

        // Last resort: first county
        return map.Counties?.FirstOrDefault()?.Id ?? 1;
    }

    private static (int r, int g, int b) ParseHexColor(string hex)
    {
        // Expects "#rrggbb"
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            int r = Convert.ToInt32(hex[..2], 16);
            int g = Convert.ToInt32(hex[2..4], 16);
            int b = Convert.ToInt32(hex[4..6], 16);
            return (r, g, b);
        }
        return (128, 128, 128); // fallback grey
    }
}
