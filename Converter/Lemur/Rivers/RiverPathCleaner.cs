using System.Drawing;
using ImageMagick;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Cleans river paths by removing duplicates and other issues.
/// </summary>
public static class RiverPathCleaner
{
    /// <summary>
    /// Remove duplicate consecutive and non-consecutive pixels from a river path.
    /// Preserves the order of the first occurrence of each pixel.
    /// </summary>
    public static List<PointD> RemoveDuplicates(List<PointD> path, string riverName)
    {
        if (path.Count < 2)
            return path;

        var cleanPath = new List<PointD>();
        var seenPixels = new HashSet<Point>();
        int duplicatesRemoved = 0;

        foreach (var point in path)
        {
            var pixelPoint = new Point((int)point.X, (int)point.Y);

            if (seenPixels.Add(pixelPoint))
            {
                // First occurrence - keep it
                cleanPath.Add(point);
            }
            else
            {
                // Duplicate - skip it
                duplicatesRemoved++;
            }
        }

        if (Settings.Instance.Debug && duplicatesRemoved > 0)
        {
            Console.WriteLine($"  Cleaned {riverName}: removed {duplicatesRemoved} duplicate pixels ({path.Count} → {cleanPath.Count} points)");
        }

        return cleanPath;
    }

    /// <summary>
    /// Remove consecutive duplicate pixels (same pixel repeated in sequence).
    /// This is a lighter version that only removes back-to-back duplicates.
    /// </summary>
    public static List<PointD> RemoveConsecutiveDuplicates(List<PointD> path, string riverName)
    {
        if (path.Count < 2)
            return path;

        var cleanPath = new List<PointD> { path[0] };
        int duplicatesRemoved = 0;

        for (int i = 1; i < path.Count; i++)
        {
            var current = new Point((int)path[i].X, (int)path[i].Y);
            var previous = new Point((int)path[i - 1].X, (int)path[i - 1].Y);

            if (current != previous)
            {
                cleanPath.Add(path[i]);
            }
            else
            {
                duplicatesRemoved++;
            }
        }

        if (Settings.Instance.Debug && duplicatesRemoved > 0)
        {
            Console.WriteLine($"  Cleaned {riverName}: removed {duplicatesRemoved} consecutive duplicates ({path.Count} → {cleanPath.Count} points)");
        }

        return cleanPath;
    }

    /// <summary>
    /// Comprehensive cleaning: removes all duplicates and validates the result.
    /// </summary>
    public static List<PointD> CleanPath(List<PointD> path, string riverName)
    {
        if (path.Count < 2)
            return path;

        int originalCount = path.Count;

        // Remove all duplicates (not just consecutive)
        path = RemoveDuplicates(path, riverName);

        // Validate we still have enough points
        if (path.Count < 2)
        {
            Console.WriteLine($"  WARNING: {riverName} cleaning removed too many points ({originalCount} → {path.Count})");
            return new List<PointD>(); // Empty path will be skipped
        }

        return path;
    }
}

/// <summary>
/// Statistics about path cleaning operations.
/// </summary>
public class PathCleaningStats
{
    public int TotalRivers { get; set; }
    public int RiversWithDuplicates { get; set; }
    public int TotalDuplicatesRemoved { get; set; }
    public int TotalPointsBefore { get; set; }
    public int TotalPointsAfter { get; set; }

    public double AverageDuplicatesPerRiver =>
        RiversWithDuplicates > 0 ? (double)TotalDuplicatesRemoved / RiversWithDuplicates : 0;

    public double PercentageReduction =>
        TotalPointsBefore > 0 ? (double)(TotalPointsBefore - TotalPointsAfter) / TotalPointsBefore * 100 : 0;

    public override string ToString()
    {
        return $"Cleaned {TotalRivers} rivers: {RiversWithDuplicates} had duplicates, " +
               $"removed {TotalDuplicatesRemoved} total ({PercentageReduction:F1}% reduction)";
    }
}
