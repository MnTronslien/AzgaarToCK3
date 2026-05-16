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
    public float Height => HeightmapAlgorithm.MaxWaterByte;
}
