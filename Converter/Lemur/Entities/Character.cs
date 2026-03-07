namespace Converter.Lemur.Entities;

public class Character
{
    private static int _counter = 0;

    public string Id { get; }
    public string Name { get; }
    public Culture Culture { get; }
    public Religion Religion { get; }
    public int BirthYear { get; }
    public List<ITitle> HeldTitles { get; } = new();

    public Character(string name, Culture culture, Religion religion)
    {
        Id = $"lemur_{++_counter}";
        Name = name;
        Culture = culture;
        Religion = religion;
        BirthYear = 1033;
    }
}
