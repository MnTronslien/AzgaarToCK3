using ImageMagick;
using Converter.Lemur;
using Converter.Lemur.Deserialization;
using Converter.Lemur.Governments;

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

        /// <summary>
        /// The suzerain's kingdom — the emperor's own demesne kingdom, set by EmpireDeFactoBuilder when this
        /// empire heads a de facto realm inferred from diplomacy. Drives CharacterFactory: it is the empire's
        /// sole de facto drill-child (the emperor holds it; vassal kingdoms get their own kings), and the
        /// culture/religion fallback below so a titular empire (zero de jure kingdoms) still derives the
        /// emperor's culture from the suzerain. Null for ordinary holderless culture/religion empires.
        /// </summary>
        public Kingdom? CapitalKingdom { get; set; }
        public AzgaarCulture Culture { get; set; }
        public AzgaarReligion Religion { get; set; }

        public ITitle? DeJureParent { get; set; }
        public ITitle? DeFactoLiege { get; set; }
        public Character? Holder { get; set; }
        public Ck3Government? Government { get; set; }

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
            // Titular empire (no de jure kingdoms): derive from the suzerain's kingdom so the emperor
            // gets the suzerain's culture rather than a fallback.
            if (Kingdoms.Count == 0 && CapitalKingdom != null)
                return CapitalKingdom.GetCultureDistributionByCells(map);
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
            if (Kingdoms.Count == 0 && CapitalKingdom != null)
                return CapitalKingdom.GetReligionDistributionByCells(map);
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