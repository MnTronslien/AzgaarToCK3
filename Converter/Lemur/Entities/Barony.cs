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
        /// Get the distribution of cultures in this barony by cell count, excluding wildlands (culture 0)
        /// </summary>
        public Dictionary<int, int> GetCultureDistributionByCells()
        {
            var cultureCounts = new Dictionary<int, int>();
            foreach (var cell in Cells)
            {
                // Skip wildlands culture (culture 0)
                if (cell.Culture == 0) continue;

                if (cultureCounts.ContainsKey(cell.Culture))
                {
                    cultureCounts[cell.Culture]++;
                }
                else
                {
                    cultureCounts[cell.Culture] = 1;
                }
            }
            return cultureCounts;
        }

        //Get the dominant culture of the barony by counting the number of cells with each culture and returning the most common (excluding wildlands)
        public AzgaarCulture GetDominantCulture(Map map)
        {
            var cultureCounts = GetCultureDistributionByCells();

            // If all cells are wildlands, return wildlands culture
            if (cultureCounts.Count == 0)
            {
                return map.JsonMap.pack.cultures[0];
            }

            var mostCommon = cultureCounts.Aggregate((l, r) => l.Value > r.Value ? l : r).Key;
            return map.JsonMap.pack.cultures[mostCommon];
        }

        public AzgaarReligion GetDominantReligion(Map map)
        {
            var religionCounts = new Dictionary<int, int>();
            foreach (var cell in Cells)
            {
                if (religionCounts.ContainsKey(cell.Religion))
                {
                    religionCounts[cell.Religion]++;
                }
                else
                {
                    religionCounts[cell.Religion] = 1;
                }
            }
            var mostCommon = religionCounts.Aggregate((l, r) => l.Value > r.Value ? l : r).Key;
            return map.JsonMap.pack.religions[mostCommon];
        }

        public Dictionary<ITitle, int> GetNeighbours()
        {
            return Neighbors?.ToDictionary(x => x as ITitle, x => 1) ?? new Dictionary<ITitle, int>();
        }
    }
}