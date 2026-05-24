using Converter.Lemur.Splats;

namespace Converter.Lemur.Provinces;

// Per-barony facts that TerrainCandidate rules read. Passed by `in` to avoid per-call copy.
public readonly struct BaronyContext
{
    public readonly AzgaarBiome DominantBiome;
    public readonly IReadOnlyDictionary<AzgaarBiome, float> BiomeFraction;

    // p95-normalised; values can occasionally exceed 1.0 — uncapped on purpose so band rules
    // see the actual distribution.
    public readonly IReadOnlyList<float> CellRoughnesses;

    public readonly bool RiverAdjacent;
    public readonly float PopDensity;     // Population / CellCount
    public readonly float Population;
    public readonly int CellCount;

    public BaronyContext(
        AzgaarBiome dominantBiome,
        IReadOnlyDictionary<AzgaarBiome, float> biomeFraction,
        IReadOnlyList<float> cellRoughnesses,
        bool riverAdjacent,
        float popDensity,
        float population,
        int cellCount)
    {
        DominantBiome = dominantBiome;
        BiomeFraction = biomeFraction;
        CellRoughnesses = cellRoughnesses;
        RiverAdjacent = riverAdjacent;
        PopDensity = popDensity;
        Population = population;
        CellCount = cellCount;
    }

    public float BiomeFractionOf(AzgaarBiome b) => BiomeFraction.TryGetValue(b, out var f) ? f : 0f;
}
