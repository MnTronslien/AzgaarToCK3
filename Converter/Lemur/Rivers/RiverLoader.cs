using Converter.Lemur.Deserialization;
using Converter.Lemur.Entities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Converter.Lemur.Rivers
{
    // Source-generated JsonSerializerContext for RiverGeoJson so the rivers
    // .geojson can be deserialized under Native AOT (where reflection-based
    // JsonSerializer is disabled by default). Without this, JsonSerializer
    // throws InvalidOperationException on first use. See bugs/BUG_aot-reflection-json-rivers.md.
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(RiverGeoJson))]
    internal partial class RiverGeoJsonContext : JsonSerializerContext { }

    public static class RiverLoader
    {
        public static List<River> LoadRivers(AzgaarJsonMap jsonMap, float majorThreshold)
        {
            Logger.Section("Loading Rivers from Azgaar Data");

            var azRivers = jsonMap.pack.rivers;
            Logger.Info($"Found {azRivers.Length} rivers in map data");

            // Convert to River entities
            var rivers = azRivers.Select(River.FromAzgaarRiver).ToList();

            if (rivers.Count == 0)
            {
                Logger.Info("No rivers found in map data");
                return rivers;
            }

            // Load rivers GeoJSON for control points (mandatory)
            LoadRiverControlPoints(rivers);

            // Log statistics
            var maxDischarge = rivers.Max(r => r.Discharge);
            var minDischarge = rivers.Min(r => r.Discharge);
            var avgDischarge = rivers.Average(r => r.Discharge);

            Logger.Info($"Discharge range: {minDischarge:F1} - {maxDischarge:F1} (avg: {avgDischarge:F1})");
            Logger.Info($"Total river cells: {rivers.Sum(r => r.CellIds.Count)}");

            // Classify
            var majorCount = rivers.Count(r => r.IsMajor(majorThreshold));
            var minorCount = rivers.Count - majorCount;
            Logger.Info($"Classification: {minorCount} minor rivers, {majorCount} major rivers (threshold: {majorThreshold})");

            // Log control points info
            var riversWithControlPoints = rivers.Count(r => r.ControlPoints != null && r.ControlPoints.Count > 0);
            Logger.Info($"Control points loaded: {riversWithControlPoints} rivers have LineString geometry");
            if (riversWithControlPoints > 0)
            {
                var avgControlPoints = rivers.Where(r => r.ControlPoints != null).Average(r => r.ControlPoints!.Count);
                Logger.Info($"Average control points per river: {avgControlPoints:F1}");
            }

            return rivers;
        }

        /// <summary>
        /// Loads river control points from the mandatory rivers GeoJSON export file.
        /// </summary>
        private static void LoadRiverControlPoints(List<River> rivers)
        {
            var riverGeoJsonPath = Settings.Instance.InputRiversGeojsonPath;
            Logger.Info($"Loading river control points from: {Path.GetFileName(riverGeoJsonPath)}");

            var json = File.ReadAllText(riverGeoJsonPath);
            var riverGeoJson = JsonSerializer.Deserialize(json, RiverGeoJsonContext.Default.RiverGeoJson);

            if (riverGeoJson == null || riverGeoJson.features == null)
            {
                throw new Exception("Failed to parse rivers GeoJSON file");
            }

            // Create lookup dictionary for fast matching
            var riverDict = rivers.ToDictionary(r => r.Id);

            // Merge control points into River objects
            int mergedCount = 0;
            foreach (var feature in riverGeoJson.features)
            {
                if (feature.geometry?.type == "LineString" &&
                    feature.geometry.coordinates != null &&
                    riverDict.TryGetValue(feature.properties.id, out var river))
                {
                    river.ControlPoints = feature.geometry.coordinates.ToList();
                    mergedCount++;
                }
            }

            Logger.Info($"Merged control points for {mergedCount} rivers");

            if (mergedCount == 0)
            {
                throw new Exception("No rivers with control points found in rivers GeoJSON");
            }
        }
    }
}
