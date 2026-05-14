using Converter.Lemur.Deserialization;
using Converter.Lemur.Entities;
using Converter.Lemur.Writers;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace Converter.Lemur.Splats;

// Builds a Splatmap from cell data via Delaunay triangulation of land-cell centroids.
//
// Algorithm (M1):
//   A) Filter land cells whose Azgaar biome has a CK3 mapping; compute pixel-space centroids.
//   B) Delaunay-triangulate the centroids — every pixel inside the convex hull of land cells
//      falls inside exactly one triangle whose 3 corners are 3 distinct cell biomes.
//   C) For each triangle, scanline-rasterise; per-pixel compute barycentric (w0, w1, w2), bucket
//      weights by CK3 biome (so [plains, plains, snow] at (0.4, 0.3, 0.3) → {plains: 0.7, snow: 0.3}),
//      pack the top biomes into a SplatPixel with intensities = weight × 255.
//
// Pixels outside any triangle (open sea, beyond convex hull) remain at SplatPixel.AllUnused — the
// writer leaves them unpainted so CK3 falls back to the engine default texture there.
//
// Math template: HeightmapAlgorithm.RasterizeTriangle (lines 826-849). Same barycentric pattern,
// different per-pixel output.
public static class SplatmapBuilder
{
    public static Splatmap BuildM1(IReadOnlyDictionary<int, Cell> cells, AzgaarMapCoordinates coords)
    {
        int width = Map.MapWidth;
        int height = Map.MapHeight;

        // Coord transform (identical to ImageUtility.GenerateCellPolygons(.., AzgaarMapCoordinates))
        float xOffset = coords.lonW;
        float yOffset = coords.latS;
        float xRatio  = width  / coords.lonT;
        float yRatio  = height / coords.latT;

        // ── Phase A: filter + compute centroids ──────────────────────────────
        // NTS perturbs/rounds coordinates during triangulation, so we cannot rely on Coordinate
        // identity to map a triangle's vertex back to its source cell. Use rounded-int (x, y)
        // tuples as keys — same pattern as HeightmapAlgorithm.BuildCoordIndex / Lookup.
        var centroids = new List<Coordinate>();
        var coordToBiome = new Dictionary<(int, int), int>();   // rounded centroid → CK3 biome

        foreach (var cell in cells.Values)
        {
            if (!Cell.IsDryLand(cell.Type)) continue;
            if (!TerrainMaskWriter.AzgaarBiomeToCk3Index.TryGetValue(cell.Biome, out int ck3Biome)) continue;
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
            coordToBiome[key] = ck3Biome;
        }

        if (centroids.Count < 3)
        {
            // Not enough land cells to form a triangulation — return all-sentinel.
            return AllSentinel(width, height);
        }

        // ── Phase B: Delaunay triangulate ────────────────────────────────────
        var gf = new GeometryFactory();
        var builder = new DelaunayTriangulationBuilder();
        builder.SetSites(gf.CreateMultiPointFromCoords(centroids.ToArray()));
        var triangles = builder.GetTriangles(gf);

        // ── Phase C: init sentinel, then rasterise each triangle ─────────────
        var splat = new Splatmap(width, height);
        for (int i = 0; i < splat.Pixels.Length; i++)
            splat.Pixels[i] = SplatPixel.AllUnused;

        foreach (var geom in triangles.Geometries)
        {
            var ring = geom.Boundary.Coordinates;
            if (ring.Length < 3) continue;
            var vA = ring[0]; var vB = ring[1]; var vC = ring[2];

            // Map each triangle vertex back to its source cell's biome (rounded-int key lookup).
            if (!coordToBiome.TryGetValue(((int)Math.Round(vA.X), (int)Math.Round(vA.Y)), out int bA)) continue;
            if (!coordToBiome.TryGetValue(((int)Math.Round(vB.X), (int)Math.Round(vB.Y)), out int bB)) continue;
            if (!coordToBiome.TryGetValue(((int)Math.Round(vC.X), (int)Math.Round(vC.Y)), out int bC)) continue;

            RasterizeTriangle(vA, vB, vC, bA, bB, bC, splat);
        }

        return splat;
    }

    private static Splatmap AllSentinel(int w, int h)
    {
        var splat = new Splatmap(w, h);
        for (int i = 0; i < splat.Pixels.Length; i++)
            splat.Pixels[i] = SplatPixel.AllUnused;
        return splat;
    }

    // Scanline fill of a triangle. For each pixel inside, compute barycentric weights, bucket the
    // 3 corner biomes (collapsing duplicates so [plains, plains, snow] → 2 entries not 3),
    // sort descending, write the top entries into the SplatPixel.
    private static void RasterizeTriangle(
        Coordinate v0, Coordinate v1, Coordinate v2,
        int biome0, int biome1, int biome2,
        Splatmap splat)
    {
        int width = splat.Width;
        int height = splat.Height;

        int xMin = Math.Max(0,          (int)Math.Min(v0.X, Math.Min(v1.X, v2.X)));
        int xMax = Math.Min(width  - 1, (int)Math.Ceiling(Math.Max(v0.X, Math.Max(v1.X, v2.X))));
        int yMin = Math.Max(0,          (int)Math.Min(v0.Y, Math.Min(v1.Y, v2.Y)));
        int yMax = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(v0.Y, Math.Max(v1.Y, v2.Y))));

        float denom = (float)((v1.Y - v2.Y) * (v0.X - v2.X) + (v2.X - v1.X) * (v0.Y - v2.Y));
        if (MathF.Abs(denom) < 1e-6f) return;   // degenerate triangle

        for (int py = yMin; py <= yMax; py++)
        {
            for (int px = xMin; px <= xMax; px++)
            {
                float w0 = (float)((v1.Y - v2.Y) * (px - v2.X) + (v2.X - v1.X) * (py - v2.Y)) / denom;
                float w1 = (float)((v2.Y - v0.Y) * (px - v2.X) + (v0.X - v2.X) * (py - v2.Y)) / denom;
                float w2 = 1f - w0 - w1;
                if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f) continue;

                // ── Bucket the 3 corner biomes (collapse duplicates) ─────────
                int bA = biome0;  float wA = w0;
                int bB = -1;      float wB = 0f;
                int bC = -1;      float wC = 0f;

                // Add corner-1
                if (biome1 == bA) wA += w1;
                else { bB = biome1; wB = w1; }

                // Add corner-2
                if (biome2 == bA)        wA += w2;
                else if (biome2 == bB)   wB += w2;
                else                     { bC = biome2; wC = w2; }

                // ── Sort by weight descending (3-element) ────────────────────
                if (wB > wA) { (bA, bB) = (bB, bA); (wA, wB) = (wB, wA); }
                if (wC > wB) { (bB, bC) = (bC, bB); (wB, wC) = (wC, wB); }
                if (wB > wA) { (bA, bB) = (bB, bA); (wA, wB) = (wB, wA); }

                splat.Pixels[py * width + px] = new SplatPixel(
                    bA >= 0 ? new SplatLayer((byte)bA, ToByteIntensity(wA)) : SplatLayer.Unused,
                    bB >= 0 ? new SplatLayer((byte)bB, ToByteIntensity(wB)) : SplatLayer.Unused,
                    bC >= 0 ? new SplatLayer((byte)bC, ToByteIntensity(wC)) : SplatLayer.Unused,
                    SplatLayer.Unused);
            }
        }
    }

    private static byte ToByteIntensity(float weight) =>
        (byte)Math.Clamp((int)Math.Round(weight * 255f), 0, 255);
}
