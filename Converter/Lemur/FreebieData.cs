using Converter.Lemur.Entities;
using Converter.Lemur.Fields;
using Converter.Lemur.Provinces;

namespace Converter.Lemur;

/// <summary>
/// Registry of "freebie" innovations — region-locked CK3 innovations with a UNIQUE unlock (men-at-arms,
/// building, naval ability) that are NOT in the random pool (<see cref="InnovationData"/>) and are
/// granted by <see cref="FreebieAssigner"/> when a per-culture <see cref="FreebieEntry.Criterion"/>
/// passes. Mirrors the gate-lambda pattern of <c>TraditionData</c>. Region innovations with merely
/// generic stat effects are demoted into the general pool instead; truly ungrantable ones are omitted.
///
/// <para><b>Force-grant model:</b> we hand these out by judgement via <c>discover_innovation</c>. CK3's
/// own <c>region</c>/<c>potential</c> gates (which govern natural research) are irrelevant to that and
/// do NOT factor into eligibility here — if a criterion says a culture should have it, we grant it and
/// rely on the error log to flag any that the engine refuses.</para>
///
/// <para><b>Signals:</b> the author-set <see cref="AzgaarCultureType"/> and the culture's terrain
/// profile (<see cref="CultureContext"/>, "how much desert/jungle/…") plus ethos/traditions. Order
/// matters: entries evaluate top-down with the running granted-set threaded in, so <c>longboats</c>
/// (the strictly-greater river tech) is listed before <c>african_canoes</c> and a culture that earns
/// longboats won't also get canoes.</para>
/// </summary>
public static class FreebieData
{
    /// <summary>Per-culture facts a criterion can read.</summary>
    public readonly record struct Ctx(Culture Culture, CultureContext Terrain, IReadOnlySet<string> Granted)
    {
        public bool HasTradition(string key) => Culture.Traditions.Contains(key);
        public bool Ethos(string e) => Culture.Ethos == e;
        public bool IsType(AzgaarCultureType t) => Culture.Type == t;
        public bool Has(string innovationKey) => Granted.Contains(innovationKey);
        /// <summary>≥ <paramref name="threshold"/> (default 20%) of land is one of the listed terrains.</summary>
        public bool Terrains(params Ck3Terrain[] terrains) => Terrain.TerrainAtLeast(Threshold, terrains) > 0f;
        private const float Threshold = 0.20f;
    }

    public record FreebieEntry(Innovation Innovation, Func<Ctx, bool> Criterion);

    public static readonly FreebieEntry[] All =
    [
        // ── Naval river-sailing (longboats FIRST: canoes tests against it) ──
        new(new("innovation_longboats", CultureEra.Tribal, "military"),
            c => c.HasTradition("tradition_hird")
              || c.HasTradition("tradition_fp1_coastal_warriors")
              || (c.IsType(AzgaarCultureType.River) && c.Ethos("ethos_bellicose"))),
        new(new("innovation_african_canoes", CultureEra.Tribal, "military"),
            c => c.IsType(AzgaarCultureType.River) && !c.Has("innovation_longboats")),

        // ── Unique unlocks: tradition signal OR matching terrain ──
        new(new("innovation_war_camels", CultureEra.Tribal, "military"),
            c => c.HasTradition("tradition_desert_nomads") || c.Terrains(Ck3Terrain.Desert, Ck3Terrain.Drylands)),
        new(new("innovation_elephantry", CultureEra.Tribal, "military"),
            c => c.HasTradition("tradition_lords_of_the_elephant") || c.Terrains(Ck3Terrain.Jungle)),
        new(new("innovation_wootz_steel", CultureEra.Tribal, "civic"),
            c => c.HasTradition("tradition_metal_craftsmanship")),

        // ── Regional men-at-arms unlocks: a warlike culture on the matching terrain.
        //    Terrain picks are fantasy approximations of each unit's homeland — easy to retune. ──
        new(new("innovation_caballeros", CultureEra.Tribal, "military"),       // heavy cavalry (Iberia)
            c => c.Ethos("ethos_bellicose") && c.Terrains(Ck3Terrain.Drylands, Ck3Terrain.Hills)),
        new(new("innovation_sahel_horsemen", CultureEra.Tribal, "military"),   // light cavalry (Sahel)
            c => c.Ethos("ethos_bellicose") && c.Terrains(Ck3Terrain.Desert, Ck3Terrain.Drylands)),
        new(new("innovation_bamboo_bows", CultureEra.Tribal, "military"),      // archers (India/SE Asia)
            c => c.Ethos("ethos_bellicose") && c.Terrains(Ck3Terrain.Jungle)),
        new(new("innovation_tiefutu", CultureEra.Tribal, "military"),          // heavy infantry (NE Asia)
            c => c.Ethos("ethos_bellicose") && c.Terrains(Ck3Terrain.Taiga, Ck3Terrain.Mountains)),
        new(new("innovation_hobbies", CultureEra.HighMedieval, "military"),    // hobelar light horse (Britain)
            c => c.Ethos("ethos_bellicose") && c.Terrains(Ck3Terrain.Hills, Ck3Terrain.Wetlands)),
        new(new("innovation_zweihanders", CultureEra.LateMedieval, "military"), // landsknecht (Germany)
            c => c.Ethos("ethos_bellicose") && c.Terrains(Ck3Terrain.Forest, Ck3Terrain.Hills)),

        // ── Reconquista: holy-war flavour. Ethos is single-valued, so "bellicose+spiritual" is read
        //    as either (a warlike OR zealous culture). Kept gated rather than pooled. ──
        new(new("innovation_reconquista", CultureEra.EarlyMedieval, "military"),
            c => c.Ethos("ethos_bellicose") || c.Ethos("ethos_spiritual")),
    ];
}
