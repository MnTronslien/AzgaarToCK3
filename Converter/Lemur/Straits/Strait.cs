using Converter.Lemur.Entities;

namespace Converter.Lemur.Straits;

/// <summary>
/// An army-crossable sea strait between two coastal land cells, through ocean water.
/// Produced by <see cref="StraitGenerator"/>; decomposed by AdjacenciesCsvWriter into a CK3
/// adjacency row, and drawn as a red line by <see cref="StraitDebugImage"/>.
///
/// First-class object (not a tuple) so the generator can attach everything the writer and the
/// debug image both need — see PLAN_straits.md.
/// </summary>
public sealed class Strait
{
    /// <summary>Coastal land cell on landmass A. Its province (a Barony) is the CK3 "From".</summary>
    public required Cell FromCell { get; init; }

    /// <summary>Coastal land cell on landmass B (B may equal A for a bay crossing).</summary>
    public required Cell ToCell { get; init; }

    /// <summary>Ocean cell nearest the crossing midpoint — resolves to the CK3 sea province ("Through").</summary>
    public required Cell ThroughCell { get; init; }

    /// <summary>Crossing length in CK3 image-pixel units (projected). Drives rule-3 shortest-first ordering.</summary>
    public required double Length { get; init; }

    /// <summary>Segment midpoint in projected image space — used for rule-3 clearance spacing.</summary>
    public required double MidX { get; init; }
    public required double MidY { get; init; }

    /// <summary>Unordered landmass-component id pair (A ≤ B); A == B for a bay crossing.</summary>
    public required int LandmassA { get; init; }
    public required int LandmassB { get; init; }
}
