using System.Drawing;
using ImageMagick;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Handles the case where a tributary's A* path fails because its endpoint is on the
/// wrong side of the parent river. Uses BFS to find the nearest valid connection point
/// on the parent river's edge, then A* to reach it.
///
/// CK3 river marker colour contract (for reference):
///   Blue  (0,225,255) — river body (#00e1ff); drawn for every pixel of the path
///   Green (0,255,0)   — source marker; placed at the upstream START of every river
///   Red   (255,0,0)   — tributary junction; placed where a tributary meets its parent
///   Yellow(255,252,0) — delta split (unimplemented, reserved for future expansion)
///
/// Placement of green and red markers is the responsibility of the drawing pipeline
/// (RiverImageGenerator_New), NOT this class.  This class only finds the connection
/// point pixel — the caller draws the path and then places the red marker there.
/// </summary>
public static class RiverTributaryConnector
{
    private static readonly MagickColor BlueColor = new MagickColor(0, 225, 255);  // #00e1ff
    private static readonly MagickColor GreenColor = new MagickColor(0, 255, 0);
    private static readonly MagickColor RedColor = new MagickColor(255, 0, 0);

    /// <summary>
    /// When A* fails for a tributary segment, search for a valid connection point on the
    /// parent river's edge via BFS, then path to it.
    /// The returned path ends at the white pixel where the red junction marker should be
    /// placed by the caller after drawing.
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
        string riverName,
        HashSet<Point>? tributaryPixels = null)
    {
        int manhattanToTarget = Math.Abs(intendedDestination.X - lastValidPixel.X)
                              + Math.Abs(intendedDestination.Y - lastValidPixel.Y);

        // ── New algorithm: permissive A* → find first violation → trim → local BFS → strict A* ──
        //
        // Instead of BFS from lastValidPixel (which may be hundreds of pixels away), run a
        // permissive A* (no Pass 2) first to get as close as possible to the destination.
        // Walk the permissive path to find the first pixel adjacent to the parent body (violation).
        // Trim 3 pixels back from that violation point → intermediatePoint.
        // BFS from intermediatePoint (much closer to parent) to find the valid connection pixel.
        // Strict A* from intermediatePoint to the connection pixel.
        // Return trimmedPermissivePath + strictPath as one combined path.

        var permissivePath = RiverPathGenerator.FindOrthogonalPath(
            lastValidPixel,
            intendedDestination,
            image,
            excludeFromPass2: lastValidPixel,
            permissive: true);

        if (permissivePath == null)
        {
            Logger.Info($"  {riverName}: permissive A* returned null, falling through to original BFS");
        }
        else if (permissivePath.Count < 2)
        {
            Logger.Info($"  {riverName}: permissive A* path too short ({permissivePath.Count} px), falling through to original BFS");
        }
        else
        {
            int vIdx = FindFirstPass2Violation(permissivePath, image, tributaryPixels);

            Logger.Info($"  {riverName}: permissive path {permissivePath.Count} px, first violation at index {(vIdx == -1 ? "none" : $"{vIdx} of {permissivePath.Count - 1}")}");

            if (vIdx == -1)
            {
                // No violations at all — the permissive path would pass strict rules too.
                // Return it directly; the connection already touches the parent body.
                return permissivePath;
            }

            if (vIdx < 4)
            {
                Logger.Info($"  {riverName}: violation at index {vIdx} too early to trim, falling through to original BFS");
            }
            else
            {
                // Enough clear pixels exist before the violation to trim back 3 and get a clean
                // intermediate anchor closer to the parent body.
                var trimmedPath = permissivePath.Take(vIdx - 3).ToList();
                var intermediatePoint = trimmedPath[^1];

                Logger.Info($"  {riverName}: trimmed to index {vIdx - 4}, intermediatePoint=({intermediatePoint.X},{intermediatePoint.Y})");

                // BFS from intermediatePoint — much closer to the parent body, so we need a
                // smaller search radius and get a geometrically nicer connection point.
                var intermediateSet = tributaryPixels != null
                    ? new HashSet<Point>(tributaryPixels.Concat(trimmedPath))
                    : new HashSet<Point>(trimmedPath);
                int localRadius = Math.Max(60, manhattanToTarget / 3 + 20);
                var connectionPoint = BFSSearchForConnectionPoint(intermediatePoint, image, localRadius, intermediateSet);

                if (connectionPoint == null)
                {
                    Logger.Info($"  {riverName}: BFS from intermediatePoint found no connection (radius={localRadius}), falling through to original BFS");
                }
                else
                {
                    Logger.Info($"  {riverName}: connection point at ({connectionPoint.Value.X},{connectionPoint.Value.Y})");

                    // Strict A* from intermediatePoint (white pixel) to the connection point.
                    var strictPath = RiverPathGenerator.FindOrthogonalPath(
                        intermediatePoint,
                        connectionPoint.Value,
                        image);

                    if (strictPath == null || strictPath.Count == 0)
                    {
                        Logger.Info($"  {riverName}: strict A* from intermediatePoint failed, falling through to original BFS");
                    }
                    else
                    {
                        // Combine trimmed permissive part + strict connection part.
                        var combined = new List<Point>(trimmedPath);
                        int startIdx = strictPath[0] == intermediatePoint ? 1 : 0;
                        combined.AddRange(strictPath.Skip(startIdx));
                        Logger.Info($"  {riverName}: fallback succeeded (permissive+strict), {combined.Count} px total");
                        return combined;
                    }
                }
            }
        }

        // ── Original algorithm: BFS from lastValidPixel ──
        // Scale the search radius to at least cover the Manhattan distance to the intended
        // destination, so that a far-away parent body is still reachable.
        int searchRadius = Math.Max(150, manhattanToTarget + 50);
        var origConnectionPoint = BFSSearchForConnectionPoint(lastValidPixel, image, searchRadius,
            tributaryPixels: tributaryPixels);

        if (origConnectionPoint == null)
        {
            Logger.Info($"  Tributary fallback FAILED: No valid connection point found for {riverName} (searched {searchRadius}px radius from ({lastValidPixel.X},{lastValidPixel.Y}))");
            return null;
        }

        Logger.Info($"  Tributary fallback: Found connection point at ({origConnectionPoint.Value.X},{origConnectionPoint.Value.Y}) for {riverName}");

        // A* from last valid pixel to the connection point.
        // Pass excludeFromPass2: lastValidPixel so A* can leave the (now-blue) tributary
        // endpoint without every neighbour being blocked by Pass 2.
        var pathToConnection = RiverPathGenerator.FindOrthogonalPath(
            lastValidPixel,
            origConnectionPoint.Value,
            image,
            excludeFromPass2: lastValidPixel);

        if (pathToConnection == null || pathToConnection.Count == 0)
        {
            Logger.Info($"  Tributary fallback FAILED: A* could not path to connection point for {riverName}");
            return null;
        }

        return pathToConnection;
    }

    /// <summary>
    /// Walk <paramref name="path"/> starting at index 1 (skipping the blue start pixel) and
    /// return the index of the first pixel that is adjacent to one or more parent-body blue
    /// pixels (i.e. would be rejected by Pass 2 in strict A*).
    /// Returns -1 if the entire path is clean.
    /// </summary>
    private static int FindFirstPass2Violation(List<Point> path, MagickImage image,
        HashSet<Point>? tributaryPixels)
    {
        for (int i = 1; i < path.Count; i++)
        {
            var p = path[i];

            // Case 1: the pixel ITSELF is a non-tributary river pixel.
            // Permissive A* can now pass through river pixels, so detect when the path
            // enters the parent body (or any other existing river).
            var color = GetPixelColor(p, image);
            if (color != null && !IsPassableColor(color) &&
                (tributaryPixels == null || !tributaryPixels.Contains(p)))
                return i;

            // Case 2: the pixel is adjacent to a parent-body blue pixel (running alongside).
            if (CountAdjacentParentBlue(p, image, tributaryPixels) > 0)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// BFS flood-fill from start to find the nearest pixel that qualifies as a valid
    /// tributary connection point: a non-river pixel adjacent to exactly 1 blue pixel
    /// that does NOT belong to the tributary itself (i.e. is a parent-body pixel).
    /// </summary>
    /// <param name="tributaryPixels">
    /// All pixels already drawn for this tributary.  The BFS ignores these when counting
    /// blue neighbours so it only counts parent-river blue pixels.
    /// </param>
    private static Point? BFSSearchForConnectionPoint(Point start, MagickImage image, int maxRadius,
        HashSet<Point>? tributaryPixels = null)
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
            if (IsValidConnectionPoint(current, image, tributaryPixels))
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
    /// 1 blue pixel that belongs to the PARENT river (not this tributary).
    /// This is the pixel where the red tributary-junction marker will be placed — it sits
    /// on the border of the parent river's blue body.
    /// NOTE: placement of the red pixel is handled by the caller after path drawing.
    /// </summary>
    private static bool IsValidConnectionPoint(Point p, MagickImage image,
        HashSet<Point>? tributaryPixels = null)
    {
        var color = GetPixelColor(p, image);
        if (color == null) return false;

        if (!IsPassableColor(color)) return false;

        int parentBlueNeighbors = CountAdjacentParentBlue(p, image, tributaryPixels);
        return parentBlueNeighbors == 1;
    }

    /// <summary>
    /// Count orthogonal neighbours of <paramref name="p"/> that are blue AND do not belong
    /// to the current tributary (i.e. are parent-body pixels).
    /// </summary>
    private static int CountAdjacentParentBlue(Point p, MagickImage image,
        HashSet<Point>? tributaryPixels)
    {
        int count = 0;
        Point[] neighbors = {
            new Point(p.X, p.Y - 1),
            new Point(p.X, p.Y + 1),
            new Point(p.X - 1, p.Y),
            new Point(p.X + 1, p.Y)
        };

        foreach (var n in neighbors)
        {
            if (!IsInBounds(n, image)) continue;
            // Skip pixels that belong to this tributary
            if (tributaryPixels != null && tributaryPixels.Contains(n)) continue;
            var c = GetPixelColor(n, image);
            if (c != null && ColorsMatch(c, BlueColor))
                count++;
        }
        return count;
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
