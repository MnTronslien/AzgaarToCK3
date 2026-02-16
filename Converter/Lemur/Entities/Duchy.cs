using ImageMagick;
using Converter.Lemur.Deserialization;

namespace Converter.Lemur.Entities
{
    public class Duchy(int i, List<Cell> cells, string name) : ITitle
    {

        public int Id { get; set; } = i;

        public string Name { get; set; } = name;
        public MagickColor? Color { get; set; }
        public List<Cell> Cells { get; set; } = cells;
        public ITitle? Parent { get; set; }
        public List<Barony> Baronies { get; set; } = new List<Barony>();

        public List<County> Counties { get; set; } = new List<County>();


        public List<Cell> GetAllCells()
        {
            //return directly assigned cells
            return Cells;
        }

        public string Ck3_Id()
        {
            return $"d_{Name}_{Id}";
        }

        public MagickColor? GetColor()
        {
            return Color ?? Counties.FirstOrDefault()?.GetColor();
        }

        /// <summary>
        /// Get the distribution of cultures in this duchy by cell count, excluding wildlands
        /// </summary>
        public Dictionary<int, int> GetCultureDistributionByCells()
        {
            var cultureCounts = new Dictionary<int, int>();
            foreach (var county in Counties)
            {
                var countyCounts = county.GetCultureDistributionByCells();
                foreach (var kvp in countyCounts)
                {
                    if (cultureCounts.ContainsKey(kvp.Key))
                    {
                        cultureCounts[kvp.Key] += kvp.Value;
                    }
                    else
                    {
                        cultureCounts[kvp.Key] = kvp.Value;
                    }
                }
            }
            return cultureCounts;
        }

        public AzgaarCulture GetDominantCulture(Map map)
        {
            var cultureCounts = GetCultureDistributionByCells();

            // If all cells are wildlands, return wildlands culture
            if (cultureCounts.Count == 0)
            {
                return map.JsonMap.pack.cultures[0];
            }

            var mostCommon = cultureCounts.OrderByDescending(x => x.Value).First().Key;
            return map.JsonMap.pack.cultures[mostCommon];
        }

        public AzgaarReligion GetDominantReligion(Map map)
        {
            //Among the counties in this duchy, what is the most common religion?
            Dictionary<AzgaarReligion, int> religionCounts = new Dictionary<AzgaarReligion, int>();
            foreach (var county in Counties)
            {
                var dominantReligion = county.GetDominantReligion(map);
                if (religionCounts.ContainsKey(dominantReligion))
                {
                    religionCounts[dominantReligion]++;
                }
                else
                {
                    religionCounts[dominantReligion] = 1;
                }
            }
            return religionCounts.OrderByDescending(x => x.Value).First().Key;
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
                var duchy = (Duchy)county.Parent;
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