namespace TerrainLab;

/// <summary>
/// Fast 2D spatial index for nearest-N lookups.
/// Divides the space into a uniform grid of buckets; queries search an expanding ring
/// of buckets until at least N candidates are found, then sort by actual distance.
/// </summary>
sealed class SpatialGrid<T>
{
    private readonly List<(float x, float y, T item)>[] _buckets;
    private readonly int _cols;
    private readonly int _rows;
    private readonly float _cellW;
    private readonly float _cellH;

    public SpatialGrid(float width, float height, int cols, int rows)
    {
        _cols = cols;
        _rows = rows;
        _cellW = width / cols;
        _cellH = height / rows;
        _buckets = new List<(float, float, T)>[cols * rows];
        for (int i = 0; i < _buckets.Length; i++)
            _buckets[i] = [];
    }

    public void Add(float x, float y, T item)
    {
        int col = Math.Clamp((int)(x / _cellW), 0, _cols - 1);
        int row = Math.Clamp((int)(y / _cellH), 0, _rows - 1);
        _buckets[row * _cols + col].Add((x, y, item));
    }

    /// <summary>Returns up to <paramref name="n"/> nearest items, sorted by ascending distance.</summary>
    public List<(float dist, T item)> NearestN(float x, float y, int n)
    {
        int col = Math.Clamp((int)(x / _cellW), 0, _cols - 1);
        int row = Math.Clamp((int)(y / _cellH), 0, _rows - 1);

        var candidates = new List<(float dist, T item)>();
        float minCellSize = Math.Min(_cellW, _cellH);
        int radius = 0;

        while (radius <= Math.Max(_cols, _rows))
        {
            int c0 = Math.Max(0, col - radius), c1 = Math.Min(_cols - 1, col + radius);
            int r0 = Math.Max(0, row - radius), r1 = Math.Min(_rows - 1, row + radius);

            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    if (radius > 0 && c > c0 && c < c1 && r > r0 && r < r1) continue;
                    foreach (var (bx, by, item) in _buckets[r * _cols + c])
                    {
                        float dx = bx - x, dy = by - y;
                        candidates.Add((MathF.Sqrt(dx * dx + dy * dy), item));
                    }
                }

            radius++;

            // Stop only once we have n candidates AND the nearest possible point
            // in the next ring is guaranteed farther than our nth candidate.
            // Any point in ring `radius` is at least (radius-1)*minCellSize away.
            if (candidates.Count >= n)
            {
                candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
                if ((radius - 1) * minCellSize > candidates[n - 1].dist) break;
            }
        }

        candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
        if (candidates.Count > n) candidates.RemoveRange(n, candidates.Count - n);
        return candidates;
    }
}
