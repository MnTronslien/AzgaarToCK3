using Converter.Lemur.Entities;

namespace Converter.Lemur;

/// <summary>
/// Start-date-relative era + end-date math (PLAN_tech_levels.md). Everything derives from
/// <c>Map.StartDate.Year</c> and the world baseline era: the baseline unlocks at the start, higher
/// eras step forward by <c>EraStepYears</c>, lower eras floor at 0 (CK3 rejects a negative era year).
/// These years govern only POST-start passive progression — the start snapshot is forced by the
/// history block — so they keep tech advancing at a sane cadence regardless of the absolute start year.
/// </summary>
public static class TechDates
{
    /// <summary>Era's passive-progress year, relative to the start date. Tribal is always 0.</summary>
    public static int EraYear(CultureEra era, int startYear, CultureEra baseline, int stepYears)
    {
        if (era == CultureEra.Tribal) return 0;
        int offset = ((int)era - (int)baseline) * stepYears;
        return Math.Max(0, startYear + offset);
    }

    /// <summary>END_DATE year: climb the eras above the baseline at the ladder pace, then the tail.</summary>
    public static int EndYear(int startYear, CultureEra baseline, int stepYears, int tailYears)
        => startYear + ((int)CultureEra.LateMedieval - (int)baseline) * stepYears + tailYears;
}
