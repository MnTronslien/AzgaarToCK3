namespace Converter.Lemur;
using Converter.Lemur.Entities;

public static class CharacterFactory
{
    public static void CreateAndAssignAll(Map map)
    {
        var culture = map.Cultures.Values.First();
        var faith = map.Faiths.Values.First();

        var ruler = new Character("The Lemur", culture, faith);
        map.Characters.Add(ruler);

        foreach (var empire in map.Empires!)
        {
            Assign(ruler, empire);
            foreach (var kingdom in empire.Kingdoms)
            {
                Assign(ruler, kingdom);
                foreach (var duchy in kingdom.Duchies)
                {
                    Assign(ruler, duchy);
                    foreach (var county in duchy.Counties)
                        Assign(ruler, county);
                }
            }
        }
    }

    private static void Assign(Character c, ITitle title)
    {
        title.Holder = c;
        c.HeldTitles.Add(title);
    }
}
