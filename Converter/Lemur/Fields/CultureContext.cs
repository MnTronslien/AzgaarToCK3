using Converter.Lemur.Provinces;

namespace Converter.Lemur.Fields;

/// <summary>
/// Per-culture land facts that <see cref="TraditionWeight"/> gates read, mirroring
/// <c>BaronyContext</c> but at culture scope and keyed on the <b>canonical</b>
/// <see cref="Ck3Terrain"/> the pipeline already decided (via
/// <c>BaronyTerrainAssigner</c>). Passed by <c>in</c> to avoid per-call copy.
///
/// Using <see cref="Ck3Terrain"/> (not raw <c>AzgaarBiome</c>) means hills /
/// mountains / desert / farmlands are already folded in — we reuse that decision
/// rather than re-deriving roughness bands.
/// </summary>
public readonly struct CultureContext
{
    /// <summary>false → all terrain gates treated as 1 (uniform, today's behaviour).</summary>
    public readonly bool HasLand;
    public readonly int CellCount;

    /// <summary>Cell-weighted terrain fraction; sums to ~1 over land cells.</summary>
    public readonly IReadOnlyDictionary<Ck3Terrain, float> TerrainFraction;

    /// <summary>Fraction of land cells adjacent to a sea cell.</summary>
    public readonly float CoastalFraction;

    public CultureContext(
        bool hasLand,
        int cellCount,
        IReadOnlyDictionary<Ck3Terrain, float> terrainFraction,
        float coastalFraction)
    {
        HasLand = hasLand;
        CellCount = cellCount;
        TerrainFraction = terrainFraction;
        CoastalFraction = coastalFraction;
    }

    public float TerrainFractionOf(Ck3Terrain t) =>
        TerrainFraction.TryGetValue(t, out var f) ? f : 0f;

    /// <summary>
    /// Hard {0,1} terrain gate: 1 iff the summed fraction of the listed terrains ≥ threshold.
    /// Reusable predicate for inline <see cref="TraditionWeight"/> lambdas, mirroring
    /// <c>PixelContext.AzgaarBiomeWeight</c>.
    /// </summary>
    public float TerrainAtLeast(float threshold, params Ck3Terrain[] terrains)
    {
        float f = 0f;
        foreach (var t in terrains) f += TerrainFractionOf(t);
        return f >= threshold ? 1f : 0f;
    }

    /// <summary>Hard {0,1} coastal gate: 1 iff <see cref="CoastalFraction"/> ≥ threshold.</summary>
    public float CoastalAtLeast(float threshold) =>
        CoastalFraction >= threshold ? 1f : 0f;
}

/// <summary>
/// A terrain/coastal eligibility gate. Returns a weight in [0, 1] (MVP cap 1):
/// 1 = "as eligible as any normal tradition", 0 = "removed from the pool".
/// </summary>
public delegate float TraditionWeight(in CultureContext ctx);
