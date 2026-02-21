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

    /// <summary>
    /// Create a pathfinder for a 2D grid.
    /// </summary>
    /// <param name="width">Grid width in pixels</param>
    /// <param name="height">Grid height in pixels</param>
    /// <param name="isPassable">Function that returns true if a point can be traversed</param>
    /// <param name="heuristic">Distance heuristic (default: Manhattan distance for orthogonal)</param>
    /// <param name="allowDiagonal">Allow diagonal movement (default: false for CK3 rivers)</param>
    /// <param name="countAdjacentBlue">Optional Pass 2 filter: returns the number of blue pixels adjacent to a point. Candidates with count > 0 are discarded (destination always bypasses this check).</param>
    public AStarPathfinder(
        int width,
        int height,
        Func<Point, bool> isPassable,
        Func<Point, Point, int>? heuristic = null,
        bool allowDiagonal = false,
        Func<Point, int>? countAdjacentBlue = null)
    {
        _width = width;
        _height = height;
        _isPassable = isPassable;
        _allowDiagonal = allowDiagonal;
        _heuristic = heuristic ?? ManhattanDistance;
        _countAdjacentBlue = countAdjacentBlue;
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

            // Explore neighbors
            foreach (var neighbor in GetNeighbors(current.Position, goal))
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
    private IEnumerable<Point> GetNeighbors(Point point, Point goal)
    {
        // Orthogonal neighbors (4-connected)
        var orthogonal = new[]
        {
            new Point(point.X, point.Y - 1), // Up
            new Point(point.X, point.Y + 1), // Down
            new Point(point.X - 1, point.Y), // Left
            new Point(point.X + 1, point.Y)  // Right
        };

        foreach (var neighbor in orthogonal)
        {
            if (!IsInBounds(neighbor)) continue;

            // Pass 2: adjacency-to-blue check (destination always bypasses this)
            if (_countAdjacentBlue != null && neighbor != goal)
            {
                if (_countAdjacentBlue(neighbor) > 0)
                    continue;  // Would create touching rivers — skip
            }

            yield return neighbor;
        }

        // Diagonal neighbors (8-connected) - only if allowed
        if (_allowDiagonal)
        {
            var diagonal = new[]
            {
                new Point(point.X - 1, point.Y - 1), // Top-left
                new Point(point.X + 1, point.Y - 1), // Top-right
                new Point(point.X - 1, point.Y + 1), // Bottom-left
                new Point(point.X + 1, point.Y + 1)  // Bottom-right
            };

            foreach (var neighbor in diagonal)
            {
                if (!IsInBounds(neighbor)) continue;

                // Pass 2: adjacency-to-blue check (destination always bypasses this)
                if (_countAdjacentBlue != null && neighbor != goal)
                {
                    if (_countAdjacentBlue(neighbor) > 0)
                        continue;
                }

                yield return neighbor;
            }
        }
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
