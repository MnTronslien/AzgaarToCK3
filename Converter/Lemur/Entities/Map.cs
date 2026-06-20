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

        /// <summary>
        /// CK3 game-start date — single source of truth for every dated emission (title history,
        /// bookmark, character births, culture creation dates). World data, not a setting; a future
        /// loader will populate it from the Azgaar export. Default 1066.1.1.
        /// </summary>
        public StartDate StartDate { get; set; } = new(1066, 1, 1);

        public Dictionary<int, Cell>? Cells { get; set; }
        public Dictionary<int, Burg>? Burgs { get; set; }

        // Populated by HeightmapWriter after HeightmapAlgorithm.Generate completes.
        // Read by TerrainMaskWriter so steepness-based splat materials (hills, mountain) have
        // access to the rendered heightmap. Null if the heightmap pipeline hasn't run yet — in
        // which case splat painting falls back to biome-only output.
        public byte[]? HeightmapPixels { get; set; }
        public float[]? HeightmapF { get; set; }
        // p95-normalised per-pixel steepness [0..1], computed ONCE from the heightmap (HeightmapWriter)
        // and shared by the splatmap (hills/mountain materials) and vegetation (steep-slope veto) —
        // so neither recomputes it. Null if the heightmap pipeline hasn't run.
        public float[]? SteepnessField { get; set; }
        // Per-pixel blended biome weights (Delaunay + barycentric over the whole map — the single
        // most expensive field in the pipeline). Built ONCE by the first consumer (TerrainMaskWriter's
        // splatmap) and shared with the vegetation writer so it isn't rasterised twice. Null until built.
        public Splats.BiomeWeightTriple[]? BiomeWeights { get; set; }

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

        /// <summary>Sea straits (PLAN_straits.md), generated after sea zones; written to adjacencies.csv.</summary>
        public List<Straits.Strait> Straits { get; set; } = new();

        public List<Character> Characters { get; set; } = new();
        public Dictionary<int, Culture> Cultures { get; set; } = new();
        public Dictionary<int, Faith> Faiths { get; set; } = new();

        public List<HolySite> HolySites { get; set; } = new();

        /// <summary>
        /// Resolved suzerain/vassal structure from the Azgaar diplomacy table (see <see cref="DiplomacyResolver"/>).
        /// Built after GenerateKingdoms; read by MergeTinyKingdoms (root protection) and EmpireDeFactoBuilder.
        /// Null when diplomacy hasn't been resolved (e.g. the rivers-only / cell-dump fast paths).
        /// </summary>
        public DiplomacyResult? Diplomacy { get; set; }

        public override string ToString()
        {
            // Return the name of the map and the number of cells in the packed map
            return $"{JsonMap.info.mapName}({JsonMap.info.width}x{JsonMap.info.height}[{JsonMap.pack.cells.Length}])";
        }
    }




}