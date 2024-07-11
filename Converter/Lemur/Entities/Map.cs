﻿namespace Converter.Lemur.Entities
{
    public record Map
    {
        public const int MapWidth = 8192;
        public const int MapHeight = 4096;
        public float XOffset => JsonMap.mapCoordinates.lonW;
        public float YOffset => JsonMap.mapCoordinates.latS;
        public float XRatio => MapWidth / JsonMap.mapCoordinates.lonT;
        public float YRatio => MapHeight / JsonMap.mapCoordinates.latT;
        public required GeoMap GeoMap { get; set; }

        //TODO: Rivers public GeoMapRivers Rivers { get; set; }
        public required JsonMap JsonMap { get; set; }
        public required Settings Settings { get; set; }

        public Dictionary<int, Cell>? Cells { get; set; }
        public Dictionary<int, Burg>? Burgs { get; set; }

        public List<Barony>? Baronies { get; set; }
        public List<Duchy>? Duchies { get; internal set; }

        // list of wasteland provinces, ths is because provinces with no burgs counts as wasteland. Add 0 by default
        public List<int> Wastelands { get; set; } = [0];
        

        

        public override string ToString()
        {
            // Return the name of the map and the number of cells in the packed map
            return $"{JsonMap.info.mapName}({JsonMap.info.width}x{JsonMap.info.height}[{JsonMap.pack.cells.Length}])";
        }
    }




}