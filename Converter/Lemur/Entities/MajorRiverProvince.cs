using ImageMagick;

namespace Converter.Lemur.Entities
{
    public class MajorRiverProvince : IProvince
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public MagickColor Color { get; set; } = MagickColors.Black;
        public List<Cell> Cells { get; set; }
        public int AzgaarRiverId { get; set; }

        public MajorRiverProvince(int id, List<Cell> cells, string name, int azgaarRiverId)
        {
            Id = id;
            Cells = cells ?? new List<Cell>();
            Name = name;
            AzgaarRiverId = azgaarRiverId;
            foreach (var cell in Cells)
                cell.Province = this;
        }
    }
}
