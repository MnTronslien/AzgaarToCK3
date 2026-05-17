using Converter.Lemur.Deserialization;
using Converter.Lemur.Entities;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace Converter.Lemur.Splats;

// Computes a per-pixel BiomeWeightTriple via Delaunay triangulation of land-cell centroids and
// barycentric interpolation inside each triangle. The output stores Azgaar biome values (not CK3
// byte indices) — the Azgaar→CK3-texture mapping lives downstream in Material declarations.
//
// Algorithm (carried over from M1's SplatmapBuilder, output reshape only):
//   A) For each land cell with a known Azgaar biome (1..12), compute its pixel-space centroid.
//   B) Delaunay-triangulate the centroids; every pixel inside the convex hull falls in exactly
//      one triangle.
//   C) For each triangle, scanline-fill; per-pixel compute barycentric (w0, w1, w2), bucket the
//      3 corner biomes (collapsing duplicates), and write into BiomeWeightTriple.
//
// Pixels outside any triangle (open sea, beyond convex hull) remain BiomeWeightTriple.Empty —
// downstream Material rules will return 0 weight there, and the pack step writes SplatPixel.AllUnused.
//
// Math template: HeightmapAlgorithm.RasterizeTriangle (lines 826-849).
public static class BiomeWeightField
{
    public static BiomeWeightTriple[] Build(IReadOnlyDictionary<int, Cell> cells, AzgaarMapCoordinates coords)
    {
        int width = Map.MapWidth;
        int height = Map.MapHeight;

        // Coord transform (identical to ImageUtility.GenerateCellPolygons(.., AzgaarMapCoordinates))
        float xOffset = coords.lonW;
        float yOffset = coords.latS;
        float xRatio  = width  / coords.lonT;
        float yRatio  = height / coords.latT;

        // ── Phase A: filter + compute centroids ──────────────────────────────
        // NTS perturbs/rounds coordinates during triangulation; use rounded-int (x, y) tuples
        // as keys for the back-lookup. Same pattern as HeightmapAlgorithm.BuildCoordIndex.
        var centroids = new List<Coordinate>();
        var coordToBiome = new Dictionary<(int, int), byte>();   // rounded centroid → AzgaarBiome byte

        foreach (var cell in cells.Values)
        {
            // INCLUDE SEA CELLS in the triangulation — they get biome = 0 (AzgaarBiome.None).
            // Inside a mixed land/sea Delaunay triangle, the sea corner takes part of the
            // barycentric weight, which naturally drops land-biome weights as the pixel
            // approaches the coast. This is cell-space distance-to-sea, much better than
            // height-from-waterline as a "near coast" signal.
            //
            // Land cells: biome ∈ [1, 12] (AzgaarBiome.HotDesert .. Wetland)
            // Sea cells:  biome = 0    (AzgaarBiome.None) — no land biome contribution
            bool isLand = Cell.IsDryLand(cell.Type);
            byte biome;
            if (isLand)
            {
                if (cell.Biome <= 0 || cell.Biome > 12) continue;   // unmapped land — skip
                biome = (byte)cell.Biome;
            }
            else
            {
                biome = 0;   // AzgaarBiome.None
            }
            if (cell.GeoDataCoordinates == null || cell.GeoDataCoordinates.Length < 3) continue;

            // Azgaar polygons close the ring (first == last); skip last to avoid double-counting.
            double sx = 0, sy = 0;
            int count = cell.GeoDataCoordinates.Length - 1;
            if (count <= 0) continue;
            for (int i = 0; i < count; i++)
            {
                sx += cell.GeoDataCoordinates[i][0];
                sy += cell.GeoDataCoordinates[i][1];
            }
            double lon = sx / count;
            double lat = sy / count;
            double px = (lon - xOffset) * xRatio;
            double py = height - (lat - yOffset) * yRatio;

            var key = ((int)Math.Round(px), (int)Math.Round(py));
            if (coordToBiome.ContainsKey(key)) continue;  // skip duplicates (rare)
            centroids.Add(new Coordinate(px, py));
            coordToBiome[key] = biome;
        }

        var field = new BiomeWeightTriple[width * height];   // zero-init = BiomeWeightTriple.Empty
        if (centroids.Count < 3) return field;               // not enough land to triangulate

        // ── Phase B: Delaunay triangulate ────────────────────────────────────
        var gf = new GeometryFactory();
        var builder = new DelaunayTriangulationBuilder();
        builder.SetSites(gf.CreateMultiPointFromCoords(centroids.ToArray()));
        var triangles = builder.GetTriangles(gf);

        // ── Phase C: rasterise each triangle ─────────────────────────────────
        foreach (var geom in triangles.Geometries)
        {
            var ring = geom.Boundary.Coordinates;
            if (ring.Length < 3) continue;
            var vA = ring[0]; var vB = ring[1]; var vC = ring[2];

            if (!coordToBiome.TryGetValue(((int)Math.Round(vA.X), (int)Math.Round(vA.Y)), out byte bA)) continue;
            if (!coordToBiome.TryGetValue(((int)Math.Round(vB.X), (int)Math.Round(vB.Y)), out byte bB)) continue;
            if (!coordToBiome.TryGetValue(((int)Math.Round(vC.X), (int)Math.Round(vC.Y)), out byte bC)) continue;

            RasterizeTriangle(vA, vB, vC, bA, bB, bC, field, width, height);
        }

        return field;
    }

    // Scanline fill of a triangle. For each pixel inside, compute barycentric weights, bucket the
    // 3 corner biomes (collapsing duplicates: [plains, plains, snow] → 2 entries not 3), and
    // write the (up to 3) (biome, weight) pairs into the BiomeWeightTriple.
    private static void RasterizeTriangle(
        Coordinate v0, Coordinate v1, Coordinate v2,
        byte biome0, byte biome1, byte biome2,
        BiomeWeightTriple[] field, int width, int height)
    {
        int xMin = Math.Max(0,          (int)Math.Min(v0.X, Math.Min(v1.X, v2.X)));
        int xMax = Math.Min(width  - 1, (int)Math.Ceiling(Math.Max(v0.X, Math.Max(v1.X, v2.X))));
        int yMin = Math.Max(0,          (int)Math.Min(v0.Y, Math.Min(v1.Y, v2.Y)));
        int yMax = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(v0.Y, Math.Max(v1.Y, v2.Y))));

        float denom = (float)((v1.Y - v2.Y) * (v0.X - v2.X) + (v2.X - v1.X) * (v0.Y - v2.Y));
        if (MathF.Abs(denom) < 1e-6f) return;   // degenerate triangle

        // Fast path: all three corners share the same biome → every interior pixel gets the
        // exact same BiomeWeightTriple regardless of barycentric position. Common cases:
        //   - 3 same-biome land cells (large biome clusters interior)
        //   - 3 sea cells (oceans away from land)
        // Skip the per-pixel barycentric math entirely; the inside-triangle check still applies.
        if (biome0 == biome1 && biome1 == biome2)
        {
            var uniform = new BiomeWeightTriple(biome0, 1f, 0, 0f, 0, 0f);
            for (int py = yMin; py <= yMax; py++)
            {
                for (int px = xMin; px <= xMax; px++)
                {
                    float w0 = (float)((v1.Y - v2.Y) * (px - v2.X) + (v2.X - v1.X) * (py - v2.Y)) / denom;
                    float w1 = (float)((v2.Y - v0.Y) * (px - v2.X) + (v0.X - v2.X) * (py - v2.Y)) / denom;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f) continue;
                    field[py * width + px] = uniform;
                }
            }
            return;
        }

        for (int py = yMin; py <= yMax; py++)
        {
            for (int px = xMin; px <= xMax; px++)
            {
                float w0 = (float)((v1.Y - v2.Y) * (px - v2.X) + (v2.X - v1.X) * (py - v2.Y)) / denom;
                float w1 = (float)((v2.Y - v0.Y) * (px - v2.X) + (v0.X - v2.X) * (py - v2.Y)) / denom;
                float w2 = 1f - w0 - w1;
                if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f) continue;

                // Bucket the 3 corner biomes (collapse duplicates)
                byte bA = biome0;  float wA = w0;
                byte bB = 0;       float wB = 0f;
                byte bC = 0;       float wC = 0f;

                if (biome1 == bA) wA += w1;
                else { bB = biome1; wB = w1; }

                if (biome2 == bA)      wA += w2;
                else if (biome2 == bB) wB += w2;
                else                   { bC = biome2; wC = w2; }

                field[py * width + px] = new BiomeWeightTriple(bA, wA, bB, wB, bC, wC);
            }
        }
    }
}
