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
    ///
    /// Each segment is reported via <paramref name="afterSegment"/> immediately after it is
    /// computed so the caller can draw it to the image before the next segment's A* runs.
    /// This ensures every segment sees previously-drawn segments of the same river as obstacles.
    /// </summary>
    public static List<Point> GenerateCompletePath(
        List<PointD> controlPoints,
        MagickImage image,
        string riverName,
        bool isTributary = false,
        Action<List<Point>>? afterSegment = null)
    {
        if (controlPoints.Count < 2)
            return new List<Point>();

        var completePath = new List<Point>();
        int successfulSegments = 0;
        int failedSegments = 0;

        // Convert first control point to starting point
        var currentPoint = new Point((int)controlPoints[0].X, (int)controlPoints[0].Y);
        completePath.Add(currentPoint);

        // Draw the source pixel immediately — it is never part of any segment's newPixels
        // (segments skip their leading `from` point since it's already in completePath).
        // Without this the source pixel would never be drawn or included in allActualPixels.
        afterSegment?.Invoke(new List<Point> { currentPoint });

        // A* pathfind from each control point to the next
        for (int i = 0; i < controlPoints.Count - 1; i++)
        {
            bool isLastSegment = (i == controlPoints.Count - 2);

            var fromPoint = controlPoints[i];
            var toPoint = controlPoints[i + 1];

            var from = new Point((int)fromPoint.X, (int)fromPoint.Y);
            var to = new Point((int)toPoint.X, (int)toPoint.Y);

            // Exclude `from` from Pass 2 adjacency counts — when this is not the first segment,
            // `from` was just drawn blue by the previous segment's callback and would otherwise
            // cause all four of its neighbours to be blocked by Pass 2.
            var segment = FindOrthogonalPath(from, to, image, excludeFromPass2: from);

            if (segment != null && segment.Count > 0)
            {
                // Collect only NEW pixels (skip duplicate leading point)
                int startIdx = (segment[0] == completePath[^1]) ? 1 : 0;
                var newPixels = new List<Point>();
                for (int j = startIdx; j < segment.Count; j++)
                    newPixels.Add(segment[j]);

                // Last segment only: trim so at most 3 pixels run offshore into ocean (magenta).
                // Restricted to the last segment to avoid falsely trimming a river that naturally
                // passes through a lake (also magenta) mid-route.
                if (isLastSegment && newPixels.Count > 0)
                {
                    int before = newPixels.Count;
                    newPixels = TrimAtOceanEdge(newPixels, image, maxOffshorePixels: 3);
                    if (newPixels.Count < before)
                        Console.WriteLine($"  {riverName}: trimmed last segment {before}→{newPixels.Count} pixels at ocean edge");
                }

                foreach (var p in newPixels)
                    completePath.Add(p);

                successfulSegments++;

                // Draw this segment immediately so subsequent A* calls see it as an obstacle
                if (newPixels.Count > 0)
                    afterSegment?.Invoke(newPixels);
            }
            else
            {
                Console.WriteLine($"  {riverName}: strict A* FAILED for segment {i}: ({from.X},{from.Y}) → ({to.X},{to.Y}), trying tributary fallback");

                // A* failed - try tributary fallback if this is a tributary
                if (isTributary)
                {
                    // Pass the full tributary pixel set so the BFS treats only
                    // parent-body blue pixels as valid connection targets.
                    var tributarySet = new HashSet<Point>(completePath);
                    var fallbackPath = RiverTributaryConnector.FindTributaryConnection(
                        completePath[^1],
                        to,
                        image,
                        riverName,
                        tributaryPixels: tributarySet);

                    if (fallbackPath != null && fallbackPath.Count > 0)
                    {
                        Console.WriteLine($"  Tributary fallback succeeded for {riverName} segment {i}");
                        int startIdx = (fallbackPath[0] == completePath[^1]) ? 1 : 0;
                        var newPixels = new List<Point>();
                        for (int j = startIdx; j < fallbackPath.Count; j++)
                        {
                            completePath.Add(fallbackPath[j]);
                            newPixels.Add(fallbackPath[j]);
                        }

                        successfulSegments++;

                        if (newPixels.Count > 0)
                            afterSegment?.Invoke(newPixels);

                        // River has merged into parent — discard remaining segments
                        Console.WriteLine($"  {riverName}: connected to parent, discarding remaining segments");
                        break;
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
    /// and pixels adjacent to existing river pixels (Pass 2 adjacency filtering).
    /// The destination pixel always bypasses the adjacency check to allow tributary connections.
    /// </summary>
    /// <param name="excludeFromPass2">
    /// A pixel to exclude from Pass 2 adjacency counts. Pass the segment's <c>from</c> pixel
    /// here so that A* can still leave the (now-blue) segment start without all neighbours blocked.
    /// </param>
    internal static List<Point>? FindOrthogonalPath(Point from, Point to, MagickImage image, Point? excludeFromPass2 = null, bool permissive = false)
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

        // Pass 2: adjacency-to-river checker.
        // Counts any orthogonally adjacent pixel that belongs to an EXISTING river
        // (blue = river body, red = tributary junction, green = river source).
        // Red and green markers already in the image were placed by previously-drawn
        // rivers, so a new river must not run alongside them either.
        // Excludes `excludeFromPass2` so the segment-start pixel (now blue from the
        // previous segment's draw) does not block all four of its own neighbours.
        int CountAdjacent(Point p) => CountAdjacentRiverPixels(p, image, excludeFromPass2);

        // Create pathfinder with orthogonal-only movement and two-pass filtering.
        // permissive mode disables Pass 2 (adjacency check) so A* can run alongside rivers.
        var pathfinder = new AStarPathfinder(
            (int)image.Width,
            (int)image.Height,
            IsPassable,
            allowDiagonal: false,
            countAdjacentBlue: permissive ? null : CountAdjacent
        );

        // Calculate max iterations based on distance
        int distance = Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y);
        int maxIterations = Math.Max(20000, distance * 4);

        return pathfinder.FindPath(from, to, maxIterations);
    }

    /// <summary>
    /// Count how many orthogonally adjacent pixels belong to any existing river
    /// (blue = river body, red = tributary junction, green = river source).
    /// Used for Pass 2 adjacency filtering in A* — a new river must not run
    /// alongside any previously-drawn river marker of any colour.
    /// </summary>
    /// <param name="exclude">A neighbour pixel to skip when counting (e.g. the segment's <c>from</c> pixel).</param>
    internal static int CountAdjacentRiverPixels(Point p, MagickImage image, Point? exclude = null)
    {
        return CountAdjacentMatchingPixels(p, image, exclude,
            new MagickColor(0, 0, 180),    // blue  – river body
            new MagickColor(255, 0, 0),    // red   – tributary junction
            new MagickColor(0, 255, 0));   // green – river source
    }

    /// <summary>
    /// Count how many orthogonally adjacent pixels are blue (river body only).
    /// Used by the tributary connection-point search, which looks specifically for
    /// the edge of the parent river's body.
    /// </summary>
    /// <param name="exclude">
    /// A neighbour pixel to skip when counting — pass the tributary's own last pixel so that
    /// the BFS doesn't count the (already-blue) tributary endpoint as a "parent body" pixel.
    /// </param>
    internal static int CountAdjacentBluePixels(Point p, MagickImage image, Point? exclude = null)
    {
        return CountAdjacentMatchingPixels(p, image, exclude, new MagickColor(0, 0, 180));
    }

    private static int CountAdjacentMatchingPixels(Point p, MagickImage image,
        Point? exclude, params MagickColor[] matchColors)
    {
        int count = 0;

        Point[] neighbors = {
            new Point(p.X, p.Y - 1),
            new Point(p.X, p.Y + 1),
            new Point(p.X - 1, p.Y),
            new Point(p.X + 1, p.Y)
        };

        using var pixels = image.GetPixels();
        foreach (var neighbor in neighbors)
        {
            if (neighbor.X < 0 || neighbor.X >= image.Width ||
                neighbor.Y < 0 || neighbor.Y >= image.Height)
                continue;

            // Skip the excluded pixel (e.g. the segment's 'from' that was just drawn blue)
            if (exclude.HasValue && neighbor == exclude.Value)
                continue;

            try
            {
                var pixel = pixels.GetPixel(neighbor.X, neighbor.Y);
                if (pixel == null) continue;

                var color = pixel.ToColor();
                if (color == null) continue;

                foreach (var match in matchColors)
                {
                    if (ColorsMatch(color, match))
                    {
                        count++;
                        break;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        return count;
    }

    /// <summary>
    /// Scans <paramref name="pixels"/> for ocean (magenta 255,0,255) pixels and trims the list
    /// so that at most <paramref name="maxOffshorePixels"/> ocean pixels remain at the end.
    /// All pixels before the first ocean pixel are always kept.
    /// If the path never enters ocean, the original list is returned unchanged.
    /// </summary>
    private static List<Point> TrimAtOceanEdge(List<Point> pixels, MagickImage image, int maxOffshorePixels)
    {
        using var px = image.GetPixels();
        int offshoreCount = 0;

        for (int k = 0; k < pixels.Count; k++)
        {
            var p = pixels[k];
            if (p.X < 0 || p.X >= image.Width || p.Y < 0 || p.Y >= image.Height)
                continue;

            try
            {
                var color = px.GetPixel(p.X, p.Y)?.ToColor();
                // Ocean = magenta (255, 0, 255)
                if (color != null && color.R == 255 && color.G == 0 && color.B == 255)
                {
                    offshoreCount++;
                    if (offshoreCount >= maxOffshorePixels)
                        return pixels.Take(k + 1).ToList(); // keep up to and including this pixel
                }
            }
            catch { /* ignore */ }
        }

        return pixels; // fewer than maxOffshorePixels ocean pixels — no trimming needed
    }

    private static bool ColorsMatch(IMagickColor<byte> a, IMagickColor<byte> b)
    {
        return a.R == b.R && a.G == b.G && a.B == b.B;
    }
}
