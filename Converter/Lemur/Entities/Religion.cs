namespace Converter.Lemur.Entities;

public class Religion
{
    public string Name { get; }
    public string Id => Name.ToLower().Replace(" ", "_");
    public Religion(string name) { Name = name; }
}
