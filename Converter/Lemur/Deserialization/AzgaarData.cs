using System.Text.Json;
using System.Text.Json.Serialization;

namespace Converter.Lemur.Deserialization
{
    /// <summary>
    /// Reads a JSON value that may be either a boolean (true/false) or an integer (0/1)
    /// and converts it to int. Azgaar exports changed some fields from 0/1 to true/false
    /// between versions.
    /// </summary>
    public class BoolOrIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.True) return 1;
            if (reader.TokenType == JsonTokenType.False) return 0;
            return reader.GetInt32();
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }

    /// <summary>
    /// Reads an int array where elements may be null (treated as 0).
    /// Azgaar newer exports write origins as [null] for root religions instead of [] or [0].
    /// </summary>
    public class NullableIntArrayConverter : JsonConverter<int[]?>
    {
        public override int[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.StartArray) return null;

            var list = new List<int>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.Null)
                    list.Add(0);
                else
                    list.Add(reader.GetInt32());
            }
            return list.ToArray();
        }

        public override void Write(Utf8JsonWriter writer, int[]? value, JsonSerializerOptions options)
        {
            if (value == null) { writer.WriteNullValue(); return; }
            writer.WriteStartArray();
            foreach (var v in value) writer.WriteNumberValue(v);
            writer.WriteEndArray();
        }
    }

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
        [property: JsonConverter(typeof(BoolOrIntConverter))]
        int capital,        // 1 if burg is a capital, 0 if not (newer Azgaar exports use true/false)
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

    public record AzgaarCulture(
        int i,
        string name,
        string color = "#808080",
        [property: JsonPropertyName("base")] int NameBaseIndex = 0,
        [property: JsonConverter(typeof(NullableIntArrayConverter))]
        int[]? origins = null
    );

    public record AzgaarReligion(
        int i,
        string name,
        string color,           // hex color e.g. "#b5b5b5"
        [property: JsonConverter(typeof(NullableIntArrayConverter))]
        int[]? origins,         // parent religion IDs; [0] or [null] or empty = root
        string type,            // "Folk", "Organized", "Heresy", "Cult"
        string deity,           // supreme deity name (may be empty string)
        int center,             // origin cell ID → used as holy site
        int culture,            // original culture ID
        float expansionism,     // growth multiplier
        string expansion,       // "culture" or "global"
        float rural,            // rural population (may be 0 if absent)
        float urban,            // urban population (may be 0 if absent)
        int cells,              // cell count (may be 0 if absent)
        [property: JsonConverter(typeof(BoolOrIntConverter))]
        int removed             // 1/true if deleted in Azgaar (newer exports use true/false)
    )
    {
        // Provide defaults so that older exports that omit fields don't fail deserialization
        public AzgaarReligion() : this(0, "", "#808080", null, "", "", 0, 0, 1.0f, "global", 0f, 0f, 0, 0) { }
    }

    public record AzgaarRiver(
        int i,              // River ID
        int source,         // Source cell ID
        int mouth,          // Mouth cell ID
        float discharge,    // Flow volume (key metric)
        float length,       // Total river length
        float width,        // Width at mouth
        float sourceWidth,  // Width at source
        float widthFactor,  // Width interpolation factor
        int parent,         // Parent river ID (0 if main river)
        int[] cells,        // Ordered list of cell IDs river passes through
        int basin,          // Watershed/basin ID
        string name,        // River name
        string type         // "River" or "Fork" (tributary)
    );

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
            // Replace the sentinel 0 at index 0 with a dummy province object.
            // Scan past any whitespace after '[' to handle both minified and formatted JSON.
            int firstToken = 1;
            while (firstToken < str.Length && char.IsWhiteSpace(str[firstToken])) firstToken++;
            if (firstToken < str.Length && str[firstToken] == '0')
            {
                escapedStr = string.Concat(
                    str.AsSpan(0, firstToken),
                    "{\"i\":0,\"state\":0,\"burg\":0,\"name\":\"\"}",
                    str.AsSpan(firstToken + 1));
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
            // Replace the sentinel 0 at index 0 with a dummy burg object.
            // Scan past any whitespace after '[' to handle both minified and formatted JSON.
            int firstToken = 1;
            while (firstToken < str.Length && char.IsWhiteSpace(str[firstToken])) firstToken++;
            if (firstToken < str.Length && str[firstToken] == '0')
            {
                escapedStr = string.Concat(
                    str.AsSpan(0, firstToken),
                    "{\"i\":0,\"name\":\"\",\"cell\":0,\"x\":0,\"y\":0,\"culture\":0,\"state\":0,\"feature\":0,\"population\":0,\"type\":\"\",\"capital\":0,\"port\":0,\"citadel\":0,\"plaza\":0,\"shanty\":0,\"temple\":0,\"walls\":0,\"removed\":false}",
                    str.AsSpan(firstToken + 1));
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

        public AzgaarRiver[] rivers { get; set; } = Array.Empty<AzgaarRiver>();
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

    // ========== River GeoJSON DTOs ==========

    public record RiverGeometry(
        string type,        // "LineString"
        double[][] coordinates  // Array of [x, y] coordinate pairs
    );

    public record RiverFeatureProperties(
        int id,
        int source,
        int mouth,
        int parent,
        int basin,
        float widthFactor,
        float sourceWidth,
        float discharge,
        string name,
        string type  // "River" or "Fork"
    );

    public record RiverFeature(
        string type,  // "Feature"
        RiverGeometry geometry,
        RiverFeatureProperties properties
    );

    /// <summary>
    /// Top-level structure for rivers GeoJSON export from Azgaar
    /// </summary>
    public record RiverGeoJson(
        string type,  // "FeatureCollection"
        RiverFeature[] features
    );
}
