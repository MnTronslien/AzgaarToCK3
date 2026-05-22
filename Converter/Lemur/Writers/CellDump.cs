using System.Text.Json;
using System.Text.Json.Serialization;
using Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

// Source-generated JsonSerializerContext so --dump-cells works under Native AOT.
// Must be namespace-level (not nested) — JsonSourceGenerator only generates
// for top-level partial classes. See bugs/BUG_aot-reflection-json-rivers.md.
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CellDump.DumpFile))]
internal partial class CellDumpContext : JsonSerializerContext { }

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
        float[][] GeoDataCoordinates,
        int Biome = 0);  // optional for back-compat with old dumps; defaults to 0 (no biome)

    public record DumpFile(MapCoords Coords, CellRecord[] Cells);

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
                c.GeoDataCoordinates,
                c.Biome))
            .ToArray();

        File.WriteAllText(path, JsonSerializer.Serialize(new DumpFile(coords, records), CellDumpContext.Default.DumpFile));
    }

    public static (Dictionary<int, Cell> Cells, MapCoords Coords) Read(string path)
    {
        var dump = JsonSerializer.Deserialize(File.ReadAllText(path), CellDumpContext.Default.DumpFile)
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
                Biome              = r.Biome,
                Culture            = 0,
                Religion           = 0,
                State              = 0,
                AzProvince         = 0,
            });

        return (cells, dump.Coords);
    }
}
