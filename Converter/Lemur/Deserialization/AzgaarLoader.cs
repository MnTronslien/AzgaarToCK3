using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
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
                Console.WriteLine($"Error loading GeoJSON from {path}: {e.Message}");
                Debugger.Break();
                throw;
            }
        }

        /// <summary>
        /// Load Azgaar JSON file
        /// </summary>
        public static async Task<AzgaarJsonMap> LoadJsonAsync(string path)
        {
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
                Console.WriteLine($"Error loading JSON from {path}: {e.Message}");
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
            Dictionary<int, Entities.Cell> cells = new();

            var cellData = geoMap.features; // Each feature represents a single cell

            foreach (var feature in cellData)
            {
                // Parse the feature type
                if (!Enum.TryParse(feature.properties.type, true, out Entities.Cell.FeatureType featureType))
                {
                    throw new Exception($"Unrecognized feature type: {feature.properties.type}");
                }

                // Get biome from JSON pack.cells (if available)
                int biome = 0;
                if (feature.properties.id < jsonMap.pack.cells.Length)
                {
                    biome = jsonMap.pack.cells[feature.properties.id].biome;
                }

                var cell = new Entities.Cell()
                {
                    Id = feature.properties.id,
                    Height = feature.properties.height,
                    Culture = feature.properties.culture,
                    Religion = feature.properties.religion,
                    State = feature.properties.state,
                    AzProvince = feature.properties.province,
                    Neighbors = feature.properties.neighbors,
                    Type = featureType,
                    GeoDataCoordinates = feature.geometry.coordinates[0],
                    Biome = biome
                };

                cells.Add(cell.Id, cell);
            }

            Console.WriteLine($"Built {cells.Count} cells from Azgaar data");
            return cells;
        }

        /// <summary>
        /// Build Lemur Burg dictionary from Azgaar JSON data
        /// Creates clean Lemur.Burg objects without wrapping upstream types
        /// </summary>
        public static Dictionary<int, Entities.Burg> BuildBurgs(
            AzgaarJsonMap jsonMap,
            Dictionary<int, Entities.Cell> cells)
        {
            // Skip the 0th entry (always empty in Azgaar data model)
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

            // Add the 0th burg (dummy entry) if needed for compatibility
            if (jsonMap.pack.burgs.Length > 0)
            {
                var dummyBurg = jsonMap.pack.burgs[0];
                burgs[0] = new Entities.Burg(
                    i: dummyBurg.i,
                    name: dummyBurg.name,
                    cell_id: dummyBurg.cell,
                    x: dummyBurg.x,
                    y: dummyBurg.y,
                    culture: dummyBurg.culture,
                    state: dummyBurg.state,
                    feature: dummyBurg.feature,
                    population: dummyBurg.population,
                    type: dummyBurg.type,
                    capital: dummyBurg.capital == 1,
                    port: dummyBurg.port == 1,
                    citadel: dummyBurg.citadel == 1,
                    plaza: dummyBurg.plaza == 1,
                    shanty: dummyBurg.shanty == 1,
                    temple: dummyBurg.temple == 1,
                    walls: dummyBurg.walls == 1,
                    removed: dummyBurg.removed
                );
            }

            Console.WriteLine($"Built {burgs.Count} burgs from Azgaar data");
            return burgs;
        }
    }
}
