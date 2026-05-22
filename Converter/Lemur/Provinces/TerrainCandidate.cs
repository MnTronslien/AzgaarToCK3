namespace Converter.Lemur.Provinces;

// A single CK3 terrain choice — one enum value + a per-barony score rule. The score is a
// pure function: BaronyContext → non-negative weight. Top-1 candidate per barony wins.
// Mirrors Lemur/Splats/Material.cs at coarser resolution.
public sealed class TerrainCandidate
{
    public Ck3Terrain Type { get; }
    private readonly TerrainScore _score;

    public TerrainCandidate(Ck3Terrain type, TerrainScore score)
    {
        Type = type;
        _score = score;
    }

    public float Score(in BaronyContext ctx) => _score(in ctx);
}

// Custom delegate so BaronyContext can be passed by `in` (struct copy avoided per call).
// Func<BaronyContext, float> would force a value copy on every rule evaluation.
public delegate float TerrainScore(in BaronyContext ctx);
