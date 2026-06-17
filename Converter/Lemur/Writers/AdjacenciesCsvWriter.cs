using Converter.Lemur.Entities;
using Converter.Lemur.Straits;

namespace Converter.Lemur.Writers;

/// <summary>
/// Writes map_data/adjacencies.csv. Each <see cref="Strait"/> in <c>map.Straits</c> becomes one
/// CK3 sea-crossing row (Type=sea): From/To land provinces joined Through a sea province, with
/// pixel-space start/stop points for the drawn line. See PLAN_straits.md.
/// </summary>
public static class AdjacenciesCsvWriter
{
    // Header describes the format; the terminator line is mandatory — omitting it causes an infinite loading screen.
    private const string Header = "From;To;Type;Through;start_x;start_y;stop_x;stop_y;Comment";
    private const string Terminator = "-1;-1;;-1;-1;-1;-1;-1;";

    public static async Task Write(Map map, string outputDirectory)
    {
        var path = Helper.GetPath(outputDirectory, "map_data", "adjacencies.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // O(1) province → 1-based CK3 province number (same scheme as DefaultMapWriter's indices).
        var provinceNumber = new Dictionary<IProvince, int>(map.AllProvinces!.Count);
        for (int i = 0; i < map.AllProvinces.Count; i++) provinceNumber[map.AllProvinces[i]] = i + 1;

        var lines = new List<string> { Header };
        int skipped = 0;
        foreach (var s in map.Straits)
        {
            if (s.FromCell.Province is not Barony from || !provinceNumber.TryGetValue(from, out int fromId) ||
                s.ToCell.Province is not Barony to || !provinceNumber.TryGetValue(to, out int toId) ||
                s.ThroughCell.Province is not SeaZone through || !provinceNumber.TryGetValue(through, out int throughId))
            {
                skipped++;
                continue;
            }

            var start = Helper.GeoToImage(StraitGenerator.CentroidGeo(s.FromCell), map);
            var stop = Helper.GeoToImage(StraitGenerator.CentroidGeo(s.ToCell), map);
            lines.Add($"{fromId};{toId};sea;{throughId};" +
                      $"{(int)start.X};{(int)start.Y};{(int)stop.X};{(int)stop.Y};" +
                      $"{from.Name}-{to.Name}");
        }
        lines.Add(Terminator);

        await File.WriteAllLinesAsync(path, lines, Helper.Utf8Bom);
        if (skipped > 0)
            Logger.Warning($"adjacencies.csv: skipped {skipped} strait(s) with unresolved province numbers.");
        Logger.Info($"Wrote adjacencies.csv ({lines.Count - 2} sea crossing(s)).");
    }
}
