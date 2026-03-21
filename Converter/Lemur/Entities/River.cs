using Converter.Lemur.Deserialization;

namespace Converter.Lemur.Entities
{
    public class River
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public float Discharge { get; set; }
        public float Length { get; set; }
        public float Width { get; set; }
        public float SourceWidth { get; set; }
        public List<int> CellIds { get; set; } = new();  // Cells river flows through
        public int SourceCellId { get; set; }
        public int MouthCellId { get; set; }

        /// <summary>
        /// Control points for the river path from the GeoJSON export.
        /// These are the actual coordinates that define the river's path, NOT just cell centroids.
        /// Each point is [x, y] in Azgaar's coordinate system.
        /// </summary>
        public List<float[]>? ControlPoints { get; set; }

        /// <summary>
        /// Parent river ID. 0 if this is a main river, otherwise the ID of the river this flows into.
        /// </summary>
        public int ParentId { get; set; }

        /// <summary>
        /// River type: "River" for main rivers, "Fork" for tributaries.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// True if this river is a tributary (flows into another river).
        /// </summary>
        public bool IsTributary => ParentId != 0;

        /// <summary>
        /// Determines if this river is a major navigable river based on discharge threshold.
        /// </summary>
        public bool IsMajor(float threshold) => Discharge >= threshold;

        /// <summary>
        /// Creates a River entity from Azgaar river data.
        /// </summary>
        public static River FromAzgaarRiver(AzgaarRiver azRiver)
        {
            return new River
            {
                Id = azRiver.i,
                Name = azRiver.name,
                Discharge = azRiver.discharge,
                Length = azRiver.length,
                Width = azRiver.width,
                SourceWidth = azRiver.sourceWidth,
                CellIds = azRiver.cells.ToList(),
                SourceCellId = azRiver.source,
                MouthCellId = azRiver.mouth,
                ParentId = azRiver.parent,
                Type = azRiver.type
            };
        }
    }
}
