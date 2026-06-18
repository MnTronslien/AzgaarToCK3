namespace Converter.Lemur.Entities;

/// <summary>
/// CK3 game-start date — the single source of truth for every dated emission (title history,
/// bookmark, character births, culture creation dates). Lives on <see cref="Map"/> because it is
/// world data, not a user setting; a future loader will populate it from the Azgaar export.
/// Default 1066.1.1 reproduces pre-rewire output byte-for-byte.
/// </summary>
public readonly record struct StartDate(int Year, int Month, int Day)
{
    public override string ToString() => $"{Year}.{Month}.{Day}";
}
