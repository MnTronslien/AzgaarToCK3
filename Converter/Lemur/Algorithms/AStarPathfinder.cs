using System.Drawing;

namespace Converter.Lemur.Algorithms;

/// <summary>
/// Generic A* pathfinding algorithm for finding shortest orthogonal path on a 2D grid.
/// Designed to be reusable for rivers, trade routes, holy site connections, etc.
/// Follows SOLID principles with configurable behavior via delegates.
/// </summary>
public class AStarPathfinder
{
    private readonly int _width;
    private readonly int _height;
    private readonly Func<Point, Point, int> _heuristic;
    private readonly Func<Point, bool> _isPassable;
    private readonly bool _allowDiagonal;
    private readonly Func<Point, int>? _countAdjacentBlue;
    private readonly bool _selfAvoid;
    private readonly IReadOnlySet<Point>? _selfAvoidSeed;
    private readonly bool _exemptGoal;

    /// <summary>
    /// Create a pathfinder for a 2D grid.
    /// </summary>
    /// <param name="width">Grid width in pixels</param>
    /// <param name="height">Grid height in pixels</param>
    /// <param name="isPassable">Function that returns true if a point can be traversed</param>
    /// <param name="heuristic">Distance heuristic (default: Manhattan distance for orthogonal)</param>
    /// <param name="allowDiagonal">Allow diagonal movement (default: false for CK3 rivers)</param>
    /// <param name="countAdjacentBlue">Optional Pass 2 filter: returns the number of blue pixels adjacent to a point. Candidates with count > 0 are discarded (destination always bypasses this check).</param>
    /// <param name="selfAvoid">
    /// When true, a candidate is discarded if it is orthogonally adjacent to an *earlier* pixel of
    /// the path being built (other than the immediate predecessor). The in-progress path is invisible
    /// to <paramref name="countAdjacentBlue"/> (that only sees already-drawn pixels), so without this
    /// a single path can loop against itself at a tight turn and form a 2×2 block — a pixel with 3
    /// orthogonal river neighbours, which CK3 rejects. Straight runs and single L-turns are unaffected.
    /// </param>
    /// <param name="selfAvoidSeed">
    /// Pixels already committed by earlier segments of the same path. The ancestor walk only sees the
    /// current A* call, so when a fresh call (e.g. a fallback/terminal segment) starts at the previous
    /// segment's tail, it would otherwise loop back and touch that tail — a 2×2 across the segment seam.
    /// Seeding them here forbids that adjacency (the start pixel itself is always exempt).
    /// </param>
    public AStarPathfinder(
        int width,
        int height,
        Func<Point, bool> isPassable,
        Func<Point, Point, int>? heuristic = null,
        bool allowDiagonal = false,
        Func<Point, int>? countAdjacentBlue = null,
        bool selfAvoid = false,
        IReadOnlySet<Point>? selfAvoidSeed = null,
        bool exemptGoal = true)
    {
        _width = width;
        _height = height;
        _isPassable = isPassable;
        _allowDiagonal = allowDiagonal;
        _heuristic = heuristic ?? ManhattanDistance;
        _countAdjacentBlue = countAdjacentBlue;
        _selfAvoid = selfAvoid;
        _selfAvoidSeed = selfAvoidSeed;
        _exemptGoal = exemptGoal;
    }

    /// <summary>
    /// Find the shortest orthogonal path from start to goal using A* algorithm.
    /// Returns null if no path exists or iteration limit is exceeded.
    /// </summary>
    /// <param name="start">Starting point</param>
    /// <param name="goal">Goal point</param>
    /// <param name="maxIterations">Maximum nodes to explore (prevents infinite loops)</param>
    /// <returns>Ordered list of points from start to goal, or null if no path found</returns>
    public List<Point>? FindPath(Point start, Point goal, int maxIterations = 10000)
    {
        // Validate inputs
        if (!IsInBounds(start) || !IsInBounds(goal))
            return null;

        if (!_isPassable(start) || !_isPassable(goal))
            return null;

        // Trivial case: start == goal
        if (start == goal)
            return new List<Point> { start };

        // Initialize open and closed sets
        var openSet = new PriorityQueue<Node, int>();
        var closedSet = new HashSet<Point>();
        var openSetLookup = new Dictionary<Point, Node>();

        var startNode = new Node
        {
            Position = start,
            Parent = null,
            G = 0,
            H = _heuristic(start, goal)
        };

        openSet.Enqueue(startNode, startNode.F);
        openSetLookup[start] = startNode;

        int iterations = 0;

        while (openSet.Count > 0 && iterations < maxIterations)
        {
            iterations++;

            // Get node with lowest F score
            var current = openSet.Dequeue();
            openSetLookup.Remove(current.Position);

            // Check if we reached the goal
            if (current.Position == goal)
            {
                return ReconstructPath(current);
            }

            closedSet.Add(current.Position);

            // Self-avoid: collect this node's ancestors (strictly before it) so a candidate touching
            // the in-progress path can be rejected. The whole chain is walked — a river can loop back
            // on itself well past any fixed window (seen at 35px). Skipped when not self-avoiding.
            HashSet<Point>? recentAncestors = null;
            if (_selfAvoid)
            {
                recentAncestors = new HashSet<Point>();
                for (var a = current.Parent; a != null; a = a.Parent)
                    recentAncestors.Add(a.Position);
            }

            // Explore neighbors
            foreach (var neighbor in GetNeighbors(current.Position, goal, recentAncestors))
            {
                // Skip if already evaluated
                if (closedSet.Contains(neighbor))
                    continue;

                // Skip if not passable
                if (!_isPassable(neighbor))
                    continue;

                // Calculate tentative G score (cost from start)
                int tentativeG = current.G + 1; // Each step costs 1

                // Check if this path to neighbor is better than any previous one
                if (openSetLookup.TryGetValue(neighbor, out var existingNode))
                {
                    if (tentativeG < existingNode.G)
                    {
                        // This path is better, update the node
                        existingNode.G = tentativeG;
                        existingNode.Parent = current;
                        // Re-enqueue with new priority (can't update priority in PriorityQueue)
                        openSet.Enqueue(existingNode, existingNode.F);
                    }
                }
                else
                {
                    // New node, add to open set
                    var neighborNode = new Node
                    {
                        Position = neighbor,
                        Parent = current,
                        G = tentativeG,
                        H = _heuristic(neighbor, goal)
                    };

                    openSet.Enqueue(neighborNode, neighborNode.F);
                    openSetLookup[neighbor] = neighborNode;
                }
            }
        }

        // No path found
        return null;
    }

    /// <summary>
    /// Get orthogonally adjacent neighbors (up, down, left, right), with two-pass filtering.
    /// Pass 1: bounds check (goal always included regardless of passability — handled in FindPath).
    /// Pass 2: if countAdjacentBlue is set, discard candidates adjacent to blue pixels unless they are the goal.
    /// If allowDiagonal is true, also includes diagonal neighbors.
    /// </summary>
    private IEnumerable<Point> GetNeighbors(Point point, Point goal, HashSet<Point>? recentAncestors)
    {
        // A candidate is allowed if it is in bounds and clears both adjacency guards.
        // exemptGoal lets the goal touch ANOTHER river (Pass 2) — used only for the deliberate
        // tributary-into-parent connection. It never exempts self-avoidance: a path may not touch
        // its OWN earlier body even at the goal, or the junction marker ends up adjacent to two of
        // its own pixels (a degree-3 red). So Pass 2 respects exemptGoal; self-avoid always applies.
        bool Allowed(Point n)
        {
            if (!IsInBounds(n)) return false;
            bool goalExempt = _exemptGoal && n == goal;
            if (_countAdjacentBlue != null && !goalExempt && _countAdjacentBlue(n) > 0)
                return false;  // Pass 2: would run alongside another river
            return !SelfTouches(n, point, recentAncestors);  // would touch the path's own earlier pixels
        }

        // Orthogonal neighbours (4-connected)
        foreach (var n in new[]
        {
            new Point(point.X, point.Y - 1), new Point(point.X, point.Y + 1),
            new Point(point.X - 1, point.Y), new Point(point.X + 1, point.Y)
        })
            if (Allowed(n)) yield return n;

        // Diagonal neighbours (8-connected) — only if allowed
        if (_allowDiagonal)
            foreach (var n in new[]
            {
                new Point(point.X - 1, point.Y - 1), new Point(point.X + 1, point.Y - 1),
                new Point(point.X - 1, point.Y + 1), new Point(point.X + 1, point.Y + 1)
            })
                if (Allowed(n)) yield return n;
    }

    /// <summary>
    /// True if <paramref name="candidate"/> is orthogonally adjacent to a path pixel it must not
    /// touch — one of the current call's recent ancestors, or a seed pixel from an earlier segment —
    /// other than its immediate predecessor <paramref name="from"/>. That adjacency is exactly what
    /// turns a 1-wide trail into a 2×2 block, whether the touch is within one A* call or across a seam.
    /// </summary>
    private bool SelfTouches(Point candidate, Point from, HashSet<Point>? recentAncestors)
    {
        if (recentAncestors != null && TouchesSet(candidate, from, recentAncestors)) return true;
        if (_selfAvoidSeed != null && TouchesSet(candidate, from, _selfAvoidSeed)) return true;
        return false;
    }

    /// <summary>
    /// True if <paramref name="candidate"/> is orthogonally adjacent to any pixel in
    /// <paramref name="pathPixels"/> other than its immediate predecessor <paramref name="from"/>.
    /// </summary>
    private static bool TouchesSet(Point candidate, Point from, IReadOnlySet<Point> pathPixels)
    {
        var up    = new Point(candidate.X, candidate.Y - 1);
        var down  = new Point(candidate.X, candidate.Y + 1);
        var left  = new Point(candidate.X - 1, candidate.Y);
        var right = new Point(candidate.X + 1, candidate.Y);
        if (up    != from && pathPixels.Contains(up))    return true;
        if (down  != from && pathPixels.Contains(down))  return true;
        if (left  != from && pathPixels.Contains(left))  return true;
        if (right != from && pathPixels.Contains(right)) return true;
        return false;
    }

    /// <summary>
    /// Check if a point is within the grid bounds.
    /// </summary>
    private bool IsInBounds(Point point)
    {
        return point.X >= 0 && point.X < _width &&
               point.Y >= 0 && point.Y < _height;
    }

    /// <summary>
    /// Reconstruct the path from goal back to start by following parent pointers.
    /// </summary>
    private static List<Point> ReconstructPath(Node goalNode)
    {
        var path = new List<Point>();
        var current = goalNode;

        while (current != null)
        {
            path.Add(current.Position);
            current = current.Parent;
        }

        path.Reverse(); // Path was built backwards
        return path;
    }

    /// <summary>
    /// Manhattan distance heuristic (optimal for orthogonal movement).
    /// </summary>
    public static int ManhattanDistance(Point a, Point b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    /// <summary>
    /// Euclidean distance heuristic (better for diagonal movement allowed).
    /// </summary>
    public static int EuclideanDistance(Point a, Point b)
    {
        int dx = a.X - b.X;
        int dy = a.Y - b.Y;
        return (int)Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Represents a node in the A* search.
    /// </summary>
    private class Node
    {
        public Point Position { get; set; }
        public Node? Parent { get; set; }
        public int G { get; set; }  // Cost from start
        public int H { get; set; }  // Estimated cost to goal (heuristic)
        public int F => G + H;      // Total estimated cost
    }
}

/// <summary>
/// Result of a pathfinding operation with additional metadata.
/// </summary>
public class PathfindingResult
{
    public List<Point>? Path { get; set; }
    public bool Success => Path != null && Path.Count > 0;
    public int PathLength => Path?.Count ?? 0;
    public string? FailureReason { get; set; }

    public static PathfindingResult Successful(List<Point> path)
    {
        return new PathfindingResult { Path = path };
    }

    public static PathfindingResult Failed(string reason)
    {
        return new PathfindingResult { FailureReason = reason };
    }
}
