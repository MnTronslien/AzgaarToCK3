using Converter.Lemur.Entities;

namespace Converter.Lemur;

/// <summary>
/// Registry of "freebie" innovations — region-gated CK3 innovations that are NOT in the random draw
/// (<see cref="InnovationData"/>) and are granted by <see cref="FreebieAssigner"/> when a per-culture
/// <see cref="Criterion"/> passes. Mirrors the gate-lambda pattern of <c>TraditionData</c> / the
/// splatmap material registry: each lambda returns a plain true/false.
///
/// <para><b>Order matters.</b> The assigner evaluates entries in array order and threads the set of
/// keys already granted to the culture into the criterion, so a later entry can test against an
/// earlier one. <c>longboats</c> is listed before <c>african_canoes</c> because canoes is the
/// strictly-lesser river-sailing tech (both grant <c>unlocks_sailable_major_rivers</c> +
/// <c>unlocks_naval_raiding</c>; longboats adds adventure intent, a bigger embarkation discount and
/// naval speed) — so a culture that earns longboats must NOT also get canoes.</para>
///
/// <para>The river-sailing signal keys off the author-set <see cref="AzgaarCultureType.River"/> (which
/// the map author controls) rather than randomly-assigned traditions; the Norse case (a Generic-type
/// culture) is still covered by the <c>hird</c>/<c>fp1_coastal_warriors</c> tradition path.</para>
/// </summary>
public static class FreebieData
{
    /// <param name="Innovation">The innovation granted (key/era/group), added verbatim to the culture.</param>
    /// <param name="Criterion">(culture, keys-already-granted) → should this culture get it.</param>
    public record FreebieInnovation(
        Innovation Innovation,
        Func<Culture, IReadOnlySet<string>, bool> Criterion);

    private static bool HasTradition(Culture c, string key) => c.Traditions.Contains(key);

    public static readonly FreebieInnovation[] All =
    [
        // Longboats FIRST (canoes tests against it). Naval-river raiding identity:
        // explicit viking traditions (hird / FP1 coastal warriors, incl. the DLC-missing hird
        // fallback), OR an author-marked River culture that is bellicose.
        new(new("innovation_longboats", CultureEra.Tribal, "military"),
            (c, _) => HasTradition(c, "tradition_hird")
                   || HasTradition(c, "tradition_fp1_coastal_warriors")
                   || (c.Type == AzgaarCultureType.River && c.Ethos == "ethos_bellicose")),

        // African canoes: the lesser river-sailing tech. Any River culture that did NOT earn longboats.
        new(new("innovation_african_canoes", CultureEra.Tribal, "military"),
            (c, granted) => c.Type == AzgaarCultureType.River
                         && !granted.Contains("innovation_longboats")),

        // Tradition-signalled freebies (unchanged behaviour, now expressed as criteria).
        new(new("innovation_elephantry", CultureEra.Tribal, "military"),
            (c, _) => HasTradition(c, "tradition_lords_of_the_elephant")),
        new(new("innovation_war_camels", CultureEra.Tribal, "military"),
            (c, _) => HasTradition(c, "tradition_desert_nomads")),
        new(new("innovation_wootz_steel", CultureEra.Tribal, "civic"),
            (c, _) => HasTradition(c, "tradition_metal_craftsmanship")),
    ];
}
