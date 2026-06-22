namespace Converter.Lemur.Entities;

/// <summary>
/// CK3's four culture eras (common/culture/eras/00_culture_eras.txt). The integer value is the
/// ladder index used for both the start-relative era-year math and the tech-count gradient
/// (distance below a culture's frontier era). See PLAN_tech_levels.md.
/// </summary>
public enum CultureEra
{
    Tribal = 0,
    EarlyMedieval = 1,
    HighMedieval = 2,
    LateMedieval = 3,
}

/// <summary>
/// One CK3 culture innovation. Two roles, decided by <see cref="PairedTradition"/>:
/// <list type="bullet">
/// <item><b>General pool</b> (<c>PairedTradition == null</c>): drawn in the seeded random per-era
/// draw in <c>TechAssigner</c>. These are the base-game, non-region innovations the matrix is sized
/// on (15 tribal / 14 each later era).</item>
/// <item><b>Freebie</b> (<c>PairedTradition != null</c>): a region/regional/DLC innovation that is
/// NOT in the random pool. Granted only when a culture already holds the paired tradition and has
/// reached the innovation's era — flavour when it comes up, absent otherwise (Mattias's rule).</item>
/// </list>
/// </summary>
public record Innovation(
    string Key,
    CultureEra Era,
    string Group,                   // "military" | "civic" (CK3 culture_group_*)
    string? PairedTradition = null  // non-null ⇒ freebie gated on this tradition, not in the random pool
);
