namespace Converter.Lemur.Entities;

/// <summary>
/// CK3 game-start date — the single source of truth for every dated emission (title history,
/// bookmark, character births, culture creation dates). Lives on <see cref="Map"/> because it is
/// world data, not a user setting; a future loader will populate it from the Azgaar export.
/// Default 1066.1.1 reproduces pre-rewire output byte-for-byte.
/// </summary>
public readonly record struct StartDate(int Year, int Month, int Day)
{
    // CK3 dates cannot be below year 0 (a .info file in the engine data spells this out).
    // Floor defensively at the source so a future negative year — e.g. a bad Azgaar export —
    // can never emit an invalid date. Derived dates (births, culture creation) are floored at
    // their own emit sites, since they subtract from this and can go negative on their own.
    public int Year { get; init; } = Math.Max(0, Year);

    public override string ToString() => $"{Year}.{Month}.{Day}";
}
