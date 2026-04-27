using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Converter.Lemur;
using Converter.Lemur.Entities;

namespace Converter.Lemur.Deserialization
{
    // Source Generation contexts for AOT compilation - must be at namespace level
    [JsonSerializable(typeof(AzgaarGeoMap))]
    internal partial class AzgaarGeoMapContext : JsonSerializerContext { }

    [JsonSerializable(typeof(AzgaarJsonMap))]
    internal partial class AzgaarJsonMapContext : JsonSerializerContext { }

    /// <summary>
    /// Loads Azgaar data directly into Lemur entities without upstream type dependencies
    /// </summary>
    public static class AzgaarLoader
    {

        /// <summary>
        /// Load Azgaar GeoJSON file
        /// </summary>
        public static async Task<AzgaarGeoMap> LoadGeoJsonAsync(string path)
        {
            using var _ = OperationTimer.Start("Loading GeoJSON");
            try
            {
                var file = await File.ReadAllTextAsync(path);
                var geoMap = JsonSerializer.Deserialize(file, AzgaarGeoMapContext.Default.AzgaarGeoMap);
                if (geoMap == null)
                {
                    throw new Exception($"Failed to deserialize GeoJSON from {path}");
                }
                return geoMap;
            }
            catch (Exception e)
            {
                Logger.Info($"Error loading GeoJSON from {path}: {e.Message}");
                Debugger.Break();
                throw;
            }
        }

        /// <summary>
        /// Load Azgaar JSON file
        /// </summary>
        public static async Task<AzgaarJsonMap> LoadJsonAsync(string path)
        {
            using var _ = OperationTimer.Start("Loading JSON");
            try
            {
                var file = await File.ReadAllTextAsync(path);
                var jsonMap = JsonSerializer.Deserialize(file, AzgaarJsonMapContext.Default.AzgaarJsonMap);
                if (jsonMap == null)
                {
                    throw new Exception($"Failed to deserialize JSON from {path}");
                }
                return jsonMap;
            }
            catch (Exception e)
            {
                Logger.Info($"Error loading JSON from {path}: {e.Message}");
                Debugger.Break();
                throw;
            }
        }

        /// <summary>
        /// Build Lemur Cell dictionary from Azgaar GeoJSON and JSON data
        /// Merges geometry from GeoJSON with metadata from JSON
        /// </summary>
        public static Dictionary<int, Entities.Cell> BuildCells(
            AzgaarGeoMap geoMap,
            AzgaarJsonMap jsonMap)
        {
            using var _ = OperationTimer.Start("Building cells");
            Dictionary<int, Entities.Cell> cells = new();

            var cellData = geoMap.features; // Each feature represents a single cell

            foreach (var feature in cellData)
            {
                // Parse the feature type
                if (!Enum.TryParse(feature.properties.type, true, out Entities.Cell.FeatureType featureType))
                {
                    throw new Exception($"Unrecognized feature type: {feature.properties.type}");
                }

                // Get biome and area from JSON pack.cells (if available)
                int biome = 0;
                int area = 0;
                int geoHeight = 0;
                if (feature.properties.id < jsonMap.pack.cells.Length)
                {
                    var packCell = jsonMap.pack.cells[feature.properties.id];
                    biome = packCell.biome;
                    area = packCell.area;
                    geoHeight = packCell.h;
                }

                var cell = new Entities.Cell()
                {
                    Id = feature.properties.id,
                    GeoHeight = geoHeight,
                    Culture = feature.properties.culture,
                    Religion = feature.properties.religion,
                    State = feature.properties.state,
                    AzProvince = feature.properties.province,
                    Neighbors = feature.properties.neighbors,
                    Type = featureType,
                    GeoDataCoordinates = feature.geometry.coordinates[0],
                    Biome = biome,
                    Area = area
                };

                cells.Add(cell.Id, cell);
            }

            Logger.Info($"Built {cells.Count} cells from Azgaar data");
            return cells;
        }

        /// <summary>
        /// Build Lemur Burg dictionary from Azgaar JSON data.
        /// Creates clean Lemur.Burg objects without wrapping upstream types.
        ///
        /// DATA NORMALIZATION:
        /// Azgaar uses 1-indexed arrays throughout its data model. Index 0 is always a
        /// sentinel/dummy — never a real burg. We skip it here; the resulting dictionary
        /// contains only real burgs (ids 1..N), keyed by their Azgaar burg id.
        ///
        /// If burg data ever looks wrong (missing burgs, bad cell links, unexpected ids),
        /// check the source Azgaar JSON first — this is more likely a data issue than
        /// a logic issue.
        /// </summary>
        public static Dictionary<int, Entities.Burg> BuildBurgs(
            AzgaarJsonMap jsonMap,
            Dictionary<int, Entities.Cell> cells)
        {
            using var _ = OperationTimer.Start("Building burgs");
            // Skip index 0: Azgaar sentinel, never a real burg — see DATA NORMALIZATION note above
            var burgs = jsonMap.pack.burgs
                .Skip(1)
                .Select(azBurg => new Entities.Burg(
                    i: azBurg.i,
                    name: azBurg.name,
                    cell_id: azBurg.cell,
                    x: azBurg.x,
                    y: azBurg.y,
                    culture: azBurg.culture,
                    state: azBurg.state,
                    feature: azBurg.feature,
                    population: azBurg.population,
                    type: azBurg.type,
                    capital: azBurg.capital == 1,
                    port: azBurg.port == 1,
                    citadel: azBurg.citadel == 1,
                    plaza: azBurg.plaza == 1,
                    shanty: azBurg.shanty == 1,
                    temple: azBurg.temple == 1,
                    walls: azBurg.walls == 1,
                    removed: azBurg.removed
                ))
                .ToDictionary(burg => burg.id);
            // No dummy re-insertion — map.Burgs contains only real burgs (ids 1..N)

            Logger.Info($"Built {burgs.Count} burgs from Azgaar data");
            return burgs;
        }
    }
}
