namespace Converter.Lemur.Entities;

public class HolySite
{
    public string Key { get; init; } = "";         // "lemur_site_{uniqueIndex}"
    public County County { get; init; } = null!;   // direct ref — .Ck3_Id() at write time
    public Barony? Barony { get; init; }            // optional, for barony-level specificity
    public List<(string Key, string Value)> Modifiers { get; init; } = [];

    public string ModifierNameKey =>               // loc key for tooltip
        $"holy_site_{Key}_effect_name";
}
