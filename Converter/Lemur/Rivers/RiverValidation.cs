using System.Drawing;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Represents the validation results for a single river.
/// </summary>
public class RiverValidation
{
    public string RiverName { get; set; } = string.Empty;

    /// <summary>
    /// Interior pixels that don't have exactly 2 blue neighbors.
    /// CK3 requires interior river pixels to have exactly 2 blue neighbors (upstream + downstream).
    /// </summary>
    public List<PixelViolation> InteriorViolations { get; set; } = new();

    /// <summary>
    /// Endpoint pixels that don't have exactly 1 blue neighbor.
    /// CK3 requires endpoints (source/mouth) to have exactly 1 blue neighbor.
    /// </summary>
    public List<PixelViolation> EndpointViolations { get; set; } = new();

    /// <summary>
    /// Non-orthogonal jumps (diagonal movement).
    /// CK3 requires rivers to move only horizontally or vertically, never diagonally.
    /// </summary>
    public List<GapViolation> NonOrthogonalJumps { get; set; } = new();

    /// <summary>
    /// Large gaps between consecutive pixels (distance > 1).
    /// These need to be filled with proper orthogonal paths.
    /// </summary>
    public List<GapViolation> LargeGaps { get; set; } = new();

    /// <summary>
    /// Duplicate pixels in the path (same pixel appears multiple times).
    /// </summary>
    public List<Point> DuplicatePixels { get; set; } = new();

    /// <summary>
    /// Red (junction) pixels that are not orthogonally adjacent to exactly 2 blue pixels.
    /// A valid junction sits between exactly one tributary pixel (blue) and one parent-river
    /// pixel (blue). Any other count indicates a malformed connection.
    /// </summary>
    public List<PixelViolation> RedPixelViolations { get; set; } = new();

    /// <summary>
    /// Maximum gap distance found in this river.
    /// </summary>
    public int MaxGapDistance { get; set; }

    /// <summary>
    /// Average gap distance for this river.
    /// </summary>
    public double AverageGapDistance { get; set; }

    /// <summary>
    /// Total number of pixels in the river path.
    /// </summary>
    public int TotalPixels { get; set; }

    /// <summary>
    /// Excessive offshore runoff (more than 3 pixels into ocean).
    /// </summary>
    public int ExcessiveOffshorePixels { get; set; }

    /// <summary>
    /// Midstream ocean crossings (river goes through ocean not at mouth).
    /// </summary>
    public List<Point> MidstreamOceanPixels { get; set; } = new();

    /// <summary>
    /// Whether this river passes all CK3 topology rules.
    /// </summary>
    public bool IsValid =>
        !InteriorViolations.Any() &&
        !EndpointViolations.Any() &&
        !NonOrthogonalJumps.Any() &&
        !LargeGaps.Any() &&
        !DuplicatePixels.Any() &&
        !RedPixelViolations.Any() &&
        ExcessiveOffshorePixels == 0 &&
        !MidstreamOceanPixels.Any();

    /// <summary>
    /// Total number of violations across all categories.
    /// </summary>
    public int TotalViolations =>
        InteriorViolations.Count +
        EndpointViolations.Count +
        NonOrthogonalJumps.Count +
        LargeGaps.Count +
        DuplicatePixels.Count +
        RedPixelViolations.Count +
        (ExcessiveOffshorePixels > 0 ? 1 : 0) +
        MidstreamOceanPixels.Count;

    public override string ToString()
    {
        if (IsValid)
            return $"✓ {RiverName}: Valid ({TotalPixels} pixels)";

        var parts = new List<string>
        {
            $"Interior:{InteriorViolations.Count}",
            $"Endpoints:{EndpointViolations.Count}",
            $"NonOrthogonal:{NonOrthogonalJumps.Count}",
            $"LargeGaps:{LargeGaps.Count}",
            $"Duplicates:{DuplicatePixels.Count}",
            $"RedPixel:{RedPixelViolations.Count}"
        };

        if (ExcessiveOffshorePixels > 0)
            parts.Add($"Offshore:{ExcessiveOffshorePixels}px");

        if (MidstreamOceanPixels.Any())
            parts.Add($"MidOcean:{MidstreamOceanPixels.Count}");

        return $"✗ {RiverName}: {TotalViolations} violations - {string.Join(" ", parts)}";
    }
}

/// <summary>
/// Represents a pixel that violates CK3 neighbor rules.
/// </summary>
public class PixelViolation
{
    public Point Pixel { get; set; }
    public int ActualNeighbors { get; set; }
    public int ExpectedNeighbors { get; set; }

    public override string ToString() =>
        $"({Pixel.X},{Pixel.Y}): {ActualNeighbors} neighbors (expected {ExpectedNeighbors})";
}

/// <summary>
/// Represents a gap between two consecutive pixels in a river path.
/// </summary>
public class GapViolation
{
    public Point From { get; set; }
    public Point To { get; set; }
    public int Distance { get; set; }
    public bool IsDiagonal { get; set; }

    public override string ToString() =>
        $"({From.X},{From.Y}) → ({To.X},{To.Y}): distance {Distance}" +
        (IsDiagonal ? " (diagonal)" : "");
}
