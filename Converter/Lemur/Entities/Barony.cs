using ImageMagick;
using Converter.Lemur;
using Converter.Lemur.Deserialization;

namespace Converter.Lemur.Entities
{
    public class Barony : IProvince, ITitle
    {
        public Barony(Burg value)
        {   
            //link to burg and back 1:1 relationship
            burg = value;
            value.Barony = this;
            Id = burg.id;
            Name = burg.Name;
    
        }
        public readonly Burg burg;
        public int Id { get; set; }
        public string Name { get; set; }

        public List<Cell> Cells { get; set; } = new List<Cell>();

        public MagickColor Color { get; set; }
        /// <summary>
        /// See <see cref="ConversionManager.GenerateBaronyAdjacency"/> for how this is generated.
        /// </summary>
        public List<Barony>? Neighbors { get; set; }
        public ITitle? Parent { get; set; }
        public Character? Holder { get; set; }

        public string Ck3_Id() => Helper.ToCk3Id("b", Name, Id);

        public List<Cell> GetAllCells()
        {
            return Cells;
        }

        //Hash and equality check
        public override int GetHashCode()
        {
            return Id;
        }

        public override bool Equals(object? obj)
        {
            if (obj is Barony other)
            {
                return Id == other.Id;
            }
            return false;
        }

        public MagickColor? GetColor()
        {
            return Color;
        }
        /// <summary>
        /// Get the distribution of cultures in this barony by cell count, excluding invalid/removed cultures.
        /// </summary>
        public Dictionary<int, int> GetCultureDistributionByCells(Map map)
        {
            var counts = new Dictionary<int, int>();
            foreach (var cell in Cells)
            {
                if (!map.Cultures.ContainsKey(cell.Culture)) continue;
                counts[cell.Culture] = counts.GetValueOrDefault(cell.Culture, 0) + 1;
            }
            return counts;
        }

        /// <summary>
        /// Get the distribution of religions in this barony by cell count, excluding invalid/removed religions.
        /// </summary>
        public Dictionary<int, int> GetReligionDistributionByCells(Map map)
        {
            var counts = new Dictionary<int, int>();
            foreach (var cell in Cells)
            {
                if (!map.Faiths.ContainsKey(cell.Religion)) continue;
                counts[cell.Religion] = counts.GetValueOrDefault(cell.Religion, 0) + 1;
            }
            return counts;
        }


        public Dictionary<ITitle, int> GetNeighbours()
        {
            return Neighbors?.ToDictionary(x => x as ITitle, x => 1) ?? new Dictionary<ITitle, int>();
        }
    }
}