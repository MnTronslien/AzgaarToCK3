using System.Numerics;

namespace Converter.Lemur.Entities
{
    public class Burg
    {
        public int id { get; set; }
        public string Name { get; set; }
        public Cell? Cell;

        public int Cell_id { get; set; }
        public CanvasPoint Position { get; set; }
        public int Culture { get; set; }
        public int State { get; set; }
        public int Feature { get; set; }
        public float Population { get; set; }
        public string Type { get; set; }
        public bool Capital { get; set; }
        public bool Port { get; set; }
        public bool Citadel { get; set; }
        public bool Plaza { get; set; }
        public bool Shanty { get; set; }
        public bool Temple { get; set; }
        public bool Walls { get; set; }
        public bool Removed { get; set; }
        public Barony Barony { get; set; }

        /// <summary>
        /// Clean constructor taking individual fields - no upstream dependencies
        /// </summary>
        public Burg(
            int i, string name, int cell_id, float x, float y,
            int culture, int state, int feature, float population,
            string type, bool capital, bool port, bool citadel,
            bool plaza, bool shanty, bool temple, bool walls, bool removed)
        {
            id = i;
            Name = name;
            Cell_id = cell_id;
            Position = new CanvasPoint(x, y);
            Culture = culture;
            State = state;
            Feature = feature;
            Population = population;
            Type = type;
            Capital = capital;
            Port = port;
            Citadel = citadel;
            Plaza = plaza;
            Shanty = shanty;
            Temple = temple;
            Walls = walls;
            Removed = removed;
        }

        //To string
        public override string ToString()
        {
            return $"id:{id},name:{Name},cell_id:{Cell_id},position:{Position},culture:{Culture},state:{State},feature:{Feature},population:{Population},type:{Type},capital:{Capital},port:{Port},citadel:{Citadel},plaza:{Plaza},shanty:{Shanty},temple:{Temple},walls:{Walls},removed:{Removed}";
        }
    }
}