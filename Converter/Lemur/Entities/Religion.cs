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
    public bool IsUnreformed { get; init; }           // true if Type == "Folk" or "Cult"
    public string Deity { get; init; } = "";
    public string Expansion { get; init; } = "";
    public float Expansionism { get; init; }
    public int OriginCellId { get; init; }
    public int OriginalCultureId { get; init; }
    public float RuralPop { get; init; }
    public float UrbanPop { get; init; }
    public int CellCount { get; init; }
}
