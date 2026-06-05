using Converter.Lemur.Provinces;

namespace Converter.Lemur.Fields;

/// <summary>
/// Per-faith land + identity facts that <see cref="TenetWeight"/> gates read. Faith twin of
/// <see cref="CultureContext"/>, keyed on the <b>canonical</b> <see cref="Ck3Terrain"/> the pipeline
/// already decided (via <c>BaronyTerrainAssigner</c>). Passed by <c>in</c> to avoid per-call copy.
///
/// <para>Unlike <see cref="CultureContext"/> this also carries the faith's <see cref="FaithType"/>
/// (Azgaar Folk/Organized/Cult) and its assigned <see cref="Doctrines"/> (e.g.
/// <c>doctrine_polytheist</c>) so the soft FaithKind penalty can read both axes — but the
/// running-chosen-tenets list is deliberately NOT here: mutual exclusivity is handled at assigner
/// level so each per-candidate weight lambda stays a pure function of <see cref="FaithContext"/>
/// (same separation culture used).</para>
/// </summary>
public readonly struct FaithContext
{
    /// <summary>false → all terrain gates treated as 1 (uniform, today's behaviour).</summary>
    public readonly bool HasLand;
    public readonly int CellCount;

    /// <summary>Cell-weighted terrain fraction; sums to ~1 over land cells.</summary>
    public readonly IReadOnlyDictionary<Ck3Terrain, float> TerrainFraction;

    /// <summary>Fraction of land cells adjacent to a sea cell.</summary>
    public readonly float CoastalFraction;

    /// <summary>Azgaar <c>Faith.Type</c>: "Folk" / "Organized" / "Cult" (may be empty).</summary>
    public readonly string FaithType;

    /// <summary>True when <see cref="FaithType"/> == "Folk" (unreformed).</summary>
    public readonly bool IsUnreformed;

    /// <summary>The faith's assigned doctrine keys (e.g. "doctrine_polytheist") — scanned for theism.</summary>
    public readonly IReadOnlyList<string> Doctrines;

    public FaithContext(
        bool hasLand,
        int cellCount,
        IReadOnlyDictionary<Ck3Terrain, float> terrainFraction,
        float coastalFraction,
        string faithType,
        bool isUnreformed,
        IReadOnlyList<string> doctrines)
    {
        HasLand = hasLand;
        CellCount = cellCount;
        TerrainFraction = terrainFraction;
        CoastalFraction = coastalFraction;
        FaithType = faithType;
        IsUnreformed = isUnreformed;
        Doctrines = doctrines;
    }

    public float TerrainFractionOf(Ck3Terrain t) =>
        TerrainFraction.TryGetValue(t, out var f) ? f : 0f;
}

/// <summary>
/// A terrain/coastal eligibility gate. Returns a weight in [0, 1] (MVP cap 1):
/// 1 = "as eligible as any normal tenet", 0 = "removed from the pool". Faith twin of
/// <see cref="TraditionWeight"/>.
/// </summary>
public delegate float TenetWeight(in FaithContext ctx);
