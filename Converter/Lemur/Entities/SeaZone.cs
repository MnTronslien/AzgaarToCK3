using ImageMagick;

namespace Converter.Lemur.Entities
{
    public class SeaZone : IProvince
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public MagickColor Color { get; set; } = MagickColors.Black;
        public List<Cell> Cells { get; set; }
        public int TotalArea { get; set; }
        public bool IsImpassable { get; set; } = false;

        public SeaZone(int id, List<Cell> cells)
        {
            Id = id;
            Cells = cells;
            foreach (var cell in cells)
                cell.Province = this;
        }
    }
}
