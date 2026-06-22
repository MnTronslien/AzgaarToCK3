namespace Converter.Lemur.Entities;

public class Culture
{
    public int AzgaarId { get; set; }
    public string Name { get; set; } = "";
    public string CK3Key => $"lemur_culture_{AzgaarId}";
    public string HexColor { get; set; } = "#808080";

    // Pillars
    public string Ethos { get; set; } = "ethos_communal";
    public string Heritage { get; set; } = "";
    public string Language { get; set; } = "";
    public string MartialCustom { get; set; } = "martial_custom_male_only";
    public string HeadDetermination { get; set; } = "head_determination_domain";
    // Theme bundle — GFX keys + vanilla name list
    public ThemeBundle ThemeBundle { get; set; } = null!;

    public List<string> Traditions { get; set; } = new();
    /// <summary>Direct references to parent cultures (0, 1, or 2 entries)</summary>
    public List<Culture> Parents { get; set; } = new();

    /// <summary>
    /// Resolved culture era (baseline <c>WorldTechLevel</c> ± rank-based development variance, or the
    /// highest parent era for hybrids). Set by <c>TechAssigner</c>; emitted as <c>join_era</c>.
    /// </summary>
    public CultureEra Era { get; set; } = CultureEra.Tribal;

    /// <summary>
    /// Innovations this culture starts with — general-pool picks plus any tradition-paired freebies.
    /// Set by <c>TechAssigner</c>; emitted one <c>discover_innovation</c> each by CultureHistoryWriter.
    /// </summary>
    public List<Innovation> Innovations { get; set; } = new();

    /// <summary>
    /// Creation date for derived/hybrid cultures. Null for foundational cultures (ancient, no created date needed).
    /// Written as `created = DATE` in culture history.
    /// </summary>
    public string? CreationDate { get; set; } = null;

    /// <summary>Ethnicity weights for the CK3 `ethnicities` block. Weights sum to 100.</summary>
    public List<(int Weight, string Key)> Ethnicity { get; set; } = [];

    /// <summary>
    /// Assign a random ethnicity distribution from the given phenotype keys.
    /// Generates random weights, normalises to sum exactly 100, and stores them.
    /// </summary>
    public void AssignEthnicity(string[] keys, Random rng)
    {
        var raw = keys.Select(_ => rng.Next(1, 101)).ToArray();
        int total = raw.Sum();
        var weights = raw.Select(w => (int)Math.Round((double)w / total * 100)).ToList();
        // Fix integer rounding so weights always sum to exactly 100
        int diff = 100 - weights.Sum();
        int maxIdx = weights.IndexOf(weights.Max());
        weights[maxIdx] += diff;
        Ethnicity = keys.Zip(weights, (k, w) => (w, k)).ToList();
    }

    /// <summary>
    /// Blend two ethnicity lists into one. Shared keys have their weights summed;
    /// unique keys are kept as-is. Result is normalised to sum exactly 100.
    /// Returns the non-empty list unchanged if the other is empty.
    /// </summary>
    public static List<(int Weight, string Key)> BlendEthnicities(
        List<(int Weight, string Key)> a,
        List<(int Weight, string Key)> b)
    {
        if (a.Count == 0) return b;
        if (b.Count == 0) return a;

        var merged = a.ToDictionary(e => e.Key, e => e.Weight);
        foreach (var (weight, key) in b)
        {
            if (merged.ContainsKey(key)) merged[key] += weight;
            else                         merged[key]  = weight;
        }

        int total   = merged.Values.Sum();
        var keys    = merged.Keys.ToList();
        var weights = merged.Values.Select(w => (int)Math.Round((double)w / total * 100)).ToList();
        int diff    = 100 - weights.Sum();
        weights[weights.IndexOf(weights.Max())] += diff;
        return keys.Zip(weights, (k, w) => (w, k)).ToList();
    }
}
