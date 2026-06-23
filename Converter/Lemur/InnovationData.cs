using Converter.Lemur.Entities;

namespace Converter.Lemur;

/// <summary>
/// Static catalog of CK3 culture innovations used by <c>TechAssigner</c>. Mirrors
/// <see cref="TraditionData"/>.
///
/// <para><b>Scope (deliberately narrow).</b> The <b>general pool</b> is exactly the base-game,
/// non-region innovations from <c>00_{tribal,early,high,late}_innovations.txt</c> — 15/14/14/14
/// per era (57 total). The per-era count matrix agreed with the maintainer is sized on these
/// numbers, so the pool must stay these and only these. Region-gated, regional men-at-arms, and
/// DLC innovations are NOT in the random pool — a "full" tribal culture in-game never holds the
/// inflated 20, only the ~15 general ones.</para>
///
/// <para><b>Freebies live elsewhere.</b> Region-gated innovations granted by a per-culture criterion
/// (longboats, african_canoes, elephantry, war_camels, wootz_steel) are in <c>FreebieData</c> and
/// applied by <c>FreebieAssigner</c> — they are NOT in this pool. The hyper-regional innovations
/// (reconquista, ghilman, stem_duchies, the cultural MaA set, …) are intentionally omitted everywhere.</para>
///
/// Source: CK3 1.19 <c>common/culture/innovations/</c>. DLC innovations are excluded to keep the
/// random draw DLC-noise-free and the matrix exact.
/// </summary>
public static class InnovationData
{
    public static readonly Innovation[] All =
    [
        // ═══════════════════════════════════════════════════════════════════════
        // Tribal — general pool (00_tribal_innovations.txt, non-region): 15
        // ═══════════════════════════════════════════════════════════════════════
        new("innovation_motte",              CultureEra.Tribal, "military"),
        new("innovation_catapult",           CultureEra.Tribal, "military"),
        new("innovation_barracks",           CultureEra.Tribal, "military"),
        new("innovation_mustering_grounds",  CultureEra.Tribal, "military"),
        new("innovation_bannus",             CultureEra.Tribal, "military"),
        new("innovation_quilted_armor",      CultureEra.Tribal, "military"),
        new("innovation_development_01",     CultureEra.Tribal, "civic"),
        new("innovation_currency_01",        CultureEra.Tribal, "civic"),
        new("innovation_gavelkind",          CultureEra.Tribal, "civic"),
        new("innovation_crop_rotation",      CultureEra.Tribal, "civic"),
        new("innovation_city_planning",      CultureEra.Tribal, "civic"),
        new("innovation_casus_belli",        CultureEra.Tribal, "civic"),
        new("innovation_plenary_assemblies", CultureEra.Tribal, "civic"),
        new("innovation_ledger",             CultureEra.Tribal, "civic"),
        new("innovation_table_of_princes",   CultureEra.Tribal, "civic"),

        // ═══════════════════════════════════════════════════════════════════════
        // Early medieval — general pool (00_early_medieval_innovations.txt, non-region): 14
        // ═══════════════════════════════════════════════════════════════════════
        new("innovation_battlements",       CultureEra.EarlyMedieval, "military"),
        new("innovation_mangonel",          CultureEra.EarlyMedieval, "military"),
        new("innovation_burhs",             CultureEra.EarlyMedieval, "military"),
        new("innovation_house_soldiers",    CultureEra.EarlyMedieval, "military"),
        new("innovation_horseshoes",        CultureEra.EarlyMedieval, "military"),
        new("innovation_arched_saddle",     CultureEra.EarlyMedieval, "military"),
        new("innovation_hereditary_rule",   CultureEra.EarlyMedieval, "civic"),
        new("innovation_manorialism",       CultureEra.EarlyMedieval, "civic"),
        new("innovation_development_02",    CultureEra.EarlyMedieval, "civic"),
        new("innovation_currency_02",       CultureEra.EarlyMedieval, "civic"),
        new("innovation_royal_prerogative", CultureEra.EarlyMedieval, "civic"),
        new("innovation_chronicle_writing", CultureEra.EarlyMedieval, "civic"),
        new("innovation_armilary_sphere",   CultureEra.EarlyMedieval, "civic"),
        new("innovation_baliffs",           CultureEra.EarlyMedieval, "civic"),

        // ═══════════════════════════════════════════════════════════════════════
        // High medieval — general pool (00_high_medieval_innovations.txt, non-region): 14
        // ═══════════════════════════════════════════════════════════════════════
        new("innovation_hoardings",         CultureEra.HighMedieval, "military"),
        new("innovation_trebuchet",         CultureEra.HighMedieval, "military"),
        new("innovation_castle_baileys",    CultureEra.HighMedieval, "military"),
        new("innovation_men_at_arms",       CultureEra.HighMedieval, "military"),
        new("innovation_knighthood",        CultureEra.HighMedieval, "military"),
        new("innovation_advanced_bowmaking", CultureEra.HighMedieval, "military"),
        new("innovation_heraldry",          CultureEra.HighMedieval, "civic"),
        new("innovation_windmills",         CultureEra.HighMedieval, "civic"),
        new("innovation_divine_right",      CultureEra.HighMedieval, "civic"),
        new("innovation_land_grants",       CultureEra.HighMedieval, "civic"),
        new("innovation_scutage",           CultureEra.HighMedieval, "civic"),
        new("innovation_guilds",            CultureEra.HighMedieval, "civic"),
        new("innovation_development_03",    CultureEra.HighMedieval, "civic"),
        new("innovation_currency_03",       CultureEra.HighMedieval, "civic"),

        // ═══════════════════════════════════════════════════════════════════════
        // Late medieval — general pool (00_late_medieval_innovations.txt, non-region): 14
        // ═══════════════════════════════════════════════════════════════════════
        new("innovation_machicolations",    CultureEra.LateMedieval, "military"),
        new("innovation_gunpowder",         CultureEra.LateMedieval, "military"),
        new("innovation_royal_armory",      CultureEra.LateMedieval, "military"),
        new("innovation_standing_armies",   CultureEra.LateMedieval, "military"),
        new("innovation_sappers",           CultureEra.LateMedieval, "military"),
        new("innovation_plate_armor",       CultureEra.LateMedieval, "military"),
        new("innovation_primogeniture",     CultureEra.LateMedieval, "civic"),
        new("innovation_cranes",            CultureEra.LateMedieval, "civic"),
        new("innovation_noblesse_oblige",   CultureEra.LateMedieval, "civic"),
        new("innovation_rightful_ownership", CultureEra.LateMedieval, "civic"),
        new("innovation_ermine_cloaks",     CultureEra.LateMedieval, "civic"),
        new("innovation_court_officials",   CultureEra.LateMedieval, "civic"),
        new("innovation_development_04",    CultureEra.LateMedieval, "civic"),
        new("innovation_currency_04",       CultureEra.LateMedieval, "civic"),
    ];

    /// <summary>The seeded-random draw pool for one era (base-game, non-region innovations).</summary>
    public static IReadOnlyList<Innovation> GeneralPool(CultureEra era) =>
        All.Where(i => i.Era == era).ToList();

    /// <summary>Size of an era's general pool (the matrix denominators: 15/14/14/14).</summary>
    public static int GeneralPoolSize(CultureEra era) => GeneralPool(era).Count;
}
