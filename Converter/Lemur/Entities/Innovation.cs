namespace Converter.Lemur.Entities;

/// <summary>
/// CK3's four culture eras (common/culture/eras/00_culture_eras.txt). The integer value is the
/// ladder index used for both the start-relative era-year math and the tech-count gradient
/// (distance below a culture's own era). See docs/CONVERSION_RULES.md.
/// </summary>
public enum CultureEra
{
    Tribal = 0,
    EarlyMedieval = 1,
    HighMedieval = 2,
    LateMedieval = 3,
}

/// <summary>
/// One CK3 culture innovation. <see cref="InnovationData"/> holds the general random-draw pool;
/// region/regional freebies live in <c>FreebieData</c> (granted by <c>FreebieAssigner</c> via a
/// per-culture criterion rather than the random draw).
/// </summary>
public record Innovation(
    string Key,
    CultureEra Era,
    string Group   // "military" | "civic" (CK3 culture_group_*)
);
