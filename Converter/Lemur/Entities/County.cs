using ImageMagick;
using Converter.Lemur;
using Converter.Lemur.Deserialization;
using Converter.Lemur.Governments;

namespace Converter.Lemur.Entities
{
    public class County : ITitle
    {
        public int Id { get; set; }

        public string Name { get; set; }
        public MagickColor? Color { get; set; }
        public List<Cell> Cells { get; set; }

        public ITitle? DeJureParent { get; set; }
        public ITitle? DeFactoLiege { get; set; }
        public Character? Holder { get; set; }
        public Ck3Government? Government { get; set; }
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
                baronies.ForEach(barony => barony.DeJureParent = this);
            }
            else
            {
                Cells = new List<Cell>();
            }
            if (duchy != null)
            {
                DeJureParent = duchy;
                ((Duchy)DeJureParent).Counties.Add(this);
            }
            if (capital != null)
                Capital = capital;
        }


        public string Ck3_Id() => Helper.ToCk3Id("c", Name, Id);

        /// <summary>
        /// CK3 start-of-game development level derived from the capital burg's Azgaar
        /// population points. One pop point ≈ 1000 people; we map 1:1 to dev so the
        /// game's growth loop has room to play out. Clamped to [1, 100] — 1 keeps
        /// CK3 from treating the county as uninhabited; 100 mirrors the engine cap.
        /// Falls back to the most populous barony if Capital is unset.
        /// </summary>
        public int GetDevelopmentLevel()
        {
            var burg = Capital?.burg
                ?? Baronies?.OrderByDescending(b => b.burg.Population).FirstOrDefault()?.burg;
            if (burg == null) return 1;
            return Math.Clamp((int)Math.Round(burg.Population), 1, 100);
        }

        public List<Cell> GetAllCells()
        {
            return Baronies.SelectMany(barony => barony.GetAllCells()).ToList();
        }

        public MagickColor? GetColor()
        {
            return Color ?? Baronies.FirstOrDefault()?.GetColor();
        }

        /// <summary>
        /// Get the distribution of cultures in this county by cell count, excluding invalid/removed cultures.
        /// </summary>
        public Dictionary<int, int> GetCultureDistributionByCells(Map map)
        {
            var counts = new Dictionary<int, int>();
            foreach (var barony in Baronies)
                counts.MergeAdd(barony.GetCultureDistributionByCells(map));
            return counts;
        }

        /// <summary>
        /// Get the distribution of religions in this county by cell count, excluding invalid/removed religions.
        /// </summary>
        public Dictionary<int, int> GetReligionDistributionByCells(Map map)
        {
            var counts = new Dictionary<int, int>();
            foreach (var barony in Baronies)
                counts.MergeAdd(barony.GetReligionDistributionByCells(map));
            return counts;
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
                    var neighbourCounty = neighbour.DeJureParent as County;

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