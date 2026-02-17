using System.Drawing;
using ImageMagick;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Fixes self-violations in river paths by iteratively removing violating pixels
/// and re-filling gaps. Based on the Python implementation's self-violation fixing loop.
/// </summary>
public static class RiverSelfViolationFixer
{
    /// <summary>
    /// Fix self-violations in a river path through iterative removal and gap filling.
    /// Continues until the path is valid or max iterations is reached.
    /// </summary>
    public static List<PointD> FixSelfViolations(List<PointD> path, MagickImage image, string riverName, int maxIterations = 10)
    {
        if (path.Count < 3)
            return path;

        int iteration = 0;
        int totalRemoved = 0;

        while (iteration < maxIterations)
        {
            iteration++;

            // Validate current path
            var validation = RiverPathValidator.ValidatePath(path, riverName);

            // If valid, we're done!
            if (validation.IsValid)
            {
                if (Settings.Instance.Debug && iteration > 1)
                {
                    Console.WriteLine($"  Self-violation fixing for {riverName}: converged after {iteration} iterations (removed {totalRemoved} pixels)");
                }
                return path;
            }

            // Find pixels with wrong neighbor counts (excluding endpoints)
            var pixels = path.Select(p => new Point((int)p.X, (int)p.Y)).ToList();
            var pixelSet = new HashSet<Point>(pixels);
            var violatingIndices = new List<int>();

            // Check interior pixels (not first or last)
            for (int i = 1; i < pixels.Count - 1; i++)
            {
                var neighbors = CountOrthogonalNeighborsInSet(pixels[i], pixelSet);
                if (neighbors != 2)
                {
                    violatingIndices.Add(i);
                }
            }

            // If no interior violations, we can't fix further
            if (violatingIndices.Count == 0)
                break;

            // Remove violating pixels (iterate backwards to preserve indices)
            for (int i = violatingIndices.Count - 1; i >= 0; i--)
            {
                int idx = violatingIndices[i];
                path.RemoveAt(idx);
                totalRemoved++;
            }

            // After removing, we might have gaps - try to fill them
            path = RiverGapFiller.FillGaps(path, image, riverName);

            // Remove duplicates that might have been introduced
            path = RiverPathCleaner.RemoveDuplicates(path, riverName);

            // Check if path is still viable
            if (path.Count < 3)
            {
                if (Settings.Instance.Debug)
                {
                    Console.WriteLine($"  WARNING: {riverName} self-violation fixing resulted in path too short ({path.Count} points)");
                }
                break;
            }
        }

        if (iteration >= maxIterations)
        {
            if (Settings.Instance.Debug)
            {
                Console.WriteLine($"  WARNING: {riverName} self-violation fixing did not converge after {maxIterations} iterations");
            }
        }

        return path;
    }

    /// <summary>
    /// Count how many orthogonally adjacent pixels exist in the given set.
    /// </summary>
    private static int CountOrthogonalNeighborsInSet(Point point, HashSet<Point> pixelSet)
    {
        int count = 0;

        // Check 4 orthogonal neighbors
        Point[] offsets = {
            new Point(point.X, point.Y - 1), // Up
            new Point(point.X, point.Y + 1), // Down
            new Point(point.X - 1, point.Y), // Left
            new Point(point.X + 1, point.Y)  // Right
        };

        foreach (var neighbor in offsets)
        {
            if (pixelSet.Contains(neighbor))
                count++;
        }

        return count;
    }
}
