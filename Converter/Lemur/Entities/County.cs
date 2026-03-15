using ImageMagick;
using Converter.Lemur;
using Converter.Lemur.Deserialization;

namespace Converter.Lemur.Entities
{
    public class County : ITitle
    {
        public int Id { get; set; }

        public string Name { get; set; }
        public MagickColor? Color { get; set; }
        public List<Cell> Cells { get; set; }

        public ITitle? Parent { get; set; }
        public Character? Holder { get; set; }
        public List<Barony>? Baronies { get; set; }

        public Barony? Capital { get; set; }

        //constructor
        public County(int id, string name, List<Barony>? baronies = null, Duchy? duchy = null, Barony? capital = null)
        {
            Id = id;
            Name = name;
            if (baronies != null)
            {
                Baronies = baronies;
                Cells = Baronies.SelectMany(barony => barony.GetAllCells()).ToList();
                baronies.ForEach(barony => barony.Parent = this);
            }
            else
            {
                Cells = new List<Cell>();
            }
            if (duchy != null)
            {
                Parent = duchy;
                ((Duchy)Parent).Counties.Add(this);
            }

        }


        public string Ck3_Id() => Helper.ToCk3Id("c", Name, Id);

        public List<Cell> GetAllCells()
        {
            return Baronies.SelectMany(barony => barony.GetAllCells()).ToList();
        }

        public MagickColor? GetColor()
        {
            return Color ?? Baronies.FirstOrDefault()?.GetColor();
        }

        /// <summary>
        /// Get the distribution of cultures in this county by cell count, excluding wildlands
        /// </summary>
        public Dictionary<int, int> GetCultureDistributionByCells()
        {
            var cultureCounts = new Dictionary<int, int>();
            foreach (var barony in Baronies)
            {
                var baronyCounts = barony.GetCultureDistributionByCells();
                foreach (var kvp in baronyCounts)
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
            Dictionary<AzgaarReligion, int> religionCounts = new Dictionary<AzgaarReligion, int>();
            foreach (var barony in Baronies)
            {
                var dominantReligion = barony.GetDominantReligion(map);
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

        //Get adjacent counties, return a dictionary with the county as the key and the times it is adjacent as the value



        /// <summary>
        /// Get the neighbouring counties of this county
        /// This is done by getting the neighbours of each barony in the county and counting the number of times each county is a neighbour
        /// </summary>
        /// <returns> A dictionary with the county as the key and the number of times it is a neighbour as the value</returns>
        public Dictionary<ITitle, int> GetNeighbours()
        {
            Dictionary<County, int> adjacentCounties = new Dictionary<County, int>();
            foreach (var barony in Baronies)
            {
                foreach (var neighbour in barony.Neighbors)
                {
                    //Get the county of the neibghbour
                    var neighbourCounty = neighbour.Parent as County;

                    //If the county is already in the dictionary, increment the value
                    if (adjacentCounties.ContainsKey(neighbourCounty))
                    {
                        adjacentCounties[neighbourCounty]++;
                    }
                    //Otherwise add the county to the dictionary
                    else
                    {
                        adjacentCounties[neighbourCounty] = 1;
                    }
                }
            }
            //Remove the county itself from the dictionary
            adjacentCounties.Remove(this);
            return adjacentCounties.ToDictionary(x => (ITitle)x.Key, x => x.Value);
        }
    }
}