using System.Text.Json;
using Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class CellDump
{
    public record MapCoords(float LonW, float LonT, float LatS, float LatT);

    public record CellRecord(
        int Id,
        int GeoHeight,
        int Area,
        string Type,
        bool IsRiverCell,
        int[] Neighbors,
        float[][] GeoDataCoordinates);

    public record DumpFile(MapCoords Coords, CellRecord[] Cells);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Write(string path, IReadOnlyDictionary<int, Cell> cells, MapCoords coords)
    {
        var records = cells.Values
            .Select(c => new CellRecord(
                c.Id,
                c.GeoHeight,
                c.Area,
                c.Type.ToString(),
                c.IsRiverCell,
                c.Neighbors,
                c.GeoDataCoordinates))
            .ToArray();

        File.WriteAllText(path, JsonSerializer.Serialize(new DumpFile(coords, records), Options));
    }

    public static (Dictionary<int, Cell> Cells, MapCoords Coords) Read(string path)
    {
        var dump = JsonSerializer.Deserialize<DumpFile>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"Failed to deserialize cell dump: {path}");

        var cells = dump.Cells.ToDictionary(
            r => r.Id,
            r => new Cell
            {
                Id                 = r.Id,
                GeoHeight          = r.GeoHeight,
                Area               = r.Area,
                Type               = Enum.Parse<Cell.FeatureType>(r.Type),
                IsRiverCell        = r.IsRiverCell,
                Neighbors          = r.Neighbors,
                GeoDataCoordinates = r.GeoDataCoordinates,
                Culture            = 0,
                Religion           = 0,
                State              = 0,
                AzProvince         = 0,
            });

        return (cells, dump.Coords);
    }
}
