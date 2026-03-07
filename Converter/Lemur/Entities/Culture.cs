namespace Converter.Lemur.Entities;

public class Culture
{
    public string Name { get; }
    public string Id => Name.ToLower().Replace(" ", "_");
    public Culture(string name) { Name = name; }
}
