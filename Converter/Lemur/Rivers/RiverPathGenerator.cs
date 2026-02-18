using System.Drawing;
using Converter.Lemur.Algorithms;
using ImageMagick;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Generates complete river paths using A* pathfinding between all cell centroids.
/// No line drawing - pure A* creates the entire orthogonal path.
/// </summary>
public static class RiverPathGenerator
{
    /// <summary>
    /// Generate a complete orthogonal river path by A* pathfinding between control points.
    /// This is the ONLY way we create river pixels - no line drawing, pure A* control.
    /// </summary>
    public static List<Point> GenerateCompletePath(
        List<PointD> controlPoints,
        MagickImage image,
        string riverName,
        bool isTributary = false)
    {
        if (controlPoints.Count < 2)
            return new List<Point>();

        var completePath = new List<Point>();
        int successfulSegments = 0;
        int failedSegments = 0;

        // Convert first control point to starting point
        var currentPoint = new Point((int)controlPoints[0].X, (int)controlPoints[0].Y);
        completePath.Add(currentPoint);

        // A* pathfind from each control point to the next
        for (int i = 0; i < controlPoints.Count - 1; i++)
        {
            var fromPoint = controlPoints[i];
            var toPoint = controlPoints[i + 1];

            var from = new Point((int)fromPoint.X, (int)fromPoint.Y);
            var to = new Point((int)toPoint.X, (int)toPoint.Y);

            // Use A* to find orthogonal path
            var segment = FindOrthogonalPath(from, to, image);

            if (segment != null && segment.Count > 0)
            {
                // Add segment (skip first point if it's the same as our current point)
                int startIdx = (segment[0] == completePath[^1]) ? 1 : 0;

                for (int j = startIdx; j < segment.Count; j++)
                {
                    completePath.Add(segment[j]);
                }

                successfulSegments++;
            }
            else
            {
                // A* failed - try tributary fallback if this is a tributary
                if (isTributary)
                {
                    var fallbackPath = RiverTributaryConnector.FindTributaryConnection(
                        completePath[^1],
                        to,
                        image,
                        riverName);

                    if (fallbackPath != null && fallbackPath.Count > 0)
                    {
                        Console.WriteLine($"  Tributary fallback succeeded for {riverName} segment {i}");
                        int startIdx = (fallbackPath[0] == completePath[^1]) ? 1 : 0;
                        for (int j = startIdx; j < fallbackPath.Count; j++)
                        {
                            completePath.Add(fallbackPath[j]);
                        }
                        successfulSegments++;
                        continue;
                    }
                }

                // Fallback failed or not a tributary - add destination and log
                if (to != completePath[^1])
                {
                    completePath.Add(to);
                }
                failedSegments++;

                Console.WriteLine($"  WARNING: A* failed for {riverName} segment {i}: ({from.X},{from.Y}) → ({to.X},{to.Y})");
            }
        }

        // Remove any duplicate consecutive points
        var deduped = new List<Point> { completePath[0] };
        for (int i = 1; i < completePath.Count; i++)
        {
            if (completePath[i] != completePath[i - 1])
            {
                deduped.Add(completePath[i]);
            }
        }

        if (Settings.Instance.Debug && (successfulSegments > 0 || failedSegments > 0))
        {
            Console.WriteLine($"  Path generation for {riverName}: {successfulSegments} segments OK, {failedSegments} failed, {deduped.Count} total pixels");
        }

        return deduped;
    }

    /// <summary>
    /// Find orthogonal path between two points using A*, avoiding existing river pixels
    /// and pixels adjacent to existing blue (river) pixels (Pass 2 adjacency filtering).
    /// The destination pixel always bypasses the adjacency check to allow tributary connections.
    /// </summary>
    internal static List<Point>? FindOrthogonalPath(Point from, Point to, MagickImage image)
    {
        // If points are the same, return empty
        if (from == to)
            return new List<Point> { from };

        // Create passability function that avoids existing river pixels
        var blueColor = new MagickColor(0, 0, 180);
        var greenColor = new MagickColor(0, 255, 0);
        var redColor = new MagickColor(255, 0, 0);

        bool IsPassable(Point p)
        {
            // Allow the start and end points
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

        // Pass 2: adjacency-to-blue checker
        // Returns how many blue pixels are orthogonally adjacent to the given point.
        // A* uses this to discard candidates that would create parallel/touching rivers.
        int CountAdjacent(Point p) => CountAdjacentBluePixels(p, image);

        // Create pathfinder with orthogonal-only movement and two-pass filtering
        var pathfinder = new AStarPathfinder(
            (int)image.Width,
            (int)image.Height,
            IsPassable,
            allowDiagonal: false,
            countAdjacentBlue: CountAdjacent
        );

        // Calculate max iterations based on distance
        int distance = Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y);
        int maxIterations = Math.Max(20000, distance * 4);

        return pathfinder.FindPath(from, to, maxIterations);
    }

    /// <summary>
    /// Count how many orthogonally adjacent pixels are blue (existing river pixels).
    /// Used for Pass 2 adjacency filtering in A* to prevent parallel/touching rivers.
    /// </summary>
    internal static int CountAdjacentBluePixels(Point p, MagickImage image)
    {
        var blueColor = new MagickColor(0, 0, 180);
        int count = 0;

        Point[] neighbors = {
            new Point(p.X, p.Y - 1),  // Up
            new Point(p.X, p.Y + 1),  // Down
            new Point(p.X - 1, p.Y),  // Left
            new Point(p.X + 1, p.Y)   // Right
        };

        using var pixels = image.GetPixels();
        foreach (var neighbor in neighbors)
        {
            if (neighbor.X < 0 || neighbor.X >= image.Width ||
                neighbor.Y < 0 || neighbor.Y >= image.Height)
                continue;

            try
            {
                var pixel = pixels.GetPixel(neighbor.X, neighbor.Y);
                if (pixel == null) continue;

                var color = pixel.ToColor();
                if (color != null && ColorsMatch(color, blueColor))
                    count++;
            }
            catch
            {
                // ignore
            }
        }

        return count;
    }

    private static bool ColorsMatch(IMagickColor<byte> a, IMagickColor<byte> b)
    {
        return a.R == b.R && a.G == b.G && a.B == b.B;
    }
}
