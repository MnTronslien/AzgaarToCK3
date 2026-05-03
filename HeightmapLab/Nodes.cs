namespace HeightmapLab;

interface INode
{
    float Px { get; }
    float Py { get; }
    float Height { get; }
}

record struct TerrainNode(
    int Id,
    float Px, float Py,
    float Height,
    float Roughness,
    bool IsLand,
    int Area) : INode;

record struct PolyNode(
    float Px, float Py,
    float SpawnPx, float SpawnPy,
    int ParentId,                   // index into TerrainNodes (debug)
    int C0, int C1, int C2,         // contributing TerrainNode indices (debug)
    float W0, float W1, float W2,   // IDW weights for C0/C1/C2 (debug)
    float IdwRoughness,
    float RawRand,
    float Height) : INode;

// Inserted at polygon vertices shared between a land cell and a sea cell.
// Pinned to CK3WaterLevel — forces the Delaunay waterline to cross at the
// actual cell boundary rather than drifting inland.
record struct CoastNode(float Px, float Py) : INode
{
    public float Height => HeightmapGenerator.CK3WaterLevel;
}
