using Converter.Lemur.Entities;
using Converter.Lemur.Provinces;
using static Converter.Lemur.TenetData;

namespace Converter.Lemur.Fields;

/// <summary>
/// The facts a tenet <see cref="TenetEval"/> reads: the live <see cref="Faith"/> and its cached
/// per-faith <see cref="Ck3Terrain"/> distribution. Passed by <c>in</c> so eval delegates don't copy
/// the struct. Two helpers cover every authored tenet: <see cref="Favoured"/> (type affinity) and
/// <see cref="TerrainAtLeast"/> (the lone terrain tenet).
/// </summary>
public readonly struct FaithContext
{
    private readonly Faith _faith;

    /// <summary>Cell-weighted terrain fraction; sums to ~1 over the faith's land cells. Cached once
    /// by <see cref="FaithTerrainProfiler"/>, never recomputed per eval.</summary>
    private readonly IReadOnlyDictionary<Ck3Terrain, float> _terrain;

    public FaithContext(Faith faith, IReadOnlyDictionary<Ck3Terrain, float> terrain)
    {
        _faith = faith;
        _terrain = terrain;
    }

    /// <summary>The faith's own (single) type, parsed literally from <c>Faith.Type</c>.</summary>
    public FaithType Type => ParseType(_faith);

    /// <summary>True when the faith's type is in <paramref name="set"/>.</summary>
    public bool IsType(FaithType set) => (Type & set) != FaithType.None;

    /// <summary>Soft type affinity: <c>1</c> if favoured, else <see cref="TypeMismatchPenalty"/> (0.3).</summary>
    public float Favoured(FaithType set) => IsType(set) ? 1f : TypeMismatchPenalty;

    /// <summary>Hard {0,1}: 1 iff the summed fraction of the listed terrains is ≥ <paramref name="thr"/>.</summary>
    public float TerrainAtLeast(float thr, params Ck3Terrain[] terrains)
    {
        float f = 0f;
        foreach (var t in terrains)
            if (_terrain.TryGetValue(t, out var v)) f += v;
        return f >= thr ? 1f : 0f;
    }

    /// <summary>Parseable read-out of the cached distribution (for the profiler's log).</summary>
    public IReadOnlyDictionary<Ck3Terrain, float> TerrainFraction => _terrain;
}
