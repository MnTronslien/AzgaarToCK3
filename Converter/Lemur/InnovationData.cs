using Converter.Lemur.Entities;

namespace Converter.Lemur;

/// <summary>
/// Static catalog of CK3 culture innovations used by <c>TechAssigner</c>. Mirrors
/// <see cref="TraditionData"/>.
///
/// <para><b>Scope.</b> The <b>general pool</b> is the base-game non-region innovations plus a handful
/// of <i>demoted</i> region innovations whose effects are generic stat buffs with no unique unlock
/// (e.g. ghilman, seigneurialism, condottieri) — fine for any culture to roll. Region innovations
/// with a unique unlock (MaA/building/naval) stay OUT of the pool and are gated in <c>FreebieData</c>.
/// The own-era count is held absolute (<c>InnovationsInOwnEra</c>=3) so growing the pool doesn't
/// inflate a culture's own-era tech.</para>
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
        // innovation_table_of_princes intentionally OMITTED: it unlocks single-heir-dynasty-house law,
        // a mechanic we don't want to hand out blindly. (It's also potential-gated to czech/slovien,
        // but per our force-grant model that gate is irrelevant — this omission is a judgement call.)

        // Demoted from region-locked → general pool: a generic effect with no unique unlock, so it's
        // fine for any culture to roll. all_things is FP1 content (unknown key / no-op on non-FP1
        // installs — harmless error-log noise; applies normally where FP1 is owned).
        new("innovation_all_things",         CultureEra.Tribal, "civic"),

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
        // Demoted region innovations (generic effects, no unique unlock):
        new("innovation_ghilman",           CultureEra.EarlyMedieval, "military"),
        new("innovation_stem_duchies",      CultureEra.EarlyMedieval, "civic"),

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
        // Demoted region innovations (generic effects, no unique unlock):
        new("innovation_east_settling",     CultureEra.HighMedieval, "civic"),
        new("innovation_french_peerage",    CultureEra.HighMedieval, "civic"),
        new("innovation_muladi",            CultureEra.HighMedieval, "civic"),
        new("innovation_seigneurialism",    CultureEra.HighMedieval, "civic"),

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
        // Demoted region innovations (generic effects, no unique unlock):
        new("innovation_condottieri",       CultureEra.LateMedieval, "military"),
        new("innovation_deccan_unity",      CultureEra.LateMedieval, "civic"),
        new("innovation_wierdijks",         CultureEra.LateMedieval, "civic"),
    ];

    /// <summary>The seeded-random draw pool for one era (base-game, non-region innovations).</summary>
    public static IReadOnlyList<Innovation> GeneralPool(CultureEra era) =>
        All.Where(i => i.Era == era).ToList();

    /// <summary>Size of an era's general pool. Grows as region innovations are demoted in; the
    /// own-era count is held absolute (InnovationsInOwnEra) so it stays 3 regardless of pool size.</summary>
    public static int GeneralPoolSize(CultureEra era) => GeneralPool(era).Count;
}
