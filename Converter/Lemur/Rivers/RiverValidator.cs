using System.Drawing;
using ImageMagick;

namespace Converter.Lemur.Rivers;

/// <summary>
/// Validates rivers against CK3's strict topological rules.
/// Works in local coordinate space for performance.
/// </summary>
public static class RiverValidator
{
    /// <summary>
    /// Count how many surrounding pixels (8-connected) match the given color.
    /// </summary>
    public static int CountSurroundingColor(MagickImage image, int x, int y, MagickColor color)
    {
        int count = 0;
        var width = image.Width;
        var height = image.Height;

        using var pixels = image.GetPixels();

        // Check all 8 neighbors
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                // Skip center pixel
                if (dx == 0 && dy == 0) continue;

                int nx = x + dx;
                int ny = y + dy;

                // Check bounds
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    continue;

                var pixel = pixels.GetPixel(nx, ny);
                if (pixel != null && ColorsMatch(pixel.ToColor()!, color))
                    count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Count how many orthogonally adjacent pixels (4-connected) match the given color.
    /// CK3 rivers only move orthogonally, so this is the relevant neighbor count.
    /// </summary>
    public static int CountOrthogonalNeighbors(MagickImage image, int x, int y, MagickColor color)
    {
        int count = 0;
        var width = image.Width;
        var height = image.Height;

        using var pixels = image.GetPixels();

        // Check 4 orthogonal neighbors: up, down, left, right
        int[][] offsets = { new[] { 0, -1 }, new[] { 0, 1 }, new[] { -1, 0 }, new[] { 1, 0 } };

        foreach (var offset in offsets)
        {
            int nx = x + offset[0];
            int ny = y + offset[1];

            // Check bounds
            if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                continue;

            var pixel = pixels.GetPixel(nx, ny);
            if (pixel != null && ColorsMatch(pixel.ToColor()!, color))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Check if two points are orthogonally adjacent (distance 1, only x OR y changes).
    /// </summary>
    public static bool IsOrthogonal(PointD a, PointD b)
    {
        int dx = Math.Abs((int)a.X - (int)b.X);
        int dy = Math.Abs((int)a.Y - (int)b.Y);

        // Orthogonal means: exactly one coordinate changes by exactly 1
        return (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
    }

    /// <summary>
    /// Calculate Manhattan distance between two points.
    /// </summary>
    public static int ManhattanDistance(PointD a, PointD b)
    {
        return Math.Abs((int)a.X - (int)b.X) + Math.Abs((int)a.Y - (int)b.Y);
    }

    /// <summary>
    /// Check if a jump is diagonal (both x and y change).
    /// </summary>
    public static bool IsDiagonal(PointD a, PointD b)
    {
        int dx = Math.Abs((int)a.X - (int)b.X);
        int dy = Math.Abs((int)a.Y - (int)b.Y);

        return dx > 0 && dy > 0;
    }

    /// <summary>
    /// Validate a river against CK3 topology rules.
    /// Works in local coordinate space (bounding box of river).
    /// </summary>
    public static RiverValidation ValidateRiver(MagickImage fullImage, List<PointD> riverPath, string riverName)
    {
        var validation = new RiverValidation
        {
            RiverName = riverName,
            TotalPixels = riverPath.Count
        };

        if (riverPath.Count < 2)
            return validation; // Too short to validate

        // Phase 1: Check for duplicate pixels
        var uniquePixels = new HashSet<Point>();
        foreach (var point in riverPath)
        {
            var p = new Point((int)point.X, (int)point.Y);
            if (!uniquePixels.Add(p))
            {
                validation.DuplicatePixels.Add(p);
            }
        }

        // Phase 2: Check gaps and orthogonality between consecutive pixels
        var distances = new List<int>();
        for (int i = 0; i < riverPath.Count - 1; i++)
        {
            var current = riverPath[i];
            var next = riverPath[i + 1];

            int distance = ManhattanDistance(current, next);
            distances.Add(distance);

            // Check for non-orthogonal jumps (diagonals)
            if (distance > 0 && IsDiagonal(current, next))
            {
                validation.NonOrthogonalJumps.Add(new GapViolation
                {
                    From = new Point((int)current.X, (int)current.Y),
                    To = new Point((int)next.X, (int)next.Y),
                    Distance = distance,
                    IsDiagonal = true
                });
            }

            // Check for large gaps (distance > 1)
            if (distance > 1)
            {
                validation.LargeGaps.Add(new GapViolation
                {
                    From = new Point((int)current.X, (int)current.Y),
                    To = new Point((int)next.X, (int)next.Y),
                    Distance = distance,
                    IsDiagonal = IsDiagonal(current, next)
                });
            }
        }

        validation.MaxGapDistance = distances.Any() ? distances.Max() : 0;
        validation.AverageGapDistance = distances.Any() ? distances.Average() : 0;

        // Phase 3: Extract local region for neighbor checking
        // (Skip for now if river is too large to avoid performance issues)
        var minX = (int)riverPath.Min(p => p.X);
        var maxX = (int)riverPath.Max(p => p.X);
        var minY = (int)riverPath.Min(p => p.Y);
        var maxY = (int)riverPath.Max(p => p.Y);

        int regionWidth = maxX - minX + 1;
        int regionHeight = maxY - minY + 1;

        // Only check neighbors if region is reasonable size (< 1000x1000)
        if (regionWidth < 1000 && regionHeight < 1000)
        {
            var blueColor = new MagickColor(0, 0, 180);

            // Check first pixel (endpoint - should have 1 neighbor)
            var firstPoint = riverPath[0];
            int firstNeighbors = CountOrthogonalNeighbors(fullImage, (int)firstPoint.X, (int)firstPoint.Y, blueColor);
            if (firstNeighbors != 1)
            {
                validation.EndpointViolations.Add(new PixelViolation
                {
                    Pixel = new Point((int)firstPoint.X, (int)firstPoint.Y),
                    ActualNeighbors = firstNeighbors,
                    ExpectedNeighbors = 1
                });
            }

            // Check last pixel (endpoint - should have 1 neighbor)
            var lastPoint = riverPath[^1];
            int lastNeighbors = CountOrthogonalNeighbors(fullImage, (int)lastPoint.X, (int)lastPoint.Y, blueColor);
            if (lastNeighbors != 1)
            {
                validation.EndpointViolations.Add(new PixelViolation
                {
                    Pixel = new Point((int)lastPoint.X, (int)lastPoint.Y),
                    ActualNeighbors = lastNeighbors,
                    ExpectedNeighbors = 1
                });
            }

            // Check interior pixels (should have exactly 2 neighbors)
            // Only check a sample to avoid performance issues
            int sampleInterval = Math.Max(1, riverPath.Count / 100); // Check max 100 pixels
            for (int i = 1; i < riverPath.Count - 1; i += sampleInterval)
            {
                var point = riverPath[i];
                int neighbors = CountOrthogonalNeighbors(fullImage, (int)point.X, (int)point.Y, blueColor);
                if (neighbors != 2)
                {
                    validation.InteriorViolations.Add(new PixelViolation
                    {
                        Pixel = new Point((int)point.X, (int)point.Y),
                        ActualNeighbors = neighbors,
                        ExpectedNeighbors = 2
                    });
                }
            }
        }

        return validation;
    }

    /// <summary>
    /// Save a cropped local view of the river with violations highlighted.
    /// </summary>
    public static void SaveLocalRiverImage(
        MagickImage fullImage,
        List<PointD> riverPath,
        string riverName,
        RiverValidation validation,
        string debugReason = "")
    {
        if (riverPath.Count < 2) return;

        // Calculate bounding box
        var minX = (int)riverPath.Min(p => p.X);
        var maxX = (int)riverPath.Max(p => p.X);
        var minY = (int)riverPath.Min(p => p.Y);
        var maxY = (int)riverPath.Max(p => p.Y);

        // Add padding
        var padding = 20;
        var cropX = Math.Max(0, minX - padding);
        var cropY = Math.Max(0, minY - padding);
        var cropWidth = Math.Min((int)fullImage.Width - cropX, maxX - minX + 2 * padding);
        var cropHeight = Math.Min((int)fullImage.Height - cropY, maxY - minY + 2 * padding);

        // Clone and crop
        using var localImage = fullImage.Clone();
        localImage.Crop(new MagickGeometry(cropX, cropY, cropWidth, cropHeight));
        localImage.RePage(); // Reset page offset

        // Draw violations in local coordinate space
        var drawables = new Drawables();

        // Highlight interior violations (red circles)
        foreach (var violation in validation.InteriorViolations)
        {
            var localX = violation.Pixel.X - cropX;
            var localY = violation.Pixel.Y - cropY;
            drawables
                .StrokeColor(MagickColors.Red)
                .StrokeWidth(2)
                .FillOpacity(new Percentage(0))
                .Circle(localX, localY, localX + 5, localY);
        }

        // Highlight endpoint violations (orange circles)
        foreach (var violation in validation.EndpointViolations)
        {
            var localX = violation.Pixel.X - cropX;
            var localY = violation.Pixel.Y - cropY;
            drawables
                .StrokeColor(MagickColors.Orange)
                .StrokeWidth(2)
                .FillOpacity(new Percentage(0))
                .Circle(localX, localY, localX + 5, localY);
        }

        // Highlight large gaps (yellow lines)
        foreach (var gap in validation.LargeGaps.Take(20)) // Limit to avoid clutter
        {
            var localFromX = gap.From.X - cropX;
            var localFromY = gap.From.Y - cropY;
            var localToX = gap.To.X - cropX;
            var localToY = gap.To.Y - cropY;

            drawables
                .StrokeColor(MagickColors.Yellow)
                .StrokeWidth(1)
                .Line(localFromX, localFromY, localToX, localToY);
        }

        localImage.Draw(drawables);

        // Save with descriptive name
        var sanitizedName = string.Join("_", riverName.Split(Path.GetInvalidFileNameChars()));
        var suffix = string.IsNullOrEmpty(debugReason) ? "" : $"_{debugReason}";
        var filename = $"river_{sanitizedName}_local{suffix}.png";

        var debugPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AzgaarToCK3",
            "debug",
            GetDebugFolderName(),
            "rivers_local",
            filename);

        Directory.CreateDirectory(Path.GetDirectoryName(debugPath)!);
        localImage.Write(debugPath);
        ImageUtility.RegisterGeneratedImage(debugPath);

        Console.WriteLine($"    Local view: {filename}");
    }

    private static string GetDebugFolderName()
    {
        var mapName = Path.GetFileNameWithoutExtension(Settings.Instance.InputJsonPath);
        var timestamp = DateTime.Now.ToString("yyyy.MM.dd_HH.mm");
        return $"{mapName}_{timestamp}";
    }

    private static bool ColorsMatch(IMagickColor<byte> a, IMagickColor<byte> b)
    {
        return a.R == b.R && a.G == b.G && a.B == b.B;
    }
}
