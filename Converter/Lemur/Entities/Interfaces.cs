using ImageMagick;
using Converter.Lemur.Deserialization;

namespace Converter.Lemur.Entities
{
    /// <summary>
    /// Province in Crusader Kings 3 that can be a barony, sea zone, or a major river.
    /// </summary>
    public interface IProvince
    {
        /// <summary>
        /// The unique identifier of the province.
        /// </summary>
        public int Id { get; set; }
        public string Name { get; set; }

        public MagickColor Color { get; set; }

        public List<Cell> Cells { get; set; }
    }


    public interface ITitle
    {
        public int Id { get; }

        public string Name { get; set; }

        public Character? Holder { get; set; }

        public MagickColor? Color { get; set; }

        public List<Cell> Cells { get; set; }

        public ITitle? DeJureParent { get; set; }

        /// <summary>
        /// The de jure capital province of this title. Null if not yet assigned or not applicable.
        /// </summary>
        public Barony? Capital { get; set; }

        /// <summary>
        /// The de facto liege title. Null means independent (no liege declared in title history).
        /// For counties this is always set explicitly (own duchy, or primary duchy of an absorbed state).
        /// Set by CharacterFactory after all holders are assigned.
        /// </summary>
        public ITitle? DeFactoLiege { get; set; }

        /// <summary>
        /// A way to get all the cells in the title. 
        /// </summary>
        /// <returns></returns>
        public List<Cell> GetAllCells();
        /// <summary>
        /// Get the color of the title. Shoule return the assigned colour,
        /// but of null then look at the first ITitle lower in the hierarchy.
        /// </summary>
        public MagickColor? GetColor();
        
        public string Ck3_Id();

        Dictionary<int, int> GetCultureDistributionByCells(Map map);
        Dictionary<int, int> GetReligionDistributionByCells(Map map);

        public AzgaarCulture GetDominantCulture(Map map)
        {
            var counts = GetCultureDistributionByCells(map);
            if (counts.Count == 0) return map.JsonMap.pack.cultures[0];
            return map.JsonMap.pack.cultures[counts.OrderByDescending(x => x.Value).First().Key];
        }

        public AzgaarReligion GetDominantReligion(Map map)
        {
            var counts = GetReligionDistributionByCells(map);
            if (counts.Count == 0) return map.JsonMap.pack.religions[0];
            return map.JsonMap.pack.religions[counts.OrderByDescending(x => x.Value).First().Key];
        }

        /// <summary>
        /// Get the neighbours of the title. The key is the neighbour and the value is how often they share a border.
        /// </summary>
        /// <returns></returns>
        public Dictionary<ITitle, int> GetNeighbours();
    }
}