using System.Drawing;
using ImageMagick;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Handles the case where a tributary's A* path fails because its endpoint is on the
/// wrong side of the parent river. Uses BFS to find the nearest valid connection point
/// on the parent river's edge, then A* to reach it.
/// </summary>
public static class RiverTributaryConnector
{
    private static readonly MagickColor BlueColor = new MagickColor(0, 0, 180);
    private static readonly MagickColor GreenColor = new MagickColor(0, 255, 0);
    private static readonly MagickColor RedColor = new MagickColor(255, 0, 0);

    /// <summary>
    /// When A* fails for a tributary segment, search for a valid connection point on the
    /// parent river's edge via BFS, then path to it.
    /// </summary>
    /// <param name="lastValidPixel">Last successfully pathed pixel before the failure</param>
    /// <param name="intendedDestination">The original intended destination (used for logging)</param>
    /// <param name="image">The current rivers image (with parent river already drawn in blue)</param>
    /// <param name="riverName">Name for logging</param>
    /// <returns>A path from lastValidPixel to the connection point, or null if none found</returns>
    public static List<Point>? FindTributaryConnection(
        Point lastValidPixel,
        Point intendedDestination,
        MagickImage image,
        string riverName)
    {
        // BFS to find nearest valid connection point on the parent river's edge
        var connectionPoint = BFSSearchForConnectionPoint(lastValidPixel, image, maxRadius: 100);

        if (connectionPoint == null)
        {
            Console.WriteLine($"  Tributary fallback FAILED: No valid connection point found for {riverName} (searched 100px radius from ({lastValidPixel.X},{lastValidPixel.Y}))");
            return null;
        }

        Console.WriteLine($"  Tributary fallback: Found connection point at ({connectionPoint.Value.X},{connectionPoint.Value.Y}) for {riverName}");

        // A* from last valid pixel to the connection point
        // Same two-pass filtering applies; destination bypass allows reaching the river edge
        var pathToConnection = RiverPathGenerator.FindOrthogonalPath(
            lastValidPixel,
            connectionPoint.Value,
            image);

        if (pathToConnection == null || pathToConnection.Count == 0)
        {
            Console.WriteLine($"  Tributary fallback FAILED: A* could not path to connection point for {riverName}");
            return null;
        }

        return pathToConnection;
    }

    /// <summary>
    /// BFS flood-fill from start to find the nearest pixel that qualifies as a valid
    /// tributary connection point: a non-river pixel adjacent to exactly 1 blue pixel.
    /// </summary>
    private static Point? BFSSearchForConnectionPoint(Point start, MagickImage image, int maxRadius)
    {
        var queue = new Queue<(Point pixel, int distance)>();
        var visited = new HashSet<Point>();

        queue.Enqueue((start, 0));
        visited.Add(start);

        while (queue.Count > 0)
        {
            var (current, distance) = queue.Dequeue();

            if (distance > maxRadius)
                continue;

            // Check if this pixel qualifies as a connection point
            if (IsValidConnectionPoint(current, image))
                return current;

            // Expand orthogonally
            Point[] neighbors = {
                new Point(current.X, current.Y - 1),
                new Point(current.X, current.Y + 1),
                new Point(current.X - 1, current.Y),
                new Point(current.X + 1, current.Y)
            };

            foreach (var neighbor in neighbors)
            {
                if (visited.Contains(neighbor)) continue;
                if (!IsInBounds(neighbor, image)) continue;

                // Only traverse through passable (non-river) pixels
                var color = GetPixelColor(neighbor, image);
                if (color != null && IsPassableColor(color))
                {
                    visited.Add(neighbor);
                    queue.Enqueue((neighbor, distance + 1));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A valid connection point is a passable (white/magenta) pixel adjacent to exactly
    /// 1 blue pixel (the parent river body edge).  This is the pixel where the green
    /// source marker for the current tributary will be spawned — it sits on the border
    /// of the parent river's blue body without replacing any existing pixel.
    /// </summary>
    private static bool IsValidConnectionPoint(Point p, MagickImage image)
    {
        var color = GetPixelColor(p, image);
        if (color == null) return false;

        if (!IsPassableColor(color)) return false;

        int blueNeighbors = RiverPathGenerator.CountAdjacentBluePixels(p, image);
        return blueNeighbors == 1;
    }

    private static bool IsInBounds(Point p, MagickImage image)
    {
        return p.X >= 0 && p.X < image.Width &&
               p.Y >= 0 && p.Y < image.Height;
    }

    private static IMagickColor<byte>? GetPixelColor(Point p, MagickImage image)
    {
        try
        {
            using var pixels = image.GetPixels();
            var pixel = pixels.GetPixel(p.X, p.Y);
            return pixel?.ToColor();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPassableColor(IMagickColor<byte> color)
    {
        // Passable = white (land) or magenta (ocean) — anything that is not an existing river pixel
        return !ColorsMatch(color, BlueColor) &&
               !ColorsMatch(color, GreenColor) &&
               !ColorsMatch(color, RedColor);
    }

    private static bool ColorsMatch(IMagickColor<byte> a, IMagickColor<byte> b)
    {
        return a.R == b.R && a.G == b.G && a.B == b.B;
    }
}
