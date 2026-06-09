using Converter.Lemur.Entities;
using Converter.Lemur;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

// Emits the converter-owned religion family (rf_lemurconverter), named after the Azgaar world, that
// every generated faith belongs to. Hostility uses vanilla abrahamic_hostility_doctrine, applied
// per-religion by FaithWriter. See PLAN_religious_families.md.
public static class ReligiousFamilyWriter
{
    public const string FamilyKey = "rf_lemurconverter";
    public const string HostilityDoctrine = "abrahamic_hostility_doctrine";

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

        var locDir = Helper.GetPath(outputDirectory, "localization", "english");
        Directory.CreateDirectory(locDir);
        await File.WriteAllLinesAsync(Helper.GetPath(locDir, "lemur_religion_families_l_english.yml"), new[]
        {
            "l_english:",
            $" {FamilyKey}:0 \"{worldName}\"",
        }, Helper.Utf8Bom);

        Logger.Info($"Wrote religious family {FamilyKey} (\"{worldName}\").");
    }
}
