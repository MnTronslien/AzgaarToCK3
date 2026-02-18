using System.Drawing;
using Converter.Lemur.Algorithms;
using ImageMagick;
using Xunit;

namespace Converter.Tests.Rivers;

/// <summary>
/// Tests for AStarPathfinder using manually-constructed pixel images as the passability source.
///
/// Image conventions match the CK3 river map specification:
///   White   (255,255,255) = passable land
///   Blue    (0,0,180)     = river body (impassable, triggers Pass 2 adjacency exclusion)
///   Green   (0,255,0)     = river source marker (impassable, does NOT trigger Pass 2)
///   Red     (255,0,0)     = tributary junction marker (impassable, does NOT trigger Pass 2)
///
/// Only blue triggers the Pass 2 adjacency exclusion zone.  Red and green are
/// endpoint markers — it must remain possible to path UP TO them.
///
/// Each test documents its image layout as an ASCII grid for easy reading.
/// </summary>
public class AStarPathfinderTests
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Shared colour constants  (match CK3 river map spec)
    // ─────────────────────────────────────────────────────────────────────────────

    private static readonly MagickColor Blue  = new(0,   0,   180);  // river body
    private static readonly MagickColor Green = new(0,   255, 0);    // source pixel
    private static readonly MagickColor Red   = new(255, 0,   0);    // tributary junction

    // ─────────────────────────────────────────────────────────────────────────────
    // Image helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a white (all-passable) test image and paints the given pixels blue.
    /// Call Dispose() on the returned image when the test is done.
    /// </summary>
    private static MagickImage CreateImage(int width, int height, params (int x, int y)[] blueAt)
    {
        var colored = blueAt.Select(p => (p.x, p.y, Blue)).ToArray();
        return CreateImageWithColors(width, height, colored);
    }

    /// <summary>
    /// Creates a white (all-passable) test image and paints each specified pixel with
    /// the given colour.  Use Blue, Red, or Green to represent river map pixels.
    /// Call Dispose() on the returned image when the test is done.
    /// </summary>
    private static MagickImage CreateImageWithColors(int width, int height,
        params (int x, int y, MagickColor color)[] paintAt)
    {
        var settings = new MagickReadSettings { Width = width, Height = height };
        var image = new MagickImage("xc:white", settings);

        if (paintAt.Length > 0)
        {
            using var pixels = image.GetPixels();
            foreach (var (x, y, color) in paintAt)
            {
                var pixel = pixels.GetPixel(x, y);
                if (pixel != null)
                {
                    pixel.SetChannel(0, color.R);
                    pixel.SetChannel(1, color.G);
                    pixel.SetChannel(2, color.B);
                }
            }
        }

        return image;
    }

    /// <summary>
    /// Builds the IsPassable delegate used by A*.
    /// Matches production code: blue, red, AND green pixels are all impassable.
    /// Start and end points always bypass the colour check.
    /// </summary>
    private static Func<Point, bool> MakeIsPassable(MagickImage image, Point start, Point end) =>
        p =>
        {
            if (p == start || p == end) return true;

            using var pixels = image.GetPixels();
            var pixel = pixels.GetPixel(p.X, p.Y);
            var color = pixel?.ToColor();
            if (color == null) return false;

            // All three CK3 river colours are impassable (matches RiverPathGenerator)
            bool isBlue  = color.R == Blue.R  && color.G == Blue.G  && color.B == Blue.B;
            bool isGreen = color.R == Green.R && color.G == Green.G && color.B == Green.B;
            bool isRed   = color.R == Red.R   && color.G == Red.G   && color.B == Red.B;
            return !(isBlue || isGreen || isRed);
        };

    /// <summary>
    /// Builds the CountAdjacentBlue delegate for Pass 2 filtering.
    /// Returns the number of orthogonally adjacent blue pixels around a point.
    /// </summary>
    private static Func<Point, int> MakeCountAdjacentBlue(MagickImage image) =>
        p =>
        {
            int count = 0;
            Point[] neighbors = { new(p.X, p.Y - 1), new(p.X, p.Y + 1), new(p.X - 1, p.Y), new(p.X + 1, p.Y) };

            using var pixels = image.GetPixels();
            foreach (var n in neighbors)
            {
                if (n.X < 0 || n.X >= (int)image.Width || n.Y < 0 || n.Y >= (int)image.Height) continue;
                var pixel = pixels.GetPixel(n.X, n.Y);
                var color = pixel?.ToColor();
                if (color != null && color.R == Blue.R && color.G == Blue.G && color.B == Blue.B)
                    count++;
            }

            return count;
        };

    /// <summary>Returns true if any orthogonal neighbor of p is blue.</summary>
    private static bool IsAdjacentToBlue(Point p, MagickImage image)
    {
        return MakeCountAdjacentBlue(image)(p) > 0;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Test 1 – Basic path on an open image (no obstacles)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Layout (5×5, all white):
    ///
    ///   S . . . .   (S = start (0,0))
    ///   . . . . .
    ///   . . . . .
    ///   . . . . .
    ///   . . . . G   (G = goal (4,4))
    ///
    /// With no obstacles and no Pass 2, A* must return a path of exactly
    /// Manhattan-distance length (9 points for distance 8).
    /// </summary>
    [Fact]
    public void FindsPath_OnOpenImage_LengthEqualsManhattanDistance()
    {
        var start = new Point(0, 0);
        var goal  = new Point(4, 4);

        using var image = CreateImage(5, 5);  // no blue pixels
        var pathfinder = new AStarPathfinder(5, 5, MakeIsPassable(image, start, goal));

        var path = pathfinder.FindPath(start, goal);

        Assert.NotNull(path);
        Assert.Equal(start, path![0]);
        Assert.Equal(goal,  path[^1]);

        int expectedLength = AStarPathfinder.ManhattanDistance(start, goal) + 1; // inclusive both ends
        Assert.Equal(expectedLength, path.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Test 2 – Pass 1: routes around a blue wall (no Pass 2)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Layout (7×3):
    ///
    ///   S . . . . . .
    ///   B B B B B B .   ← blue wall at y=1, x=0..5; gap at x=6
    ///   G . . . . . .
    ///
    /// A* must route right to the gap at x=6, step down, then back left to reach G.
    /// No blue pixels in the returned path.
    /// </summary>
    [Fact]
    public void Pass1_RoutesAroundBlueWall()
    {
        var start = new Point(0, 0);
        var goal  = new Point(0, 2);

        using var image = CreateImage(7, 3,
            (0, 1), (1, 1), (2, 1), (3, 1), (4, 1), (5, 1));  // wall, gap at x=6

        var pathfinder = new AStarPathfinder(7, 3, MakeIsPassable(image, start, goal));

        var path = pathfinder.FindPath(start, goal);

        Assert.NotNull(path);
        Assert.Equal(start, path![0]);
        Assert.Equal(goal,  path[^1]);

        // No blue pixel should appear in the path
        using var pixels = image.GetPixels();
        foreach (var p in path)
        {
            var pixel = pixels.GetPixel(p.X, p.Y);
            var color = pixel?.ToColor();
            bool isBlue = color != null && color.R == Blue.R && color.G == Blue.G && color.B == Blue.B;
            Assert.False(isBlue, $"Path contains blue pixel at ({p.X},{p.Y})");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Test 3 – Pass 2 forces longer detour around adjacent-to-blue zone
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Layout (8×5, single blue pixel at (2,1)):
    ///
    ///   S . . . . . . G   y=0  (start (0,0), goal (7,0))
    ///   . . B . . . . .   y=1  (blue at (2,1))
    ///   . . . . . . . .   y=2
    ///   . . . . . . . .   y=3
    ///   . . . . . . . .   y=4
    ///
    /// Without Pass 2: direct path goes (0,0)→(1,0)→(2,0)→… length = 8 points.
    ///   (2,0) is adjacent to blue (2,1) — would create a touching river in CK3.
    ///
    /// With Pass 2: (2,0) and (1,1) and (3,1) and (2,2) are all in the exclusion zone
    ///   and must be avoided. Path detours through lower rows, length > 8 points.
    ///   No path pixel (except the goal) may be adjacent to blue.
    /// </summary>
    [Fact]
    public void Pass2_ForcesDetour_PathNotAdjacentToBlue()
    {
        var start = new Point(0, 0);
        var goal  = new Point(7, 0);

        using var image = CreateImage(8, 5, (2, 1));  // single blue pixel

        // ── Without Pass 2 ───────────────────────────────────────────────────
        var pathfinderNoPass2 = new AStarPathfinder(8, 5, MakeIsPassable(image, start, goal));
        var pathNoPass2 = pathfinderNoPass2.FindPath(start, goal);

        Assert.NotNull(pathNoPass2);
        Assert.Equal(start, pathNoPass2![0]);
        Assert.Equal(goal,  pathNoPass2[^1]);
        // Without Pass2 the direct route has length 8 (Manhattan + 1)
        Assert.Equal(8, pathNoPass2.Count);
        // And some pixel in that direct path IS adjacent to blue (proving Pass2 is needed)
        bool anyAdjacentToBlue = pathNoPass2.Any(p => p != goal && IsAdjacentToBlue(p, image));
        Assert.True(anyAdjacentToBlue, "Without Pass2 the direct path should have pixels adjacent to blue");

        // ── With Pass 2 ──────────────────────────────────────────────────────
        var pathfinderWithPass2 = new AStarPathfinder(
            8, 5,
            MakeIsPassable(image, start, goal),
            countAdjacentBlue: MakeCountAdjacentBlue(image));
        var pathWithPass2 = pathfinderWithPass2.FindPath(start, goal);

        Assert.NotNull(pathWithPass2);
        Assert.Equal(start, pathWithPass2![0]);
        Assert.Equal(goal,  pathWithPass2[^1]);

        // Pass2 path must be longer than the direct route
        Assert.True(pathWithPass2.Count > 8,
            $"Pass2 path should detour but has length {pathWithPass2.Count}");

        // No intermediate pixel (excluding start and goal) may be adjacent to blue
        foreach (var p in pathWithPass2)
        {
            if (p == start || p == goal) continue;  // endpoints are exempt
            Assert.False(IsAdjacentToBlue(p, image),
                $"Pass2 path contains pixel ({p.X},{p.Y}) that is adjacent to blue — violation!");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Test 4 – Pass 2 destination bypass: goal can touch blue (tributary connection)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Layout (7×5, single blue pixel at (3,2)):
    ///
    ///   . . . . . . .   y=0
    ///   . . . . . . .   y=1
    ///   S . . B G . .   y=2  (start (0,2), blue (3,2), goal (4,2) — adjacent to blue)
    ///   . . . . . . .   y=3
    ///   . . . . . . .   y=4
    ///
    /// Goal (4,2) is orthogonally adjacent to blue (3,2).  Without the destination
    /// bypass, Pass 2 would exclude it (it has 1 blue neighbour).  With the bypass,
    /// A* must find a route that goes around the exclusion zone and enters the goal
    /// from (4,1) or (4,3) — the two non-blue neighbours of the goal.
    ///
    /// This simulates a tributary connecting to the edge of its parent river.
    /// </summary>
    [Fact]
    public void Pass2_DestinationBypass_AllowsTributaryConnectionToRiverEdge()
    {
        var start = new Point(0, 2);
        var goal  = new Point(4, 2);  // adjacent to blue (3,2)

        using var image = CreateImage(7, 5, (3, 2));  // single blue pixel

        var pathfinder = new AStarPathfinder(
            7, 5,
            MakeIsPassable(image, start, goal),
            countAdjacentBlue: MakeCountAdjacentBlue(image));

        var path = pathfinder.FindPath(start, goal);

        Assert.NotNull(path);
        Assert.Equal(start, path![0]);
        Assert.Equal(goal,  path[^1]);

        // Confirm the goal IS adjacent to blue (so the bypass was actually exercised)
        Assert.True(IsAdjacentToBlue(goal, image),
            "Test setup error: goal should be adjacent to blue to exercise the bypass");

        // All intermediate pixels must NOT be adjacent to blue
        foreach (var p in path)
        {
            if (p == start || p == goal) continue;
            Assert.False(IsAdjacentToBlue(p, image),
                $"Intermediate pixel ({p.X},{p.Y}) is adjacent to blue — Pass2 violation");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Test 5 – No path when goal is completely surrounded by blue
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Layout (5×5):
    ///
    ///   S . . . .
    ///   . . . . .
    ///   . . . B .   ← (3,2) blue
    ///   . . B G B   ← (2,3) blue, G=(3,3) goal (white but unreachable), (4,3) blue
    ///   . . . B .   ← (3,4) blue
    ///
    /// Every orthogonal neighbour of the goal is blue so it can never be reached.
    /// FindPath must return null.
    /// </summary>
    [Fact]
    public void ReturnsNull_WhenGoalIsCompletelyBlocked()
    {
        var start = new Point(0, 0);
        var goal  = new Point(3, 3);  // surrounded on all 4 sides

        using var image = CreateImage(5, 5,
            (3, 2), (2, 3), (4, 3), (3, 4));  // north, west, east, south of goal

        var pathfinder = new AStarPathfinder(5, 5, MakeIsPassable(image, start, goal));
        var path = pathfinder.FindPath(start, goal);

        Assert.Null(path);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Test 6 – Start == Goal returns single-element path
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StartEqualsGoal_ReturnsSingleElementPath()
    {
        var point = new Point(2, 2);
        using var image = CreateImage(5, 5);

        var pathfinder = new AStarPathfinder(5, 5, MakeIsPassable(image, point, point));
        var path = pathfinder.FindPath(point, point);

        Assert.NotNull(path);
        Assert.Single(path!);
        Assert.Equal(point, path[0]);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Tests 7–10 – Red (tributary junction) and Green (source) pixel behaviour
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Layout (7×3, red pixel at (3,1)):
    ///
    ///   S . . . . . .   y=0
    ///   . . . R . . .   y=1  ← red (tributary junction) at (3,1)
    ///   . . . . . . G   y=2  ← goal (6,2)
    ///
    /// Red is a CK3 tributary-junction marker.  It is impassable — A* must route
    /// around it via y=0 rather than through it.
    /// The returned path must not contain the red pixel.
    /// </summary>
    [Fact]
    public void Pass1_RedTributaryJunction_IsImpassable()
    {
        var start = new Point(0, 0);
        var goal  = new Point(6, 2);

        using var image = CreateImageWithColors(7, 3, (3, 1, Red));

        var pathfinder = new AStarPathfinder(7, 3, MakeIsPassable(image, start, goal));
        var path = pathfinder.FindPath(start, goal);

        Assert.NotNull(path);
        Assert.Equal(start, path![0]);
        Assert.Equal(goal,  path[^1]);

        // Red pixel must not appear in the path
        Assert.DoesNotContain(new Point(3, 1), path);
    }

    /// <summary>
    /// Layout (7×3, green pixel at (3,1)):
    ///
    ///   S . . . . . .   y=0
    ///   . . . G . . .   y=1  ← green (river source) at (3,1)
    ///   . . . . . . E   y=2  ← goal (6,2)
    ///
    /// Green is a CK3 river-source marker.  It is impassable — A* must route
    /// around it.  The returned path must not contain the green pixel.
    /// </summary>
    [Fact]
    public void Pass1_GreenSource_IsImpassable()
    {
        var start = new Point(0, 0);
        var goal  = new Point(6, 2);

        using var image = CreateImageWithColors(7, 3, (3, 1, Green));

        var pathfinder = new AStarPathfinder(7, 3, MakeIsPassable(image, start, goal));
        var path = pathfinder.FindPath(start, goal);

        Assert.NotNull(path);
        Assert.Equal(start, path![0]);
        Assert.Equal(goal,  path[^1]);

        Assert.DoesNotContain(new Point(3, 1), path);
    }

    /// <summary>
    /// Layout (7×3, red pixel at (3,1)):
    ///
    ///   S . . . . . .   y=0
    ///   . . . R . . .   y=1  ← red at (3,1)
    ///   . . . . . . G   y=2  ← goal (6,2)
    ///
    /// Pass 2 adjacency exclusion must apply ONLY to blue pixels.
    /// Pixels adjacent to red (e.g. (3,0), (3,2), (2,1), (4,1)) must NOT be
    /// excluded from A* — if they were, a tributary could never path to a red
    /// junction point, which is the whole purpose of the red marker.
    ///
    /// Verified by enabling Pass 2 and checking that a valid path is still found
    /// and that its length is the same as without Pass 2.
    /// </summary>
    [Fact]
    public void Pass2_AdjacentToRed_IsNotExcluded()
    {
        var start = new Point(0, 1);
        var goal  = new Point(6, 1);

        using var image = CreateImageWithColors(7, 3, (3, 1, Red));

        // Without Pass 2
        var pf1 = new AStarPathfinder(7, 3, MakeIsPassable(image, start, goal));
        var pathNoPass2 = pf1.FindPath(start, goal);

        // With Pass 2 (blue-only adjacency check)
        var pf2 = new AStarPathfinder(7, 3, MakeIsPassable(image, start, goal),
            countAdjacentBlue: MakeCountAdjacentBlue(image));
        var pathWithPass2 = pf2.FindPath(start, goal);

        // Both must find a path (red does not block)
        Assert.NotNull(pathNoPass2);
        Assert.NotNull(pathWithPass2);

        // Path lengths must be equal: Pass 2 must not add extra detour around red
        Assert.Equal(pathNoPass2!.Count, pathWithPass2!.Count);
    }

    /// <summary>
    /// Layout (7×3, green pixel at (3,1)):
    ///
    ///   S . . . . . .   y=0
    ///   . . . G . . .   y=1  ← green at (3,1)
    ///   . . . . . . E   y=2  ← goal (6,2)
    ///
    /// Same principle as the red test above.  Pass 2 must not create an exclusion
    /// zone around green source pixels — otherwise a river could never start
    /// adjacent to another river's source.
    /// </summary>
    [Fact]
    public void Pass2_AdjacentToGreen_IsNotExcluded()
    {
        var start = new Point(0, 1);
        var goal  = new Point(6, 1);

        using var image = CreateImageWithColors(7, 3, (3, 1, Green));

        // Without Pass 2
        var pf1 = new AStarPathfinder(7, 3, MakeIsPassable(image, start, goal));
        var pathNoPass2 = pf1.FindPath(start, goal);

        // With Pass 2 (blue-only adjacency check)
        var pf2 = new AStarPathfinder(7, 3, MakeIsPassable(image, start, goal),
            countAdjacentBlue: MakeCountAdjacentBlue(image));
        var pathWithPass2 = pf2.FindPath(start, goal);

        // Both must find a path
        Assert.NotNull(pathNoPass2);
        Assert.NotNull(pathWithPass2);

        // Path lengths must be equal: Pass 2 must not add extra detour around green
        Assert.Equal(pathNoPass2!.Count, pathWithPass2!.Count);
    }
}
