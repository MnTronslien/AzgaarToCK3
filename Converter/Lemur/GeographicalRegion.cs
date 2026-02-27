using LE = Converter.Lemur.Entities;

namespace Converter.Lemur;

/// <summary>
/// A CK3 geographical region — name, sub-region hierarchy, and direct title members.
/// When reading from the vanilla file we only populate Name (stubs everything else).
/// When building Lemur's output we mutate the parsed object with real data.
/// </summary>
public class GeographicalRegion
{
    public required string Name { get; init; }

    /// <summary>Sub-regions nested inside this region's <c>regions = { }</c> block.</summary>
    public List<GeographicalRegion> SubRegions { get; set; } = [];

    /// <summary>
    /// Titles directly assigned to this region. Using ITitle allows adding at any
    /// hierarchy level — e.g. a Kingdom implicitly covers all its baronies in CK3.
    /// </summary>
    public List<LE.ITitle> DirectMembers { get; set; } = [];

    /// <summary>Whether to write <c>graphical = yes</c> in the output block.</summary>
    public bool Graphical { get; set; }

    /// <summary>RGB color for the map editor overlay (null = omit the color line).</summary>
    public (int R, int G, int B)? Color { get; set; }

    /// <summary>
    /// Additional province IDs written verbatim into the <c>provinces = { }</c> block.
    /// Used to include wasteland provinces that are not Barony DirectMembers.
    /// </summary>
    public List<int> ExtraProvinceIds { get; set; } = [];

    /// <summary>
    /// Collects all Barony-level members from DirectMembers AND from all SubRegions
    /// recursively, deduplicating by province ID.
    /// NOTE: Only resolves members that are already Barony instances. Higher-level
    /// ITitle types (Kingdom, Duchy, etc.) are not expanded yet.
    /// </summary>
    public List<LE.Barony> GetRegionMembers()
    {
        var seen = new HashSet<int>();
        var result = new List<LE.Barony>();
        Collect(this, seen, result);
        return result;

        static void Collect(GeographicalRegion r, HashSet<int> seen, List<LE.Barony> result)
        {
            foreach (var m in r.DirectMembers.OfType<LE.Barony>())
                if (seen.Add(m.Id)) result.Add(m);
            foreach (var sub in r.SubRegions)
                Collect(sub, seen, result);
        }
    }
}
