using System.Drawing;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Validates ACTUAL pixels that were drawn, not intended paths.
/// This is the ground truth validator - it checks what's really on the image.
/// </summary>
public static class RiverActualPixelValidator
{
    /// <summary>
    /// Validate actual drawn pixels against CK3 topology rules.
    /// This checks the REAL pixels, not coordinates we intended to draw.
    /// </summary>
    public static RiverValidation ValidateActualPixels(List<Point> actualPixels, string riverName)
    {
        var validation = new RiverValidation
        {
            RiverName = riverName,
            TotalPixels = actualPixels.Count
        };

        if (actualPixels.Count < 2)
            return validation;

        // Create lookup for fast neighbor checking
        var pixelSet = new HashSet<Point>(actualPixels);

        // Check gaps and orthogonality between consecutive pixels
        var distances = new List<int>();
        for (int i = 0; i < actualPixels.Count - 1; i++)
        {
            var current = actualPixels[i];
            var next = actualPixels[i + 1];

            int distance = ManhattanDistance(current, next);
            distances.Add(distance);

            // Check for non-orthogonal jumps
            if (distance > 0 && IsDiagonal(current, next))
            {
                validation.NonOrthogonalJumps.Add(new GapViolation
                {
                    From = current,
                    To = next,
                    Distance = distance,
                    IsDiagonal = true
                });
            }

            // Check for large gaps
            if (distance > 1)
            {
                validation.LargeGaps.Add(new GapViolation
                {
                    From = current,
                    To = next,
                    Distance = distance,
                    IsDiagonal = IsDiagonal(current, next)
                });
            }
        }

        validation.MaxGapDistance = distances.Any() ? distances.Max() : 0;
        validation.AverageGapDistance = distances.Any() ? distances.Average() : 0;

        // Check neighbor counts for each pixel
        // First pixel (source) should have 1 neighbor
        var firstNeighbors = CountOrthogonalNeighborsInSet(actualPixels[0], pixelSet);
        if (firstNeighbors != 1)
        {
            validation.EndpointViolations.Add(new PixelViolation
            {
                Pixel = actualPixels[0],
                ActualNeighbors = firstNeighbors,
                ExpectedNeighbors = 1
            });
        }

        // Last pixel (mouth) should have 1 neighbor
        var lastNeighbors = CountOrthogonalNeighborsInSet(actualPixels[^1], pixelSet);
        if (lastNeighbors != 1)
        {
            validation.EndpointViolations.Add(new PixelViolation
            {
                Pixel = actualPixels[^1],
                ActualNeighbors = lastNeighbors,
                ExpectedNeighbors = 1
            });
        }

        // Interior pixels should have exactly 2 neighbors
        for (int i = 1; i < actualPixels.Count - 1; i++)
        {
            var neighbors = CountOrthogonalNeighborsInSet(actualPixels[i], pixelSet);
            if (neighbors != 2)
            {
                validation.InteriorViolations.Add(new PixelViolation
                {
                    Pixel = actualPixels[i],
                    ActualNeighbors = neighbors,
                    ExpectedNeighbors = 2
                });
            }
        }

        // Check for duplicates
        if (actualPixels.Count != actualPixels.Distinct().Count())
        {
            var seen = new HashSet<Point>();
            foreach (var pixel in actualPixels)
            {
                if (!seen.Add(pixel))
                {
                    validation.DuplicatePixels.Add(pixel);
                }
            }
        }

        return validation;
    }

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

    private static int ManhattanDistance(Point a, Point b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    private static bool IsDiagonal(Point a, Point b)
    {
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);
        return dx > 0 && dy > 0;
    }
}
