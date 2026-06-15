using Converter.Lemur;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

/// <summary>
/// Emits CK3 <c>common/flavorization/</c> entries so each title shows its Azgaar
/// <c>formName</c> prefix ("Brotherhood of X", "Horde of Y") instead of CK3's generic
/// tier+government default. Flavorization is runtime-evaluated by government, so the prefix
/// follows the current holder: a feudal conqueror taking a theocracy reverts to "Kingdom of X".
///
/// Mirrors <see cref="Governments.GovernmentResolver"/>'s two passes:
///   * one entry per surviving kingdom, keyed on its own Azgaar state;
///   * one entry per absorbed duchy (its parent state dissolved in MergeTinyKingdoms), keyed
///     on the duchy's original state — preserving flavour even under a foreign liege.
///
/// The <c>governments</c> selector lists the resolved government plus its DLC fallback chain
/// (<see cref="Governments.Ck3Government.KeyWithFallbacks"/>) so the prefix survives the engine's
/// game-start substitution when a DLC is absent (Nomad → Tribal).
///
/// Must run after GovernmentResolver has populated Kingdom/Duchy.Government.
/// </summary>
public static class FlavorizationWriter
{
    // Beats vanilla's tier+government entries (max observed priority ~76); titles = { id } keeps
    // each entry strictly scoped so there is no cross-title bleed.
    private const int Priority = 100;

    public static async Task Write(L.Map map, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing realm-name flavorization");

        var statesById = map.JsonMap.pack.states.ToDictionary(s => s.i);

        var entries = new List<string>
        {
            "# Lemur conversion: per-title realm-name flavorization.",
            "# Shows each state's Azgaar formName prefix, following the holder's current government.",
            "",
        };
        var loc = new List<string> { "l_english:" };

        int kingdomCount = 0, duchyCount = 0;

        foreach (var kingdom in map.Kingdoms)
        {
            statesById.TryGetValue(kingdom.Id, out var state);
            if (Emit(entries, loc, kingdom.Ck3_Id(), "kingdom", kingdom.Government, state?.formName))
                kingdomCount++;
        }

        foreach (var duchy in map.Duchies!.Where(d => d.IsAbsorbed))
        {
            statesById.TryGetValue(duchy.AzgaarStateId, out var state);
            if (Emit(entries, loc, duchy.Ck3_Id(), "duchy", duchy.Government, state?.formName))
                duchyCount++;
        }

        var dir = Helper.GetPath(outputDirectory, "common", "flavorization");
        Directory.CreateDirectory(dir);
        await File.WriteAllLinesAsync(Helper.GetPath(dir, "00_lemur_flavour.txt"), entries, Helper.Utf8Bom);

        var locDir = Helper.GetPath(outputDirectory, "localization", "english");
        Directory.CreateDirectory(locDir);
        await File.WriteAllLinesAsync(Helper.GetPath(locDir, "lemur_flavour_l_english.yml"), loc, Helper.Utf8Bom);

        Logger.Info($"Wrote 00_lemur_flavour.txt ({kingdomCount} kingdoms + {duchyCount} absorbed duchies)");
    }

    /// <summary>
    /// Appends one flavorization entry + matching loc line. Returns false (emits nothing) when the
    /// state has no formName — there is no prefix word to show, so CK3's default name stands.
    /// </summary>
    private static bool Emit(List<string> entries, List<string> loc, string titleId, string tier,
        Governments.Ck3Government? government, string? formName)
    {
        if (string.IsNullOrWhiteSpace(formName) || government == null)
            return false;

        var key = $"lemur_flavour_{titleId}";
        var governments = string.Join(" ", government.KeyWithFallbacks());

        entries.Add($"{key} = {{");
        entries.Add("\ttype = title");
        entries.Add($"\ttier = {tier}");
        entries.Add($"\tpriority = {Priority}");
        entries.Add($"\ttitles = {{ {titleId} }}");
        entries.Add($"\tgovernments = {{ {governments} }}");
        entries.Add("}");

        loc.Add($" {key}: \"{formName}\"");
        return true;
    }
}
