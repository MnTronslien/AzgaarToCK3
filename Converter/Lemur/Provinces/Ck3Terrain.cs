namespace Converter.Lemur.Provinces;

/// <summary>
/// The complete legal-CK3 set of per-province terrain types we may emit into
/// <c>common/province_terrain/00_province_terrain.txt</c>.
///
/// Sea types (<c>sea</c>, <c>coastal_sea</c>) are handled separately as the
/// <c>default_sea</c> / <c>default_coastal_sea</c> header lines and never appear
/// per-province, so they are intentionally absent from this enum.
///
/// <see cref="TerracedHills"/> is included for completeness even though the v1
/// terrain registry has no Azgaar signal that selects it — its score lambda
/// returns 0. Keeping the enum complete means the type system enforces
/// "only legal CK3 values ever reach the writer."
/// </summary>
public enum Ck3Terrain
{
    Plains,
    Farmlands,
    Hills,
    Mountains,
    Desert,
    DesertMountains,
    Drylands,
    Jungle,
    Forest,
    Taiga,
    Wetlands,
    Steppe,
    Floodplains,
    Oasis,
    TerracedHills,
}

public static class Ck3TerrainExtensions
{
    /// <summary>
    /// Snake_case string CK3 expects in 00_province_terrain.txt.
    /// </summary>
    public static string ToCk3String(this Ck3Terrain terrain) => terrain switch
    {
        Ck3Terrain.Plains          => "plains",
        Ck3Terrain.Farmlands       => "farmlands",
        Ck3Terrain.Hills           => "hills",
        Ck3Terrain.Mountains       => "mountains",
        Ck3Terrain.Desert          => "desert",
        Ck3Terrain.DesertMountains => "desert_mountains",
        Ck3Terrain.Drylands        => "drylands",
        Ck3Terrain.Jungle          => "jungle",
        Ck3Terrain.Forest          => "forest",
        Ck3Terrain.Taiga           => "taiga",
        Ck3Terrain.Wetlands        => "wetlands",
        Ck3Terrain.Steppe          => "steppe",
        Ck3Terrain.Floodplains     => "floodplains",
        Ck3Terrain.Oasis           => "oasis",
        Ck3Terrain.TerracedHills   => "terraced_hills",
        _ => throw new ArgumentOutOfRangeException(nameof(terrain), terrain, "Unknown Ck3Terrain value"),
    };
}
