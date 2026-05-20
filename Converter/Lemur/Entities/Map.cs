using Converter.Lemur.Deserialization;

namespace Converter.Lemur.Entities
{
    public record Map
    {
        // IMPORTANT: These must match the TCS heightmap binary dimensions.
        // StaticFilesWriter copies TCS's packed_heightmap.png / indirection_heightmap.png
        // unchanged, and the heightmap.heightmap config is hardcoded to these values.
        // If you ever generate a custom heightmap, update StaticFilesWriter too.
        // See notes-for-later.md §Heightmap.
        public const int MapWidth = 8192;
        public const int MapHeight = 4096;
        public float XOffset => JsonMap.mapCoordinates.lonW;
        public float YOffset => JsonMap.mapCoordinates.latS;
        public float XRatio => MapWidth / JsonMap.mapCoordinates.lonT;
        public float YRatio => MapHeight / JsonMap.mapCoordinates.latT;

        // Now using Azgaar DTOs instead of upstream types
        public AzgaarGeoMap? GeoMap { get; set; }

        public List<River>? Rivers { get; set; }
        public required AzgaarJsonMap JsonMap { get; set; }
        public required Settings Settings { get; set; }

        public Dictionary<int, Cell>? Cells { get; set; }
        public Dictionary<int, Burg>? Burgs { get; set; }

        // Populated by HeightmapWriter after HeightmapAlgorithm.Generate completes.
        // Read by TerrainMaskWriter so steepness-based splat materials (hills, mountain) have
        // access to the rendered heightmap. Null if the heightmap pipeline hasn't run yet — in
        // which case splat painting falls back to biome-only output.
        public byte[]? HeightmapPixels { get; set; }
        public float[]? HeightmapF { get; set; }

        public List<Barony>? Baronies { get; set; } = new();

        public List<County>? Counties { get; set; } = new();
        public List<Duchy>? Duchies { get; set; } = new();
        public List<Kingdom> Kingdoms { get; set; } = new();
        public List<Empire>? Empires { get; internal set; } = new();

        // list of wasteland provinces, ths is because provinces with no burgs counts as wasteland. Add 0 by default
        public List<Wasteland>? Wastelands { get; set; } = new();

        public List<SeaZone>? SeaZones { get; set; } = new();

        public List<SeaZone>? FarSeaZones { get; set; } = new();

        public List<MajorRiverProvince> MajorRiverProvinces { get; set; } = new();

        public List<IProvince>? AllProvinces { get; set; }

        public List<Character> Characters { get; set; } = new();
        public Dictionary<int, Culture> Cultures { get; set; } = new();
        public Dictionary<int, Faith> Faiths { get; set; } = new();

        public List<HolySite> HolySites { get; set; } = new();

        public override string ToString()
        {
            // Return the name of the map and the number of cells in the packed map
            return $"{JsonMap.info.mapName}({JsonMap.info.width}x{JsonMap.info.height}[{JsonMap.pack.cells.Length}])";
        }
    }




}