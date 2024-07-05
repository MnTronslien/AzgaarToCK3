using ImageMagick;

namespace Converter.Lemur.Entities
{
    public class Barony : IProvince, ITitle
    {
        public readonly Burg burg;

        public int Id { get; set; }
        public string Name { get; set; }

        public List<Cell> Cells { get; set; } = new List<Cell>();

        // IProvince implementation
        public MagickColor Color { get; set; }
        public Barony(Burg value)
        {   
            //link to burg and back 1:1 relationship
            burg = value;
            value.Barony = this;
            Id = burg.id;
            Name = burg.Name;
    
        }

        public List<Cell> GetAllCells()
        {
            return Cells;
        }
    }
}