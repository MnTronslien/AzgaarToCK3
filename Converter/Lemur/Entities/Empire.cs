using ImageMagick;
using Converter.Lemur;
using Converter.Lemur.Deserialization;

namespace Converter.Lemur.Entities
{
    public class Empire : ITitle
    {

        public Empire(int id, string name, MagickColor? magickColor)
        {
            Id = id;
            Name = name;
            Color = magickColor;
        }

        public int Id { get; set; }

        public string Name { get; set; }
        public MagickColor? Color { get; set; }
        public List<Cell> Cells { get; set; } = new List<Cell>();
        public List<Kingdom> Kingdoms { get; set; } = new List<Kingdom>();
        public Barony? Capital { get; set; }
        public AzgaarCulture Culture { get; set; }
        public AzgaarReligion Religion { get; set; }

        public ITitle? DeJureParent { get; set; }
        public ITitle? DeFactoLiege { get; set; }
        public Character? Holder { get; set; }

        public string Ck3_Id() => Helper.ToCk3Id("e", Name, Id);

        public List<Cell> GetAllCells()
        {
            //get all cells in kingdoms unless there are no kingdoms, then return cells
            if (Kingdoms.Count == 0)
            {
                return Cells;
            }
            else
            {
                List<Cell> cells = new List<Cell>();
                foreach (var kingdom in Kingdoms)
                {
                    cells.AddRange(kingdom.GetAllCells());
                }
                return cells;
            }
        }

        public MagickColor? GetColor()
        {
            //colour if not null if null use kingdoms
            return Color ?? Kingdoms.FirstOrDefault()?.GetColor();
        }

        /// <summary>
        /// Get the distribution of cultures in this empire by cell count, excluding invalid/removed cultures.
        /// </summary>
        public Dictionary<int, int> GetCultureDistributionByCells(Map map)
        {
            var counts = new Dictionary<int, int>();
            foreach (var kingdom in Kingdoms)
                counts.MergeAdd(kingdom.GetCultureDistributionByCells(map));
            return counts;
        }

        /// <summary>
        /// Get the distribution of religions in this empire by cell count, excluding invalid/removed religions.
        /// </summary>
        public Dictionary<int, int> GetReligionDistributionByCells(Map map)
        {
            var counts = new Dictionary<int, int>();
            foreach (var kingdom in Kingdoms)
                counts.MergeAdd(kingdom.GetReligionDistributionByCells(map));
            return counts;
        }


        public Dictionary<ITitle, int> GetNeighbours()
        {
            Dictionary<ITitle, int> neighbouringEmpires = new Dictionary<ITitle, int>();
        
            foreach (var kingdom in Kingdoms)
            {
                var localNeighbours = kingdom.GetNeighbours();
                if (localNeighbours == null || !localNeighbours.Any())
                {
                    continue;
                }
                foreach (var neighbour in localNeighbours.Keys)
                {
                    // Ignore kingdoms within the same empire
                    if (Kingdoms.Contains(neighbour))
                    {
                        continue;
                    }

                    // The neighbour is from another empire, so we update the count
                    var empire = neighbour.DeJureParent; // Can be null for orphan kingdoms

                    // Skip orphan kingdoms (no parent empire)
                    if (empire == null)
                    {
                        continue;
                    }

                    if (!neighbouringEmpires.ContainsKey(empire))
                    {
                        neighbouringEmpires[empire] = localNeighbours[neighbour];
                    }
                    else
                    {
                        neighbouringEmpires[empire] += localNeighbours[neighbour];
                    }
                }
            }
        
            return neighbouringEmpires;
        }
    }
}