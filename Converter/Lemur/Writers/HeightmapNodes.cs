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
    // Coast nodes pin to the FIRST land byte (LowestLandByte = MaxWaterByte + 1) so they sit on
    // the land side of the waterline. Sitting at MaxWaterByte would make coast nodes water under
    // strict-`>` land semantics, and the rasterised coastline would creep a few pixels inland.
    public float Height => HeightmapAlgorithm.LowestLandByte;
}
