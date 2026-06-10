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
    public static (List<Point> path, bool connectedAsTributary) GenerateCompletePath(
        List<PointD> controlPoints,
        MagickImage image,
        string riverName,
        bool isTributary = false,
        Func<int, int, bool>? terminalCellCheck = null,
        int maxOffshorePixels = 3,
        Action<List<Point>>? afterSegment = null)
    {
        if (controlPoints.Count < 2)
            return (new List<Point>(), false);

        var completePath = new List<Point>();
        bool connectedAsTributary = false;
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
            var fromPoint = controlPoints[i];
            var toPoint = controlPoints[i + 1];

            var from = new Point((int)fromPoint.X, (int)fromPoint.Y);
            var to = new Point((int)toPoint.X, (int)toPoint.Y);

            // Pre-check: if `to` is inside the terminal cell, skip strict A* entirely.
            // The failure branch below handles it via permissive A* + trim.
            List<Point>? segment = null;
            if (terminalCellCheck == null || !terminalCellCheck(to.X, to.Y))
            {
                // Exclude `from` from Pass 2 adjacency counts — when this is not the first segment,
                // `from` was just drawn blue by the previous segment's callback and would otherwise
                // cause all four of its neighbours to be blocked by Pass 2.
                // Seed self-avoid with the pixels committed so far so this segment cannot loop back
                // and touch the previous segment's tail across the seam.
                segment = FindOrthogonalPath(from, to, image, excludeFromPass2: from,
                    selfAvoidSeed: new HashSet<Point>(completePath),
                    exemptGoal: false);   // an ordinary control point must not be reached by touching
                                          // another river (or this river's own course) — only the
                                          // deliberate tributary-into-parent connection may do that
            }

            if (segment != null && segment.Count > 0)
            {
                // Collect only NEW pixels (skip duplicate leading point)
                int startIdx = (segment[0] == completePath[^1]) ? 1 : 0;
                var newPixels = new List<Point>();
                for (int j = startIdx; j < segment.Count; j++)
                    newPixels.Add(segment[j]);

                foreach (var p in newPixels)
                    completePath.Add(p);

                successfulSegments++;

                // Draw this segment immediately so subsequent A* calls see it as an obstacle
                if (newPixels.Count > 0)
                    afterSegment?.Invoke(newPixels);
            }
            else
            {
                Logger.Debug($"  {riverName}: strict A* FAILED for segment {i}: ({from.X},{from.Y}) → ({to.X},{to.Y}), trying terminal cell / tributary fallback");

                // NEW: Terminal-cell interceptor — fires for any river (tributary or not)
                // when `to` is inside the terminal cell (pre-skipped or genuinely failed).
                if (terminalCellCheck != null && terminalCellCheck(to.X, to.Y))
                {
                    // Route into the terminal (ocean/lake) cell. Prefer a Pass-2 path that still
                    // avoids OTHER rivers — two mouths emptying into the same coast must not run
                    // alongside each other — with the goal exempt so it can reach the open-water
                    // target. Only if that's blocked do we fall back to fully permissive routing
                    // (the original behaviour). Either way self-avoid stops it doubling onto its tail.
                    // Route into the terminal cell while still avoiding OTHER rivers (Pass-2 on).
                    // The goal sits in open water, so Pass-2 — which only counts river pixels, not
                    // land/sea — reaches it normally; it's rejected only when the goal is adjacent to
                    // another river (two mouths meeting at the same coast). exemptGoal stays false so
                    // that case fails here and the river truncates a pixel short of the water below,
                    // rather than being forced onto its neighbour. No permissive fallback: fully
                    // permissive routing is exactly what let the mouths run alongside each other.
                    var seed = new HashSet<Point>(completePath);
                    var permPath = FindOrthogonalPath(
                        completePath[^1], to, image,
                        excludeFromPass2: completePath[^1], permissive: false,
                        selfAvoidSeed: seed, exemptGoal: false);

                    if (permPath != null && permPath.Count > 1)
                    {
                        // Find the Nth pixel inside the terminal cell (N = maxOffshorePixels).
                        // Trim everything after — no pixels are ever erased.
                        int terminalCount = 0, trimIdx = permPath.Count - 1;
                        for (int k = 1; k < permPath.Count; k++)
                        {
                            if (terminalCellCheck(permPath[k].X, permPath[k].Y))
                            {
                                terminalCount++;
                                if (terminalCount == maxOffshorePixels) { trimIdx = k; break; }
                            }
                        }

                        var trimmedPermPath = permPath.Take(trimIdx + 1).ToList();
                        int startIdx = trimmedPermPath[0] == completePath[^1] ? 1 : 0;
                        var newPixels = trimmedPermPath.Skip(startIdx).ToList();

                        foreach (var p in newPixels) completePath.Add(p);
                        successfulSegments++;
                        if (newPixels.Count > 0) afterSegment?.Invoke(newPixels);

                        Logger.Debug(
                            $"  {riverName}: terminal-cell cutoff seg {i}, " +
                            $"{newPixels.Count} px ({terminalCount} inside terminal cell)");

                        break; // River terminates here — discard remaining segments
                    }
                }

                // A* failed — try tributary fallback if this is a tributary
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
                        Logger.Debug($"  {riverName}: tributary join succeeded on segment {i}");
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
                        connectedAsTributary = true;
                        Logger.Debug($"  {riverName}: joined another river as tributary, discarding remaining segments");
                        break;
                    }
                }

                // Strict A* failed and no terminal/tributary handler applied. Previously we jammed
                // the raw destination point in here — but that bypasses BOTH the self-avoid and the
                // Pass-2 adjacency guards, so it routinely landed orthogonally adjacent to the
                // river's own body (or another river), producing degree-3 violations and a visible
                // gap. Strict A* only fails when the start is boxed in (every clean route blocked),
                // i.e. the river physically cannot continue without touching something. End it at the
                // last cleanly-pathed pixel instead of forcing an invalid one.
                failedSegments++;
                Logger.Debug($"  WARNING: A* failed for {riverName} segment {i}: ({from.X},{from.Y}) → ({to.X},{to.Y}) — truncating river at last clean pixel ({completePath[^1].X},{completePath[^1].Y})");
                break;
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

        Logger.Debug($"  Path generation for {riverName}: {successfulSegments} segments OK, {failedSegments} failed, {deduped.Count} total pixels");

        return (deduped, connectedAsTributary);
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
    internal static List<Point>? FindOrthogonalPath(Point from, Point to, MagickImage image, Point? excludeFromPass2 = null, bool permissive = false, IReadOnlySet<Point>? selfAvoidSeed = null, bool exemptGoal = true)
    {
        // If points are the same, return empty
        if (from == to)
            return new List<Point> { from };

        // Create passability function that avoids existing river pixels
        var blueColor = RiverImageGenerator.RiverBodyColor;
        var greenColor = new MagickColor(0, 255, 0);
        var redColor = new MagickColor(255, 0, 0);

        bool IsPassable(Point p)
        {
            // Permissive mode: allow ALL pixels — A* pathfinder handles bounds.
            // This lets the path cross rivers, enabling "as the crow flies" routing
            // so FindFirstPass2Violation can identify exactly where the path enters
            // or runs alongside an existing river.
            if (permissive)
                return true;

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
        // selfAvoid is ALWAYS on: it keeps a path from looping against itself into a 2×2 block
        // (a degree-3 pixel CK3 rejects). The in-progress path is invisible to Pass 2, so this is
        // the only guard against self-touch — and it must hold in permissive mode too, where the
        // terminal-cell mouth approach is drawn (that path was the sole source of the 2×2 blobs).
        // It is orthogonal to Pass 2: it never blocks approaching another river, only oneself.
        var pathfinder = new AStarPathfinder(
            (int)image.Width,
            (int)image.Height,
            IsPassable,
            allowDiagonal: false,
            countAdjacentBlue: permissive ? null : CountAdjacent,
            selfAvoid: true,
            selfAvoidSeed: selfAvoidSeed,
            exemptGoal: exemptGoal
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
            RiverImageGenerator.RiverBodyColor,  // river body (#000064)
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
        return CountAdjacentMatchingPixels(p, image, exclude, RiverImageGenerator.RiverBodyColor);
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

    private static bool ColorsMatch(IMagickColor<byte> a, IMagickColor<byte> b)
    {
        return a.R == b.R && a.G == b.G && a.B == b.B;
    }
}
