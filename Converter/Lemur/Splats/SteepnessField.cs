using Converter.Lemur.Writers;

namespace Converter.Lemur.Splats;

// Per-pixel surface steepness, p95-normalised, in [0..1]. Zero over sea pixels.
//
// Math (carried over verbatim from HeightmapMasks.cs:52-90):
//   1. For each land pixel, compute the gradient via central differences with a kd-pixel kernel.
//   2. Steepness = 1 - 1/sqrt(gx² + gy² + 1)  (= 1 - N.z of the surface normal)
//   3. Find the 95th percentile across all land pixels.
//   4. Normalise: each pixel's steepness = (raw steepness / p95), clamped to [0..1].
//
// The heightmap-already-Gaussian-blurred assumption: kd=4 safely lands within smoothed terrain
// rather than on triangle facet edges.
//
// Both HeightmapMasks (PNG mask writer) and SplatmapBuilder consume this — single source of truth.
public static class SteepnessField
{
    private const int kd = 4;

    /// <summary>
    /// Compute p95-normalised steepness for every pixel. Returns a fresh float[width*height].
    /// Pixels over sea (heightmapBytes[i] &lt; CK3WaterLevel) are 0.
    /// </summary>
    public static float[] Compute(float[] heightmapF, byte[] heightmapBytes, int width, int height)
    {
        byte wl = HeightmapAlgorithm.CK3WaterLevel;
        var raw = new float[width * height];

        // Step 1+2: central-difference gradient → steepness scalar
        for (int y = kd; y < height - kd; y++)
        {
            for (int x = kd; x < width - kd; x++)
            {
                int i = y * width + x;
                if (heightmapBytes[i] < wl) continue;     // sea pixel — leave 0

                // Zero out a component if either sample crosses the ocean boundary.
                float gx = (heightmapBytes[i + kd]         >= wl && heightmapBytes[i - kd]         >= wl)
                    ? (heightmapF[i + kd]         - heightmapF[i - kd])         / (2f * kd) : 0f;
                float gy = (heightmapBytes[i + kd * width] >= wl && heightmapBytes[i - kd * width] >= wl)
                    ? (heightmapF[i + kd * width] - heightmapF[i - kd * width]) / (2f * kd) : 0f;
                raw[i] = 1f - 1f / MathF.Sqrt(gx * gx + gy * gy + 1f);
            }
        }

        // Step 3: p95 over land pixels (1024 bins covering [0, 1.024), bin width 0.001)
        var hist = new long[1024];
        int landCount = 0;
        for (int i = 0; i < heightmapBytes.Length; i++)
        {
            if (heightmapBytes[i] < wl) continue;
            landCount++;
            hist[Math.Clamp((int)(raw[i] * 1000f), 0, 1023)]++;
        }

        float p95 = 0.001f;
        if (landCount > 0)
        {
            long target = (long)(landCount * 0.95), cum = 0;
            for (int b = 0; b < hist.Length; b++)
            {
                cum += hist[b];
                if (cum >= target) { p95 = Math.Max(0.001f, b / 1000f); break; }
            }
        }

        // Step 4: normalise in place
        if (p95 > 0f)
        {
            float invP95 = 1f / p95;
            for (int i = 0; i < raw.Length; i++)
                raw[i] = Math.Clamp(raw[i] * invP95, 0f, 1f);
        }

        return raw;
    }
}
