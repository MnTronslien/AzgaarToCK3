namespace Converter.Lemur.Writers;

public interface IHeightmapNode
{
    float Px { get; }
    float Py { get; }
    float Height { get; }
}

public record struct TerrainNode(
    int Id,
    float Px, float Py,
    float Height,
    float Roughness,
    bool IsLand,
    int Area) : IHeightmapNode;

public record struct PolyNode(
    float Px, float Py,
    float SpawnPx, float SpawnPy,
    int ParentId,
    int C0, int C1, int C2,
    float W0, float W1, float W2,
    float IdwRoughness,
    float RawRand,
    float Height) : IHeightmapNode;

public record struct CoastNode(float Px, float Py) : IHeightmapNode
{
    // Coast nodes mark the FIRST LAND BYTE — one above MaxWaterByte. With strict `> MaxWaterByte`
    // = land semantics, sitting at MaxWaterByte would make coast nodes water; raising to +1 puts
    // them on the land side so the rasterised coastline lines up with the actual cell-polygon
    // boundary instead of creeping a few pixels inland.
    public float Height => HeightmapAlgorithm.MaxWaterByte + 1;
}
