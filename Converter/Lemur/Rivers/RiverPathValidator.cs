using System.Drawing;
using ImageMagick;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Validates a river path's topology based on the path data itself,
/// not the rendered image (which may have multiple overlapping rivers).
/// </summary>
public static class RiverPathValidator
{
    /// <summary>
    /// Validate a river path against CK3 topology rules using path data and image context.
    /// This checks both path topology and offshore/ocean violations.
    /// </summary>
    public static RiverValidation ValidatePath(List<PointD> path, string riverName, MagickImage? image = null)
    {
        var validation = new RiverValidation
        {
            RiverName = riverName,
            TotalPixels = path.Count
        };

        if (path.Count < 2)
            return validation;

        // Convert to pixel points
        var pixels = path.Select(p => new Point((int)p.X, (int)p.Y)).ToList();

        // Create lookup for fast neighbor checking
        var pixelSet = new HashSet<Point>(pixels);

        // Check gaps and orthogonality between consecutive pixels
        var distances = new List<int>();
        for (int i = 0; i < pixels.Count - 1; i++)
        {
            var current = pixels[i];
            var next = pixels[i + 1];

            int distance = RiverValidator.ManhattanDistance(
                new PointD(current.X, current.Y),
                new PointD(next.X, next.Y));
            distances.Add(distance);

            // Check for non-orthogonal jumps
            if (distance > 0 && RiverValidator.IsDiagonal(
                new PointD(current.X, current.Y),
                new PointD(next.X, next.Y)))
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
                    IsDiagonal = RiverValidator.IsDiagonal(
                        new PointD(current.X, current.Y),
                        new PointD(next.X, next.Y))
                });
            }
        }

        validation.MaxGapDistance = distances.Any() ? distances.Max() : 0;
        validation.AverageGapDistance = distances.Any() ? distances.Average() : 0;

        // Check neighbor counts for each pixel in the path
        // First pixel (source) should have 1 neighbor
        var firstNeighbors = CountOrthogonalNeighborsInSet(pixels[0], pixelSet);
        if (firstNeighbors != 1)
        {
            validation.EndpointViolations.Add(new PixelViolation
            {
                Pixel = pixels[0],
                ActualNeighbors = firstNeighbors,
                ExpectedNeighbors = 1
            });
        }

        // Last pixel (mouth) should have 1 neighbor
        var lastNeighbors = CountOrthogonalNeighborsInSet(pixels[^1], pixelSet);
        if (lastNeighbors != 1)
        {
            validation.EndpointViolations.Add(new PixelViolation
            {
                Pixel = pixels[^1],
                ActualNeighbors = lastNeighbors,
                ExpectedNeighbors = 1
            });
        }

        // Interior pixels should have exactly 2 neighbors
        for (int i = 1; i < pixels.Count - 1; i++)
        {
            var neighbors = CountOrthogonalNeighborsInSet(pixels[i], pixelSet);
            if (neighbors != 2)
            {
                validation.InteriorViolations.Add(new PixelViolation
                {
                    Pixel = pixels[i],
                    ActualNeighbors = neighbors,
                    ExpectedNeighbors = 2
                });
            }
        }

        // Check for duplicates (already handled by cleaning, but double-check)
        if (pixels.Count != pixels.Distinct().Count())
        {
            var seen = new HashSet<Point>();
            foreach (var pixel in pixels)
            {
                if (!seen.Add(pixel))
                {
                    validation.DuplicatePixels.Add(pixel);
                }
            }
        }

        // Check offshore runoff and midstream ocean crossings (requires image context)
        if (image != null)
        {
            // Check for excessive offshore runoff
            var (hasOffshoreViolation, offshorePixels) = RiverOffshoreChecker.CheckOffshoreRunoff(path, image, maxOffshorePixels: 3);
            if (hasOffshoreViolation)
            {
                validation.ExcessiveOffshorePixels = offshorePixels - 3; // How many pixels over the limit
            }

            // Check for midstream ocean crossings
            validation.MidstreamOceanPixels = RiverOffshoreChecker.FindMidstreamOceanPixels(path, image)
                .Select(idx => pixels[idx])
                .ToList();
        }

        return validation;
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
