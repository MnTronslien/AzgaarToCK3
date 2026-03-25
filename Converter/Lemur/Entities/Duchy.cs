using ImageMagick;
using Converter.Lemur;
using Converter.Lemur.Deserialization;

namespace Converter.Lemur.Entities
{
    public class Duchy(int i, List<Cell> cells, string name) : ITitle
    {

        /// <summary>
        /// Azgaar province ID (normal duchies) or state ID (wasteland-derived duchies).
        /// Drives CK3 title ID generation and deterministic color hashing. Numeric collisions
        /// between province and state ID spaces are harmless — the duchy name is always part
        /// of the CK3 key. Does not represent Azgaar state membership; that is not persisted here.
        /// </summary>
        public int Id { get; set; } = i;

        /// <summary>
        /// The Azgaar state this duchy belongs to. Azgaar guarantees all cells in a province
        /// share the same state, so any cell is authoritative.
        /// </summary>
        public int AzgaarStateId => Cells.First().State;

        /// <summary>True if absorbed by MergeTinyKingdoms into a foreign kingdom (AzgaarStateId ≠ parent kingdom.Id); absorbed duchies start independent in title history.</summary>
        public bool IsAbsorbed => Cells.Any() && AzgaarStateId != ((Kingdom)DeJureParent!).Id;

public string Name { get; set; } = name;
        public MagickColor? Color { get; set; }
        public List<Cell> Cells { get; set; } = cells;
        public ITitle? DeJureParent { get; set; }
        public ITitle? DeFactoLiege { get; set; }
        public Character? Holder { get; set; }
        public List<Barony> Baronies { get; set; } = new List<Barony>();

        public List<County> Counties { get; set; } = new List<County>();
        public Barony? Capital { get; set; }


        public List<Cell> GetAllCells()
        {
            //return directly assigned cells
            return Cells;
        }

        public string Ck3_Id() => Helper.ToCk3Id("d", Name, Id);

        public MagickColor? GetColor()
        {
            return Color ?? Counties.FirstOrDefault()?.GetColor();
        }

        /// <summary>
        /// Get the distribution of cultures in this duchy by cell count, excluding invalid/removed cultures.
        /// </summary>
        public Dictionary<int, int> GetCultureDistributionByCells(Map map)
        {
            var counts = new Dictionary<int, int>();
            foreach (var county in Counties)
                counts.MergeAdd(county.GetCultureDistributionByCells(map));
            return counts;
        }

        /// <summary>
        /// Get the distribution of religions in this duchy by cell count, excluding invalid/removed religions.
        /// </summary>
        public Dictionary<int, int> GetReligionDistributionByCells(Map map)
        {
            var counts = new Dictionary<int, int>();
            foreach (var county in Counties)
                counts.MergeAdd(county.GetReligionDistributionByCells(map));
            return counts;
        }


        public Dictionary<ITitle, int> GetNeighbours()
        {
            Dictionary<Duchy, int> neighbouringDuchies = new Dictionary<Duchy, int>();
            // Track neighbouring counties and their border counts
            Dictionary<ITitle, int> neighbouringCounties = new Dictionary<ITitle, int>();

            foreach (var county in Counties)
            {
                // Get the neighbours of the county, filtering out internal neighbours
                var localNeighbours = county.GetNeighbours();
                foreach (var key in localNeighbours.Keys)
                {
                    // Ignore counties within the same duchy
                    if (Counties.Contains(key))
                    {
                        continue;
                    }
                    // Add or update the neighbouring county count
                    if (!neighbouringCounties.ContainsKey(key))
                    {
                        neighbouringCounties[key] = 1;
                    }
                    else
                    {
                        neighbouringCounties[key]++;
                    }
                }
            }

            // Sum up the neighbouring counties to the duchy level
            foreach (var county in neighbouringCounties.Keys)
            {
                // Get the duchy the county belongs to
                var duchy = (Duchy)county.DeJureParent;
                // Add or update the neighbouring duchy count
                if (!neighbouringDuchies.ContainsKey(duchy))
                {
                    neighbouringDuchies[duchy] = neighbouringCounties[county];
                }
                else
                {
                    neighbouringDuchies[duchy] += neighbouringCounties[county];
                }
            }

            return neighbouringDuchies.ToDictionary(x => (ITitle)x.Key, x => x.Value);
        }

    }
}