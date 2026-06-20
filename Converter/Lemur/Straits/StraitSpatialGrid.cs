namespace Converter.Lemur.Straits;

/// <summary>
/// Uniform 2D bucket grid over projected cell centroids (CK3 image-pixel space), purpose-built
/// for strait generation: a radius query (candidate enumeration) and a nearest query (validity-gate
/// point→cell lookup, exact for Azgaar's Voronoi mesh). The heightmap grid only exposes NearestN,
/// hence a dedicated grid here. Stores reference items so an empty result is unambiguously null.
/// </summary>
internal sealed class StraitSpatialGrid<T> where T : class
{
    private readonly List<(double x, double y, T item)>[] _buckets;
    private readonly int _cols, _rows;
    private readonly double _minX, _minY, _cellW, _cellH;

    public StraitSpatialGrid(double minX, double minY, double maxX, double maxY, double targetCell)
    {
        _minX = minX;
        _minY = minY;
        double w = Math.Max(1e-6, maxX - minX);
        double h = Math.Max(1e-6, maxY - minY);
        double cell = Math.Max(1e-6, targetCell);
        _cols = Math.Max(1, (int)(w / cell));
        _rows = Math.Max(1, (int)(h / cell));
        _cellW = w / _cols;
        _cellH = h / _rows;
        _buckets = new List<(double, double, T)>[_cols * _rows];
        for (int i = 0; i < _buckets.Length; i++) _buckets[i] = [];
    }

    private int Col(double x) => Math.Clamp((int)((x - _minX) / _cellW), 0, _cols - 1);
    private int Row(double y) => Math.Clamp((int)((y - _minY) / _cellH), 0, _rows - 1);

    public void Add(double x, double y, T item) => _buckets[Row(y) * _cols + Col(x)].Add((x, y, item));

    /// <summary>All items whose centroid lies within <paramref name="radius"/> of (x, y).</summary>
    public IEnumerable<T> Within(double x, double y, double radius)
    {
        int span = (int)Math.Ceiling(radius / Math.Min(_cellW, _cellH)) + 1;
        int c = Col(x), r = Row(y);
        double r2 = radius * radius;
        for (int rr = Math.Max(0, r - span); rr <= Math.Min(_rows - 1, r + span); rr++)
            for (int cc = Math.Max(0, c - span); cc <= Math.Min(_cols - 1, c + span); cc++)
                foreach (var (bx, by, item) in _buckets[rr * _cols + cc])
                {
                    double dx = bx - x, dy = by - y;
                    if (dx * dx + dy * dy <= r2) yield return item;
                }
    }

    /// <summary>Nearest item to (x, y), or null if the grid is empty. Expanding-ring search.</summary>
    public T? Nearest(double x, double y)
    {
        int c = Col(x), r = Row(y);
        T? best = null;
        double bestD2 = double.MaxValue;
        double minCell = Math.Min(_cellW, _cellH);
        int maxRing = Math.Max(_cols, _rows);

        for (int radius = 0; radius <= maxRing; radius++)
        {
            int c0 = Math.Max(0, c - radius), c1 = Math.Min(_cols - 1, c + radius);
            int r0 = Math.Max(0, r - radius), r1 = Math.Min(_rows - 1, r + radius);
            for (int rr = r0; rr <= r1; rr++)
                for (int cc = c0; cc <= c1; cc++)
                {
                    if (radius > 0 && cc > c0 && cc < c1 && rr > r0 && rr < r1) continue; // ring shell only
                    foreach (var (bx, by, item) in _buckets[rr * _cols + cc])
                    {
                        double dx = bx - x, dy = by - y, d2 = dx * dx + dy * dy;
                        if (d2 < bestD2) { bestD2 = d2; best = item; }
                    }
                }

            // Stop once any point in the next ring is guaranteed farther than the best so far.
            if (best != null)
            {
                double guaranteed = radius * minCell;
                if (guaranteed * guaranteed > bestD2) break;
            }
        }
        return best;
    }
}
