using Converter.Lemur.Deserialization;

namespace Converter.Lemur.Entities;

public static class CultureManager
{
    private static readonly string[] Ethoses =
    [
        "ethos_bellicose", "ethos_stoic", "ethos_bureaucratic",
        "ethos_communal", "ethos_egalitarian", "ethos_spiritual", "ethos_courtly"
    ];

    private static readonly string[] MartialCustoms =
    [
        "martial_custom_male_only", "martial_custom_equal", "martial_custom_female_only"
    ];

    // 14 phenotype sets — all keys are valid vanilla base-game ethnicity keys.
    private static readonly string[][] EthnicitySets =
    [
        ["caucasian_blond", "caucasian_brown_hair", "caucasian_dark_hair", "caucasian_ginger"],
        ["caucasian_northern_blond", "caucasian_northern_brown_hair", "caucasian_northern_dark_hair", "caucasian_northern_ginger"],
        ["mediterranean", "mediterranean_byzantine"],
        ["slavic_blond", "slavic_brown_hair", "slavic_dark_hair", "slavic_ginger"],
        ["slavic_northern_blond", "slavic_northern_brown_hair", "slavic_northern_dark_hair", "slavic_northern_ginger"],
        ["arab"],
        ["african", "east_african"],
        ["asian_han_chinese", "asian_japanese", "asian_manchu_korean"],
        ["asian_mongol", "turkic", "turkic_west"],
        ["indian", "south_indian"],
        ["asian", "asian_tibetan", "asian_malay", "asian_austronesian"],
        ["circumpolar_blonde_hair", "circumpolar_brown_hair", "circumpolar_dark_hair"],
        ["caucasian_blond", "caucasian_brown_hair", "slavic_blond", "slavic_dark_hair"],
        ["papuan"],
    ];

    // Thematically coherent packs: GFX keys + vanilla name list. Male/Female names per bundle are
    // sourced from vanilla name_list_*.txt at startup via NameListLoader. The bare identifiers in
    // vanilla male_names/female_names blocks (e.g. O_lafr, T_orsteinn, E_lfric) double as the
    // localization keys CK3 resolves at runtime to the proper Unicode display form, so we pass them
    // through unchanged into name = "..." in character history.
    //
    // To add a new bundle: pick any top-level name_list_* key that exists in
    // <Ck3Directory>/game/common/culture/name_lists/ and add a Bundle(...) line. NameListLoader
    // throws on unknown keys with a list of available ones — no separate allowlist to maintain.
    //
    // Lazy init: Settings.Instance.Ck3Directory must be populated before first read.
    private static ThemeBundle[]? _themeBundles;
    private static ThemeBundle[] ThemeBundles => _themeBundles ??= BuildThemeBundles();

    private static ThemeBundle[] BuildThemeBundles()
    {
        var ck3 = Converter.Settings.Instance.Ck3Directory;
        ThemeBundle Bundle(string name, string coa, string building, string clothing, string unit, string nameList)
        {
            var (male, female) = NameListLoader.GetNames(ck3, nameList);
            // Empty pools would crash CharacterFactory on modulo. Substitute a single placeholder
            // so the converter completes and the issue is glaringly obvious in-game ("Nameless"
            // rulers everywhere of that culture) rather than a stack trace mid-run.
            // Vanilla CK3 only defines male_names / female_names — no gender-neutral pool exists
            // (verified 2026-05-20), so empty here means the chosen name_list lacks names of that gender.
            male = SubstituteIfEmpty(male, name, nameList, "male_names");
            female = SubstituteIfEmpty(female, name, nameList, "female_names");
            return new(name, coa, building, clothing, unit, nameList, male, female);
        }
        return
        [
            Bundle("western",   "western_coa_gfx",          "western_building_gfx",   "western_clothing_gfx",   "western_unit_gfx",  "name_list_english"),
            Bundle("byzantine", "byzantine_group_coa_gfx",  "byzantine_building_gfx", "byzantine_clothing_gfx", "eastern_unit_gfx",  "name_list_greek"),
            Bundle("mena",      "mena_coa_gfx",             "african_building_gfx",   "mena_clothing_gfx",      "eastern_unit_gfx",  "name_list_bedouin"),
            Bundle("northern",  "western_coa_gfx",          "western_building_gfx",   "northern_clothing_gfx",  "western_unit_gfx",  "name_list_norse"),
        ];
    }

    public static Dictionary<int, Culture> Build(AzgaarCulture[] cultures, int seed, int startYear)
    {
        Converter.Lemur.Logger.Section("Building cultures");
        var result = new Dictionary<int, Culture>();

        // Pass 1: create all Culture objects, skip sentinel (i == 0)
        foreach (var azc in cultures)
        {
            if (azc.i == 0) continue;

            var rng = new Random(Helper.MixSeeds(seed, azc.i));
            var culture = new Culture
            {
                AzgaarId    = azc.i,
                Name        = azc.name,
                HexColor    = azc.color ?? "#808080",
                Ethos        = Ethoses[rng.Next(Ethoses.Length)],
                MartialCustom = MartialCustoms[rng.Next(MartialCustoms.Length)],
                ThemeBundle  = ThemeBundles[rng.Next(ThemeBundles.Length)],
            };
            culture.AssignEthnicity(EthnicitySets[rng.Next(EthnicitySets.Length)], rng);
            result[azc.i] = culture;
        }

        // Pass 2: assign Heritage and Language in BFS topological order
        // (parents before children so children can inherit)
        AssignPillarsTopological(cultures, result, seed);

        // Traditions are NOT assigned here. They are deferred to CultureTraditionAssigner,
        // which runs after BaronyTerrainAssigner so it can gate on canonical Barony.Ck3Terrain.
        // Build leaves Culture.Traditions empty; nothing between here and CultureWriter reads it.

        // Pass 4: wire Parents list (CK3 keys of direct parents)
        foreach (var azc in cultures)
        {
            if (azc.i == 0) continue;
            if (!result.TryGetValue(azc.i, out var culture)) continue;

            var realOrigins = GetRealOrigins(azc.origins);
            foreach (var pid in realOrigins)
                if (result.TryGetValue(pid, out var parentCulture))
                    culture.Parents.Add(parentCulture);
        }

        // Pass 5: assign creation dates
        // Foundational cultures (no parents) are ancient — no created date.
        AssignCreationDates(cultures, result, startYear);

        var sb = new System.Text.StringBuilder($"Assigned pillars to {result.Count} cultures (traditions assigned later by CultureTraditionAssigner).");
        foreach (var c in result.Values.OrderBy(c => c.AzgaarId))
            sb.Append($"\n- {c.Name} (id {c.AzgaarId}): heritage={c.Heritage}, language={c.Language}, ethos={c.Ethos}");
        Converter.Lemur.Logger.Info(sb.ToString());
        Converter.Lemur.Logger.Info("Cultures done.");

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pillar assignment
    // ─────────────────────────────────────────────────────────────────────────

    private static void AssignPillarsTopological(
        AzgaarCulture[] cultures,
        Dictionary<int, Culture> result,
        int seed)
    {
        // Build parent lookup: cultureId → direct parent ids
        var parentIds = new Dictionary<int, int[]>();
        foreach (var azc in cultures)
        {
            if (azc.i == 0) continue;
            parentIds[azc.i] = GetRealOrigins(azc.origins);
        }

        // BFS from foundational (no real parents) outward
        var visited = new HashSet<int>();
        var queue = new Queue<int>();

        // Seed with foundational cultures
        foreach (var azc in cultures)
        {
            if (azc.i == 0) continue;
            if (parentIds[azc.i].Length == 0)
                queue.Enqueue(azc.i);
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!visited.Add(id)) continue;
            if (!result.TryGetValue(id, out var culture)) continue;

            var origins = parentIds[id];
            var rng = new Random(Helper.MixSeeds(seed, id, 42)); // different salt for pillars

            if (origins.Length == 0)
            {
                // Foundational: own heritage and language
                culture.Heritage = $"lemur_heritage_{id}";
                culture.Language = $"lemur_language_{id}";
            }
            else if (origins.Length == 1)
            {
                // Derived: inherit from parent
                if (result.TryGetValue(origins[0], out var parent))
                {
                    culture.Heritage = parent.Heritage;
                    culture.Language = parent.Language;
                    // Inherit ethos + martial custom with mutation chance
                    float mutationRate = Converter.Settings.Instance.DoctrineMutationRate;
                    if (rng.NextDouble() < mutationRate)
                        culture.Ethos = Ethoses[rng.Next(Ethoses.Length)];
                    else
                        culture.Ethos = parent.Ethos;
                    if (rng.NextDouble() < mutationRate)
                        culture.MartialCustom = MartialCustoms[rng.Next(MartialCustoms.Length)];
                    else
                        culture.MartialCustom = parent.MartialCustom;
                }
                else
                {
                    culture.Heritage = $"lemur_heritage_{id}";
                    culture.Language = $"lemur_language_{id}";
                }
            }
            else
            {
                // Hybrid: heritage from one parent, language from the other
                if (origins.Length > 2)
                    Converter.Lemur.Logger.Warning($"Culture {culture.Name} (id {id}) has {origins.Length} origins; taking first 2.");

                var pa = origins[0];
                var pb = origins[1];
                result.TryGetValue(pa, out var parentA);
                result.TryGetValue(pb, out var parentB);

                culture.Heritage = parentA?.Heritage ?? $"lemur_heritage_{id}";
                culture.Language = parentB?.Language ?? $"lemur_language_{id}";
                // Hybrid ethos: from one of the parents
                culture.Ethos = (rng.Next(2) == 0 ? parentA?.Ethos : parentB?.Ethos) ?? Ethoses[rng.Next(Ethoses.Length)];
                culture.MartialCustom = (rng.Next(2) == 0 ? parentA?.MartialCustom : parentB?.MartialCustom) ?? MartialCustoms[rng.Next(MartialCustoms.Length)];
                // Hybrid ethnicity: blend both parents' distributions
                var ea = parentA?.Ethnicity ?? [];
                var eb = parentB?.Ethnicity ?? [];
                if (ea.Count > 0 || eb.Count > 0)
                    culture.Ethnicity = Culture.BlendEthnicities(ea, eb);
            }

            // Enqueue children
            foreach (var candidate in result.Keys)
            {
                if (visited.Contains(candidate)) continue;
                if (parentIds.TryGetValue(candidate, out var pids) && pids.Contains(id))
                    queue.Enqueue(candidate);
            }
        }

        // Any unvisited cultures (cycles or missing parents) get own pillars
        foreach (var culture in result.Values)
        {
            if (string.IsNullOrEmpty(culture.Heritage))
                culture.Heritage = $"lemur_heritage_{culture.AzgaarId}";
            if (string.IsNullOrEmpty(culture.Language))
                culture.Language = $"lemur_language_{culture.AzgaarId}";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void AssignCreationDates(AzgaarCulture[] cultures, Dictionary<int, Culture> result, int startYear)
    {
        var parentIds = new Dictionary<int, int[]>();
        foreach (var azc in cultures)
        {
            if (azc.i == 0) continue;
            parentIds[azc.i] = GetRealOrigins(azc.origins);
        }

        // Compute depth as max(parent depths) + 1 so hybrid cultures (two parents)
        // are always strictly younger than BOTH parents, not just the first visited.
        var depth = new Dictionary<int, int>();

        // Foundational cultures start at depth 0
        foreach (var azc in cultures)
        {
            if (azc.i == 0) continue;
            if (parentIds[azc.i].Length == 0)
                depth[azc.i] = 0;
        }

        // Iterative relaxation: keep updating depths until stable.
        // A culture's depth = max(parent depths) + 1.
        // Converges in at most O(tree height) passes; cycle guard via max iterations.
        bool changed = true;
        int maxIterations = result.Count + 1;
        while (changed && maxIterations-- > 0)
        {
            changed = false;
            foreach (var azc in cultures)
            {
                if (azc.i == 0) continue;
                var pids = parentIds[azc.i];
                if (pids.Length == 0) continue; // foundational, already set

                // Only assign if ALL parents have a known depth
                if (!pids.All(pid => depth.ContainsKey(pid))) continue;

                int newDepth = pids.Max(pid => depth[pid]) + 1;
                if (!depth.TryGetValue(azc.i, out int existing) || existing != newDepth)
                {
                    depth[azc.i] = newDepth;
                    changed = true;
                }
            }
        }

        // Any cultures still unresolved (cycles) fall back to depth 1
        foreach (var id in result.Keys)
            if (!depth.ContainsKey(id))
                depth[id] = 1;

        // Invert: deepest culture is created 100 years before start date (startYear),
        // each level up adds another 100 years. Foundational (depth 0) = no date.
        const int stepYears = 100;
        int maxDepth = depth.Values.DefaultIfEmpty(0).Max();

        foreach (var culture in result.Values)
        {
            if (!depth.TryGetValue(culture.AzgaarId, out int d)) d = 1; // unvisited = treat as derived
            if (d == 0)
            {
                culture.CreationDate = null; // foundational: ancient, no created date
                continue;
            }
            // depth maxDepth → startYear - stepYears
            // depth 1        → startYear - (maxDepth * stepYears)
            int year = startYear - ((maxDepth - d + 1) * stepYears);
            culture.CreationDate = $"{year}.1.1";
        }
    }

    /// <summary>
    /// Returns <paramref name="pool"/> if non-empty; otherwise logs at Error level and returns
    /// a single-entry placeholder pool so the converter keeps running and the issue is visible
    /// in-game (rulers of affected cultures all named "Nameless").
    /// </summary>
    private static string[] SubstituteIfEmpty(string[] pool, string bundleName, string nameList, string blockName)
    {
        if (pool.Length > 0) return pool;
        Logger.Error(
            $"ThemeBundle '{bundleName}' (NameList={nameList}): vanilla '{blockName}' is empty. " +
            $"Substituting [\"Nameless\"] placeholder so the converter completes — pick a different " +
            $"name_list with both male_names and female_names, or extend the loader to merge pools.");
        return ["Nameless"];
    }

    /// <summary>Extract non-zero, non-null origin IDs. Returns at most 2.</summary>
    private static int[] GetRealOrigins(int[]? origins)
    {
        if (origins == null) return [];
        return origins.Where(o => o != 0).Take(2).ToArray();
    }
}

/// <summary>
/// A thematically coherent pack of vanilla base-game CK3 content keys assigned to a culture.
/// Covers GFX (CoA, buildings, clothing, units) and a name list.
/// Future: match bundle to Azgaar nameBase string for thematic coherence.
/// </summary>
public record ThemeBundle(
    string Name,
    string CoaGfx,
    string BuildingGfx,
    string ClothingGfx,
    string UnitGfx,
    string NameList,
    string[] MaleNames,
    string[] FemaleNames
);
