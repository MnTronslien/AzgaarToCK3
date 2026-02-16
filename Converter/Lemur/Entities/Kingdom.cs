using ImageMagick;
using Converter.Lemur.Deserialization;

namespace Converter.Lemur.Entities
{
    public class Kingdom : ITitle
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public MagickColor? Color { get; set; }
        public List<Cell> Cells { get; set; } = new List<Cell>();

        public List<Duchy> Duchies { get; set; } = new List<Duchy>();

        public ITitle? Parent { get; set; }

        public Kingdom(int id, string name, MagickColor? color, List<Duchy>? duchies)
        {
            Id = id;
            Name = name;

            if (duchies != null)
            {
                Duchies = duchies;
                Cells = GetAllCells();
                duchies.ForEach(duchy => duchy.Parent = this);
            }
        }

        public List<Cell> GetAllCells()
        {
            //if no duchies throw exception saying no duchies
            if (Duchies.Count == 0)
            {
                throw new ArgumentException($"Cannot get cells from a kingdom [{Name}] with no duchies.");
            }
            //Get cells by calling Get cells on each duchy object in Duchies

            List<Cell> cells = new List<Cell>();
            foreach (var duchy in Duchies)
            {
                cells.AddRange(duchy.GetAllCells());
            }
            return cells;
        }

        public MagickColor? GetColor()
        {
            return Color ?? Duchies.FirstOrDefault()?.GetColor();
        }

        /// <summary>
        /// Get the distribution of cultures in this kingdom by cell count, excluding wildlands
        /// </summary>
        public Dictionary<int, int> GetCultureDistributionByCells()
        {
            var cultureCounts = new Dictionary<int, int>();
            foreach (var duchy in Duchies)
            {
                var duchyCounts = duchy.GetCultureDistributionByCells();
                foreach (var kvp in duchyCounts)
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
            //Get the most common religion among the duchies in this kingdom
            Dictionary<AzgaarReligion, int> religionCounts = new Dictionary<AzgaarReligion, int>();
            foreach (var duchy in Duchies)
            {
                var dominantReligion = duchy.GetDominantReligion(map);
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
            Dictionary<ITitle, int> neighbouringKingdoms = new Dictionary<ITitle, int>();

            foreach (var duchy in Duchies)
            {
                var localNeighbours = duchy.GetNeighbours();
                foreach (var neighbour in localNeighbours.Keys)
                {
                    // Ignore duchies within the same kingdom
                    if (Duchies.Contains(neighbour))
                    {
                        continue;
                    }

                    // The neighbour is from another kingdom, so we update the count
                    var kingdom = neighbour.Parent; // Can be null in edge cases

                    // Skip orphan duchies (no parent kingdom)
                    if (kingdom == null)
                    {
                        continue;
                    }

                    if (!neighbouringKingdoms.ContainsKey(kingdom))
                    {
                        neighbouringKingdoms[kingdom] = localNeighbours[neighbour];
                    }
                    else
                    {
                        neighbouringKingdoms[kingdom] += localNeighbours[neighbour];
                    }
                }
            }

            return neighbouringKingdoms;
        }
    }
}