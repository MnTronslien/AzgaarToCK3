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

            var (mergedCount, skippedCount) = MergeControlPoints(rivers, riverGeoJson);

            Logger.Info($"Merged control points for {mergedCount} rivers");
            if (skippedCount > 0)
            {
                Logger.Warning($"Skipped {skippedCount} malformed river feature(s) (missing geometry/properties) " +
                               $"in {Path.GetFileName(riverGeoJsonPath)}");
            }

            if (mergedCount == 0)
            {
                throw new Exception("No rivers with control points found in rivers GeoJSON");
            }
        }

        /// <summary>
        /// Merges LineString control points from the parsed rivers GeoJSON into the matching
        /// <see cref="River"/> entities (matched by id). Returns the number of rivers merged and
        /// the number of malformed features skipped.
        ///
        /// Guards against malformed GeoJSON: an Azgaar rivers export can contain a null feature,
        /// or a feature whose <c>properties</c>/<c>geometry</c> is null. Dereferencing those
        /// (e.g. <c>feature.properties.id</c>) previously threw an unhandled
        /// NullReferenceException that aborted the entire conversion before any file was written,
        /// leaving an empty mod folder (issue #32). Such features are skipped, not fatal.
        /// </summary>
        public static (int merged, int skipped) MergeControlPoints(List<River> rivers, RiverGeoJson riverGeoJson)
        {
            // Create lookup dictionary for fast matching
            var riverDict = rivers.ToDictionary(r => r.Id);

            int mergedCount = 0;
            int skippedCount = 0;
            foreach (var feature in riverGeoJson.features)
            {
                if (feature == null || feature.properties == null || feature.geometry == null)
                {
                    skippedCount++;
                    continue;
                }

                if (feature.geometry.type == "LineString" &&
                    feature.geometry.coordinates != null &&
                    riverDict.TryGetValue(feature.properties.id, out var river))
                {
                    river.ControlPoints = feature.geometry.coordinates.ToList();
                    mergedCount++;
                }
            }

            return (mergedCount, skippedCount);
        }
    }
}
