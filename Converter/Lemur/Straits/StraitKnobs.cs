namespace Converter.Lemur.Straits;

/// <summary>
/// Density-scaling hack for strait knobs (see STRAITS_TUNING.md). Azgaar maps render to a fixed CK3
/// resolution regardless of cell count, so a fixed pixel MaxDistance means different things on a
/// sparse vs a dense map. Until the proper fix (density-invariant cell pixel-area), we interpolate
/// MaxDistance between two maintainer-tuned anchors by log(cell count). Quick, dirty, replaceable —
/// it is the ONLY density-sensitive knob (self-sep, clearance, ocean-area are stable; see the doc).
/// </summary>
public static class StraitKnobs
{
    // Maintainer-approved anchors (STRAITS_TUNING.md): (cell count, MaxDistance px).
    private const double SparseCells = 4_148, SparseMaxDist = 120;   // Oncyia
    private const double DenseCells = 51_759, DenseMaxDist = 50;     // Showcase

    /// <summary>
    /// Effective MaxDistance for a map. <paramref name="explicit"/> non-null wins outright; otherwise
    /// log-interpolate between the anchors, clamped to their range for maps outside it.
    /// </summary>
    public static double ResolveMaxDistance(int cellCount, double? @explicit)
    {
        if (@explicit.HasValue) return @explicit.Value;

        double n = Math.Clamp(cellCount, SparseCells, DenseCells);
        double t = (Math.Log(n) - Math.Log(SparseCells)) / (Math.Log(DenseCells) - Math.Log(SparseCells));
        return SparseMaxDist + t * (DenseMaxDist - SparseMaxDist);
    }
}
