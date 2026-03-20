namespace Converter.Lemur.Entities;

public class Culture
{
    public int AzgaarId { get; set; }
    public string Name { get; set; } = "";
    public string CK3Key => $"lemur_culture_{AzgaarId}";
    public string HexColor { get; set; } = "#808080";

    // Pillars
    public string Ethos { get; set; } = "ethos_communal";
    public string Heritage { get; set; } = "";
    public string Language { get; set; } = "";
    public string MartialCustom { get; set; } = "martial_custom_male_only";
    public string HeadDetermination { get; set; } = "head_determination_domain";
    // Theme bundle — GFX keys + vanilla name list
    public ThemeBundle ThemeBundle { get; set; } = null!;

    public List<string> Traditions { get; set; } = new();
    /// <summary>Direct references to parent cultures (0, 1, or 2 entries)</summary>
    public List<Culture> Parents { get; set; } = new();

    /// <summary>
    /// Creation date for derived/hybrid cultures. Null for foundational cultures (ancient, no created date needed).
    /// Written as `created = DATE` in culture history.
    /// </summary>
    public string? CreationDate { get; set; } = null;
}
