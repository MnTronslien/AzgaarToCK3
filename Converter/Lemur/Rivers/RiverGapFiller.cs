using System.Drawing;
using Converter.Lemur.Algorithms;
using ImageMagick;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Fills gaps in river paths to create valid CK3-compliant orthogonal paths.
/// Uses A* pathfinding to connect distant cell centroids.
/// </summary>
public static class RiverGapFiller
{
    /// <summary>
    /// Fill gaps in a river path, creating orthogonal connections between all points.
    /// </summary>
    /// <param name="originalPath">The original path with gaps (cell centroids)</param>
    /// <param name="image">The rivers image (to check for existing rivers)</param>
    /// <param name="riverName">River name for logging</param>
    /// <returns>A new path with all gaps filled using orthogonal pixels</returns>
    public static List<PointD> FillGaps(List<PointD> originalPath, MagickImage image, string riverName)
    {
        if (originalPath.Count < 2)
            return originalPath;

        var filledPath = new List<PointD>();
        var stats = new GapFillingStats { RiverName = riverName };

        // Add first point
        filledPath.Add(originalPath[0]);

        // Process each consecutive pair
        for (int i = 0; i < originalPath.Count - 1; i++)
        {
            var from = originalPath[i];
            var to = originalPath[i + 1];

            var fromPoint = new Point((int)from.X, (int)from.Y);
            var toPoint = new Point((int)to.X, (int)to.Y);

            int distance = RiverValidator.ManhattanDistance(from, to);

            // If points are adjacent (distance 1), no gap filling needed
            if (distance <= 1)
            {
                stats.NoGapCount++;
                continue;
            }

            // Need to fill the gap
            stats.GapCount++;
            stats.TotalGapDistance += distance;

            // Use A* to find orthogonal path
            var pathSegment = FindOrthogonalPath(fromPoint, toPoint, image);

            if (pathSegment != null)
            {
                // Add all points except the first (already in filledPath)
                for (int j = 1; j < pathSegment.Count; j++)
                {
                    filledPath.Add(new PointD(pathSegment[j].X, pathSegment[j].Y));
                }
                stats.SuccessfulFills++;
            }
            else
            {
                // Pathfinding failed - just add the destination point
                // This will leave a gap, but at least we don't lose the river entirely
                filledPath.Add(to);
                stats.FailedFills++;
                Console.WriteLine($"  WARNING: Failed to find path from ({fromPoint.X},{fromPoint.Y}) to ({toPoint.X},{toPoint.Y}) in {riverName}");
            }
        }

        // Log stats
        if (Settings.Instance.Debug && stats.GapCount > 0)
        {
            Console.WriteLine($"  Gap filling for {riverName}: {stats.SuccessfulFills}/{stats.GapCount} gaps filled, avg distance: {stats.AverageGapDistance:F1}px");
            if (stats.FailedFills > 0)
                Console.WriteLine($"    ⚠ {stats.FailedFills} gaps could not be filled");
        }

        return filledPath;
    }

    /// <summary>
    /// Find an orthogonal path between two points, avoiding existing river pixels.
    /// </summary>
    private static List<Point>? FindOrthogonalPath(Point from, Point to, MagickImage image)
    {
        // Create passability function that avoids existing river pixels
        var blueColor = new MagickColor(0, 0, 180);
        var greenColor = new MagickColor(0, 255, 0);
        var redColor = new MagickColor(255, 0, 0);

        bool IsPassable(Point p)
        {
            // Allow the start and end points to be passable even if they're blue
            if (p == from || p == to)
                return true;

            // Check if pixel is already a river color
            try
            {
                using var pixels = image.GetPixels();
                var pixel = pixels.GetPixel(p.X, p.Y);
                if (pixel == null) return false;

                var color = pixel.ToColor();
                if (color == null) return false;

                // Can't path through existing river pixels (blue/green/red)
                // But CAN path through land (white) and ocean (magenta)
                if (ColorsMatch(color, blueColor) ||
                    ColorsMatch(color, greenColor) ||
                    ColorsMatch(color, redColor))
                {
                    return false;
                }

                // All other colors (white land, magenta ocean) are passable
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Create pathfinder with orthogonal-only movement
        var pathfinder = new AStarPathfinder(
            (int)image.Width,
            (int)image.Height,
            IsPassable,
            allowDiagonal: false // CK3 requires orthogonal-only
        );

        // Find path with generous iteration limit
        // For large gaps (500+ pixels), we need MANY more iterations
        // Use distance * 4 to handle complex routing around obstacles
        int maxIterations = Math.Max(20000, RiverValidator.ManhattanDistance(
            new PointD(from.X, from.Y),
            new PointD(to.X, to.Y)) * 4);

        return pathfinder.FindPath(from, to, maxIterations);
    }

    private static bool ColorsMatch(IMagickColor<byte> a, IMagickColor<byte> b)
    {
        return a.R == b.R && a.G == b.G && a.B == b.B;
    }
}

/// <summary>
/// Statistics about gap filling operations for a river.
/// </summary>
public class GapFillingStats
{
    public string RiverName { get; set; } = string.Empty;
    public int GapCount { get; set; }
    public int NoGapCount { get; set; }
    public int SuccessfulFills { get; set; }
    public int FailedFills { get; set; }
    public int TotalGapDistance { get; set; }
    public double AverageGapDistance => GapCount > 0 ? (double)TotalGapDistance / GapCount : 0;
}
