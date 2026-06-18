using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class TitleHistoryWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        var startDate = map.StartDate.ToString();
        var sb = new System.Text.StringBuilder();

        foreach (var empire in map.Empires!)
        {
            // Emperor: assigned only for diplomacy-driven empires (CharacterFactory's empire pass). Holderless
            // culture/religion shells emit nothing. Government inherited from the suzerain kingdom.
            if (empire.Holder != null)
                sb.AppendLine(TitleEntry(startDate, empire.Ck3_Id(), empire.Holder.Id, liege: null, governmentKey: empire.Government?.Key));

            foreach (var kingdom in empire.Kingdoms)
            {
                // Kingdom liege = its de facto liege (an empire) for a vassal king; null for independent kings.
                // Suppress a self-liege: the emperor's own (suzerain) kingdom is held by the same character as
                // its empire, so no liege line is needed — CK3 nests it under the higher title automatically.
                if (kingdom.Holder != null)
                {
                    var kLiege = kingdom.DeFactoLiege is { } kl && kl.Holder != kingdom.Holder ? kl.Ck3_Id() : null;
                    sb.AppendLine(TitleEntry(startDate, kingdom.Ck3_Id(), kingdom.Holder.Id, liege: kLiege, governmentKey: kingdom.Government?.Key));
                }

                foreach (var duchy in kingdom.Duchies)
                {
                    if (duchy.Holder != null)
                        sb.AppendLine(TitleEntry(startDate, duchy.Ck3_Id(), duchy.Holder.Id, duchy.DeFactoLiege?.Ck3_Id(), governmentKey: duchy.Government?.Key));

                    // Always write county liege — even without a holder.
                    // CK3 will auto-spawn a count and route them to the correct liege.
                    // Without this, CK3 walks the de jure chain and assigns unspecified
                    // counties to the first titled holder it finds (often the wrong king).
                    foreach (var county in duchy.Counties)
                        sb.AppendLine(TitleEntry(
                            startDate,
                            county.Ck3_Id(),
                            county.Holder?.Id,
                            county.DeFactoLiege!.Ck3_Id(),
                            governmentKey: null,
                            development: county.GetDevelopmentLevel()));
                }
            }
        }

        var path = Helper.GetPath(outputDirectory, "history", "titles", "00_lemur_titles.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, sb.ToString(), Helper.Utf8Bom);
        Logger.Info($"Wrote 00_lemur_titles.txt");
    }

    /// <summary>
    /// One uniform shape for every title-history entry: holder + liege + government + development,
    /// each optional. CK3 vanilla emits `government = X` plain at the title-history level for all
    /// governments, including DLC-gated keys (engine handles missing-DLC fallback to the
    /// corresponding base-game government — verified against vanilla `k_caspian_steppe.txt` /
    /// `e_japan.txt` for nomad and japan_feudal). Development levels are emitted on counties only
    /// (from the 1.3.0 feature/county-development feature).
    /// </summary>
    private static string TitleEntry(string startDate, string titleId, string? holderId, string? liege, string? governmentKey = null, int? development = null)
    {
        var holderClause = holderId != null ? $" holder = {holderId}" : "";
        var liegeClause = liege != null ? $" liege = {liege}" : "";
        var govClause = governmentKey != null ? $" government = {governmentKey}" : "";
        var devClause = development.HasValue ? $" change_development_level = {development.Value}" : "";
        return $"{titleId} = {{ {startDate} = {{{holderClause}{liegeClause}{govClause}{devClause} }} }}";
    }
}
