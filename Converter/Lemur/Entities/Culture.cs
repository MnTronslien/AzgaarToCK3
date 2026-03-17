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
    public string NameList { get; set; } = "name_list_lemur_placeholder";

    // GFX bundle (4 strings: coa_gfx, building_gfx, clothing_gfx, unit_gfx)
    public string[] GfxBundle { get; set; } = [];

    public List<string> Traditions { get; set; } = new();
    /// <summary>CK3 keys of parent cultures (0, 1, or 2 entries)</summary>
    public List<string> Parents { get; set; } = new();
}
