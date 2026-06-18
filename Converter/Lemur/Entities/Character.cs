namespace Converter.Lemur.Entities;

public class Character
{
    private static int _counter = 0;

    public string Id { get; }
    public string? Name { get; }
    public Culture Culture { get; }
    public Faith Faith { get; }
    public int BirthYear { get; }
    public List<ITitle> HeldTitles { get; } = new();

    public Character(Culture culture, Faith faith, int birthYear, string? name = null)
    {
        Id = $"lemur_{++_counter}";
        Name = name;
        Culture = culture;
        Faith = faith;
        BirthYear = birthYear;
    }
}
