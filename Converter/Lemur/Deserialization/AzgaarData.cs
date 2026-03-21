using System.Text.Json;
using System.Text.Json.Serialization;

namespace Converter.Lemur.Deserialization
{
    /// <summary>
    /// Clean DTOs for Azgaar JSON structure - no upstream dependencies
    /// These records match the Azgaar data model directly for deserialization
    /// </summary>

    /// <summary>
    /// Represents a burg as defined in the Azgaar Data
    /// </summary>
    public record AzgaarBurg(
        int i,              // Burg ID, always equal to the array index
        string name,        // Burg name
        int cell,           // Burg cell ID. One cell can have only one burg
        float x,            // X axis coordinate, rounded to two decimals
        float y,            // Y axis coordinate, rounded to two decimals
        int culture,        // Burg culture ID
        int state,          // Burg state ID
        int feature,        // Burg feature ID (ID of a landmass)
        float population,   // Burg population in population points
        string type,        // Burg type
        int capital,        // 1 if burg is a capital, 0 if not
        int port,           // If burg is not a port, then 0, otherwise feature ID of the water body
        int citadel,        // 1 if burg has a castle, 0 if not
        int plaza,          // 1 if burg has a marketplace, 0 if not
        int shanty,         // 1 if burg has a shanty town, 0 if not
        int temple,         // 1 if burg has a temple, 0 if not
        int walls,          // 1 if burg has walls, 0 if not
        bool removed        // True if burg is removed
    );

    public record AzgaarPackCell(int i, int area, int biome);

    public record AzgaarProvince(int i, int state, int burg, string name);

    public record AzgaarState(int i, string name, int[] provinces);

    public record AzgaarCulture(int i, string name);

    public record AzgaarReligion(int i, string name);

    public record AzgaarMapCoordinates(
        float latT,  // Total latitude range
        float latN,  // Northern latitude
        float latS,  // Southern latitude
        float lonT,  // Total longitude range
        float lonW,  // Western longitude
        float lonE   // Eastern longitude
    );

    public record AzgaarInfo(
        int width,
        int height,
        string mapName
    );

    public record AzgaarNameBase(string name, string b);

    // Custom converter for provinces array (0th element is number, not object)
    public class AzgaarProvinceJsonConverter : JsonConverter<AzgaarProvince[]>
    {
        public override AzgaarProvince[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var jsonDocument = JsonDocument.ParseValue(ref reader);
            var str = jsonDocument.RootElement.GetRawText();

            string escapedStr = str;
            // Replace 0 with dummy province object
            if (str[1] == '0')
            {
                escapedStr = string.Concat(str.AsSpan(0, 1), "{\"i\":0,\"state\":0,\"burg\":0,\"name\":\"\"}", str.AsSpan(2));
            }

            var provinces = JsonSerializer.Deserialize<AzgaarProvince[]>(escapedStr, options);
            return provinces;
        }

        public override void Write(Utf8JsonWriter writer, AzgaarProvince[] value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }

    // Custom converter for burgs array (0th element is number 0, not object)
    public class AzgaarBurgJsonConverter : JsonConverter<AzgaarBurg[]>
    {
        public override AzgaarBurg[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var jsonDocument = JsonDocument.ParseValue(ref reader);
            var str = jsonDocument.RootElement.GetRawText();

            string escapedStr = str;
            // Replace 0 with dummy burg object
            if (str[1] == '0')
            {
                escapedStr = string.Concat(
                    str.AsSpan(0, 1),
                    "{\"i\":0,\"name\":\"\",\"cell\":0,\"x\":0,\"y\":0,\"culture\":0,\"state\":0,\"feature\":0,\"population\":0,\"type\":\"\",\"capital\":0,\"port\":0,\"citadel\":0,\"plaza\":0,\"shanty\":0,\"temple\":0,\"walls\":0,\"removed\":false}",
                    str.AsSpan(2)
                );
            }

            var burgs = JsonSerializer.Deserialize<AzgaarBurg[]>(escapedStr, options);
            return burgs;
        }

        public override void Write(Utf8JsonWriter writer, AzgaarBurg[] value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Top-level container for Azgaar pack data
    /// </summary>
    public class AzgaarPack
    {
        [JsonConverter(typeof(AzgaarBurgJsonConverter))]
        public AzgaarBurg[] burgs { get; set; } = Array.Empty<AzgaarBurg>();

        public AzgaarPackCell[] cells { get; set; } = Array.Empty<AzgaarPackCell>();

        [JsonConverter(typeof(AzgaarProvinceJsonConverter))]
        public AzgaarProvince[] provinces { get; set; } = Array.Empty<AzgaarProvince>();

        public AzgaarState[] states { get; set; } = Array.Empty<AzgaarState>();

        public AzgaarCulture[] cultures { get; set; } = Array.Empty<AzgaarCulture>();

        public AzgaarReligion[] religions { get; set; } = Array.Empty<AzgaarReligion>();
    }

    /// <summary>
    /// Top-level Azgaar JSON map structure
    /// </summary>
    public record AzgaarJsonMap(
        AzgaarPack pack,
        AzgaarMapCoordinates mapCoordinates,
        AzgaarInfo info,
        AzgaarNameBase[] nameBases
    );

    // ========== GeoJSON DTOs ==========

    public record GeoJsonGeometry(
        string type,
        float[][][] coordinates
    );

    public record GeoJsonProperties(
        int id,
        string type,
        int province,
        int state,
        int height,
        int[] neighbors,
        int culture,
        int religion
    );

    public record GeoJsonFeature(
        GeoJsonGeometry geometry,
        GeoJsonProperties properties
    );

    /// <summary>
    /// Top-level GeoJSON map structure
    /// </summary>
    public record AzgaarGeoMap(
        GeoJsonFeature[] features
    );
}
