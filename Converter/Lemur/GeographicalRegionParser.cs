namespace Converter.Lemur;

/// <summary>
/// Parses Paradox geographical_regions files into a flat list of <see cref="GeographicalRegion"/>
/// objects containing only their names (content is discarded — we generate stubs).
/// </summary>
public static class GeographicalRegionParser
{
    /// <summary>
    /// Reads all *.txt files in <paramref name="filePaths"/> and returns a flat list of
    /// name-only <see cref="GeographicalRegion"/> objects in file order.
    /// </summary>
    public static List<GeographicalRegion> ParseFiles(IEnumerable<string> filePaths)
    {
        var regions = new List<GeographicalRegion>();

        foreach (var filePath in filePaths)
        {
            int depth = 0;
            GeographicalRegion? current = null;

            foreach (var rawLine in File.ReadLines(filePath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                // Strip inline comment
                var commentIdx = line.IndexOf('#');
                if (commentIdx >= 0)
                    line = line[..commentIdx].Trim();

                // At depth 0, detect a new region block:  identifier = {
                if (depth == 0 && line.Contains('{'))
                {
                    var eqIdx = line.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        var name = line[..eqIdx].Trim();
                        if (IsValidIdentifier(name))
                            current = new GeographicalRegion { Name = name };
                        else
                            Console.WriteLine($"Warning: GeographicalRegionParser skipped unrecognised identifier '{name}' in {Path.GetFileName(filePath)}");
                    }
                }

                // Count brace depth
                foreach (var ch in line)
                {
                    if (ch == '{') depth++;
                    else if (ch == '}') depth--;
                }

                // Region block closed
                if (depth == 0 && current != null)
                {
                    regions.Add(current);
                    current = null;
                }
            }
        }

        return regions;
    }

    private static bool IsValidIdentifier(string s) =>
        s.Length > 0 &&
        (char.IsLetter(s[0]) || s[0] == '_') &&
        s.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '&');
}
