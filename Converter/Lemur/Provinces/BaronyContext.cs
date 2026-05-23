using Converter.Lemur.Splats;

namespace Converter.Lemur.Provinces;

// Per-barony facts that TerrainCandidate rules read. Built fresh per barony from cell-level
// data (Biome, Roughness) plus the barony's Burg (population) and adjacency. Mirrors the
// shape of Lemur/Splats/PixelContext.cs at coarser (barony rather than pixel) resolution.
// Passed by `in` to rules to avoid per-call struct copy.
public readonly struct BaronyContext
{
    /// <summary>Most common AzgaarBiome across this barony's land cells, weighted by cell count.</summary>
    public readonly AzgaarBiome DominantBiome;

    /// <summary>
    /// Fraction of cells per biome, summing to 1.0 across all land cells of this barony.
    /// Lets rules look at minority biomes (e.g. "any wetland cell at all → wetland-leaning").
    /// </summary>
    public readonly IReadOnlyDictionary<AzgaarBiome, float> BiomeFraction;

    /// <summary>
    /// Raw per-cell roughness values for this barony, in [0..1] (p95-normalised; values can
    /// occasionally exceed 1.0). Exposed as a list so band-based rules (Hills, Mountains,
    /// DesertMountains) can compute their own "fraction of cells in band [x, y]" → score
    /// interpolation locally, keeping each rule's tuning knobs adjacent to its lambda.
    /// </summary>
    public readonly IReadOnlyList<float> CellRoughnesses;

    /// <summary>True if any cell in this barony touches a major-river province or a freshwater feature.</summary>
    public readonly bool RiverAdjacent;

    /// <summary>Burg population divided by cell count. Proxy for agricultural intensity / settlement density.</summary>
    public readonly float PopDensity;

    /// <summary>Raw burg population, before dividing by cells. Useful for "is this a big city" signals.</summary>
    public readonly float Population;

    /// <summary>Number of land cells in this barony.</summary>
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

    /// <summary>Sugar — fraction of cells in this barony with the given biome (0..1).</summary>
    public float BiomeFractionOf(AzgaarBiome b) => BiomeFraction.TryGetValue(b, out var f) ? f : 0f;
}
