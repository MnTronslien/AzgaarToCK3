using ImageMagick;
using Converter.Lemur;
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

        public ITitle? DeJureParent { get; set; }
        public ITitle? DeFactoLiege { get; set; }
        public Character? Holder { get; set; }

        public Kingdom(int id, string name, MagickColor? color, List<Duchy>? duchies)
        {
            Id = id;
            Name = name;

            if (duchies != null)
            {
                Duchies = duchies;
                Cells = GetAllCells();
                duchies.ForEach(duchy => duchy.DeJureParent = this);
            }
        }

        public string Ck3_Id() => Helper.ToCk3Id("k", Name, Id);

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
        /// Get the distribution of cultures in this kingdom by cell count, excluding invalid/removed cultures.
        /// </summary>
        public Dictionary<int, int> GetCultureDistributionByCells(Map map)
        {
            var counts = new Dictionary<int, int>();
            foreach (var duchy in Duchies)
                counts.MergeAdd(duchy.GetCultureDistributionByCells(map));
            return counts;
        }

        /// <summary>
        /// Get the distribution of religions in this kingdom by cell count, excluding invalid/removed religions.
        /// </summary>
        public Dictionary<int, int> GetReligionDistributionByCells(Map map)
        {
            var counts = new Dictionary<int, int>();
            foreach (var duchy in Duchies)
                counts.MergeAdd(duchy.GetReligionDistributionByCells(map));
            return counts;
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
                    var kingdom = neighbour.DeJureParent; // Can be null in edge cases

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