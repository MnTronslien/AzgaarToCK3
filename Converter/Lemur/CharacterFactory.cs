namespace Converter.Lemur;
using Converter.Lemur.Entities;

public static class CharacterFactory
{
    public static void CreateAndAssignAll(Map map)
    {
        var culture = new Culture("English");
        var faith = new Faith { Name = "Catholic", CK3Key = "catholic" };
        map.Cultures.Add(culture);
        // Use sentinel key -1 for this placeholder faith (real faiths use AzgaarId >= 1)
        map.Faiths[-1] = faith;

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
