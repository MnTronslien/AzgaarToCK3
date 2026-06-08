using Converter.Lemur.Entities;
using Converter.Lemur;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

// Emits a converter-owned religion family (rf_lemurconverter) so every generated faith shares one
// family with Abrahamic-like (harsh) hostility — generated faiths read as rivals to each other and
// to vanilla faiths. The family's display name is the Azgaar world name: it is the universal faith
// family of all peoples in this world. See PLAN_religious_families.md.
public static class ReligiousFamilyWriter
{
    public const string FamilyKey = "rf_lemurconverter";
    private const string HostilityDoctrine = "lemur_hostility_doctrine";
    private const string HostilityGroup = "lemur_hostility_group";

    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing religious family");

        var worldName = map.JsonMap.info.mapName;

        var familyDir = Helper.GetPath(outputDirectory, "common", "religion", "religion_family_types");
        Directory.CreateDirectory(familyDir);
        await File.WriteAllLinesAsync(Helper.GetPath(familyDir, "01_lemur_family.txt"), new[]
        {
            "# Lemur conversion: family for all generated faiths.",
            $"{FamilyKey} = {{",
            $"\thostility_doctrine = {HostilityDoctrine}",
            "\tgraphical_faith = \"pagan_gfx\"",
            "\tpiety_icon_group = \"pagan\"",
            "\tdoctrine_background_icon = \"core_tenet_banner_pagan.dds\"",
            "}",
        }, Helper.Utf8Bom);

        // Hostility doctrine lives in our own group (category not_creatable, never player-picked).
        var groupDir = Helper.GetPath(outputDirectory, "common", "religion", "doctrine_group_types");
        Directory.CreateDirectory(groupDir);
        await File.WriteAllLinesAsync(Helper.GetPath(groupDir, "01_lemur_hostility_group.txt"), new[]
        {
            $"{HostilityGroup} = {{",
            "\tcategory = \"not_creatable\"",
            "\tdoctrine_types = {",
            $"\t\t{HostilityDoctrine}",
            "\t}",
            "}",
        }, Helper.Utf8Bom);

        // Abrahamic-like tiers: same_religion=hostile, same_family/others=evil.
        var doctrineDir = Helper.GetPath(outputDirectory, "common", "religion", "doctrine_types");
        Directory.CreateDirectory(doctrineDir);
        await File.WriteAllLinesAsync(Helper.GetPath(doctrineDir, "01_lemur_hostility.txt"), new[]
        {
            $"{HostilityDoctrine} = {{",
            "\tparameters = {",
            "\t\thostility_same_religion = 2",
            "\t\thostility_same_family = 3",
            "\t\thostility_others = 3",
            "\t}",
            "}",
        }, Helper.Utf8Bom);

        var locDir = Helper.GetPath(outputDirectory, "localization", "english");
        Directory.CreateDirectory(locDir);
        await File.WriteAllLinesAsync(Helper.GetPath(locDir, "lemur_religion_families_l_english.yml"), new[]
        {
            "l_english:",
            $" {FamilyKey}:0 \"{worldName}\"",
            $" {HostilityGroup}_name:0 \"{worldName}\"",
            $" {HostilityDoctrine}_name:0 \"{worldName}\"",
            $" {HostilityDoctrine}_desc:0 \"The native faiths of {worldName} regard one another, and all foreign faiths, as rivals.\"",
        }, Helper.Utf8Bom);

        Logger.Info($"Wrote religious family {FamilyKey} (\"{worldName}\") with hostility doctrine.");
    }
}
