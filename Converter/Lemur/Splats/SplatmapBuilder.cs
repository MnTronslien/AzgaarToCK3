using Converter.Lemur.Deserialization;
using Converter.Lemur.Entities;
using Converter.Lemur.Writers;

namespace Converter.Lemur.Splats;

// Top-level Splatmap builder. Coordinates the three phases of M2:
//
//   A. BiomeWeightField.Build  — per-pixel BiomeWeightTriple from Delaunay-rasterised land cells.
//   B. SteepnessField.Compute  — per-pixel float[0..1] from heightmap gradient (if heightmap given).
//   C. Per-pixel rule loop     — ask every Material in MaterialRegistry for its weight, take top 4,
//                                 normalise so intensities sum to 255, pack into a SplatPixel.
//
// Steepness materials (hills, mountain) need a heightmap; pass null heightmapF/heightmapBytes for
// biome-only output equivalent to M1.
public static class SplatmapBuilder
{
    // Sea-side gate width. Pixels with heightmap byte > (MaxWaterByte - this) get evaluated by
    // materials so coastal/beach rules can paint into the immediate underwater margin. Past this
    // depth we shortcut to AllUnused. Roughly matches the underwater beach falloff distance in
    // MaterialRegistry — keep them in sync.
    public const int CoastalBandUnderwater = 30;

    public static Splatmap Build(
        IReadOnlyDictionary<int, Cell> cells,
        AzgaarMapCoordinates coords,
        float[]? heightmapF = null,
        byte[]? heightmapBytes = null,
        float[]? precomputedSteepness = null)
    {
        int width = Map.MapWidth;
        int height = Map.MapHeight;
        byte maxWaterByte = HeightmapAlgorithm.MaxWaterByte;

        // ── Phase A: per-pixel biome weights ──────────────────────────────────
        var biomes = BiomeWeightField.Build(cells, coords);

        // ── Phase B: per-pixel steepness ──────────────────────────────────────
        // Use the shared field if the caller already computed it (the pipeline computes it once in
        // HeightmapWriter); else compute here (e.g. the TerrainLab paint path). Identical values.
        float[]? steepness = precomputedSteepness ?? ((heightmapF != null && heightmapBytes != null)
            ? SteepnessField.Compute(heightmapF, heightmapBytes, width, height)
            : null);

        // Pre-resolve each material's CK3 byte index once — looking it up per-pixel would be
        // 33M × N dict lookups, pointless work.
        var materials = MaterialRegistry.All;
        var materialBytes = new byte[materials.Count];
        for (int m = 0; m < materials.Count; m++)
        {
            if (!Ck3MaterialBytes.ByName.TryGetValue(materials[m].TextureName, out var b))
                throw new InvalidOperationException(
                    $"Material '{materials[m].TextureName}' not found in Ck3MaterialBytes — " +
                    "is the texture name correct, or did materials.settings change? " +
                    "Try `./TerrainLab --gen-materials` to regenerate.");
            materialBytes[m] = b;
        }

        var splat = new Splatmap(width, height);

        // Parallelise across rows. Each thread gets its own `weights` scratch buffer (allocated
        // inside the lambda) — pixel writes go to disjoint indices in splat.Pixels, biomes/
        // steepness/heightmapBytes are read-only inputs, and Material.Evaluate is a pure function
        // of PixelContext (see MaterialRegistry — no shared mutable state). ~26M Evaluate calls at
        // 8192x4096, trivially parallel.
        Parallel.For(0, height, y =>
        {
            // Per-thread scratch — must be inside the lambda so threads don't stomp each other.
            var weights = new float[materials.Count];

            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;

                // Coastal-band gate. Land pixels always run. Sea pixels within
                // CoastalBandUnderwater bytes of the waterline also run so coastal materials
                // (beaches, surf) can paint into the immediate underwater margin. Deep-sea pixels
                // skip the material loop and stay sentinel.
                bool isLand;
                bool inCoastalBand;
                float h01;
                if (heightmapBytes != null)
                {
                    // Strict `>`: a byte == MaxWaterByte is water (CK3 renders it as ocean), so
                    // first land byte is MaxWaterByte + 1. The coastal-band gate is `>` so the
                    // pixel exactly at MaxWaterByte enters the loop and can pick up beach/mud
                    // weight on the sea side.
                    isLand = heightmapBytes[i] > maxWaterByte;
                    inCoastalBand = heightmapBytes[i] > (maxWaterByte - CoastalBandUnderwater);
                    h01 = heightmapBytes[i] / 255f;
                }
                else
                {
                    // No heightmap: fall back to "in biome triangle" as the gate. No underwater
                    // band because we don't know what's underwater.
                    var bt = biomes[i];
                    isLand = bt.W0 > 0 || bt.W1 > 0 || bt.W2 > 0;
                    inCoastalBand = isLand;
                    h01 = 0f;
                }

                if (!inCoastalBand)
                {
                    splat.Pixels[i] = SplatPixel.AllUnused;
                    continue;
                }

                float s = steepness != null ? steepness[i] : 0f;
                var ctx = new PixelContext(x, y, biomes[i], s, h01,
                    waterLevel01: maxWaterByte / 255f, isLand: isLand);

                // Evaluate every material once per pixel.
                for (int m = 0; m < materials.Count; m++)
                    weights[m] = materials[m].Evaluate(in ctx);

                splat.Pixels[i] = PackTopK(weights, materialBytes);
            }
        });

        return splat;
    }

    // Selects up to 4 highest-weight materials at this pixel, normalises so intensity bytes sum to
    // 255 (matches vanilla detail_intensity), packs into a SplatPixel. Insertion-sort is fine —
    // n ≤ MaterialRegistry.All.Count which is ~14 today.
    private static SplatPixel PackTopK(float[] weights, byte[] materialBytes)
    {
        int total = weights.Length;
        Span<int> idx = stackalloc int[total];
        Span<float> w = stackalloc float[total];

        int n = 0;
        for (int m = 0; m < total; m++)
        {
            if (weights[m] > 0f) { idx[n] = m; w[n] = weights[m]; n++; }
        }

        if (n == 0) return SplatPixel.AllUnused;

        // Sort descending by weight (stable: equal weights keep their registry-order)
        for (int i = 1; i < n; i++)
        {
            int curIdx = idx[i]; float curW = w[i];
            int j = i;
            while (j > 0 && w[j - 1] < curW)
            {
                idx[j] = idx[j - 1]; w[j] = w[j - 1];
                j--;
            }
            idx[j] = curIdx; w[j] = curW;
        }

        int take = Math.Min(n, 4);
        float sum = 0f;
        for (int i = 0; i < take; i++) sum += w[i];
        if (sum <= 0f) return SplatPixel.AllUnused;

        // Normalise to byte intensities summing to exactly 255. Give the last-taken slot the
        // remainder so float rounding doesn't drift the total.
        Span<SplatLayer> layers = stackalloc SplatLayer[4];
        int remainder = 255;
        for (int slot = 0; slot < take; slot++)
        {
            int intensity = (slot == take - 1)
                ? remainder
                : Math.Clamp((int)Math.Round(w[slot] / sum * 255f), 0, remainder);
            remainder -= intensity;
            layers[slot] = new SplatLayer(materialBytes[idx[slot]], (byte)intensity);
        }
        for (int slot = take; slot < 4; slot++) layers[slot] = SplatLayer.Unused;

        return new SplatPixel(layers[0], layers[1], layers[2], layers[3]);
    }
}
