namespace Converter.Lemur.Entities;

public class Faith
{
    // Identity / CK3 keys
    public int AzgaarId { get; init; }
    public string Name { get; init; } = "";
    public string CK3Key { get; init; } = "";         // "lemur_faith_{i}"
    public string CK3ReligionKey { get; init; } = ""; // "lemur_religion_{rootId}"
    public string HexColor { get; init; } = "#808080";
    public string IconKey { get; init; } = "";        // "custom_faith_{(azgaarId%10)+1}"
    public Faith? Parent { get; set; }

    // Azgaar fields
    public string Type { get; init; } = "";
    // true if Type == "Folk"
    public bool IsUnreformed => Type == "Folk";
    public string Deity { get; init; } = "";
    public string Expansion { get; init; } = "";
    public float Expansionism { get; init; }
    public int OriginCellId { get; init; }
    public int OriginalCultureId { get; init; }

    // Assigned by DoctrineAssigner after faith graph is built
    public List<string> Doctrines { get; set; } = [];  // one per required doctrine group (21 entries)
    public List<string> Tenets { get; set; } = [];      // 1–5 tenets

    // Set by HolySiteFactory.Build() — first entry is the primary site
    public List<HolySite> HolySites { get; set; } = [];
}
