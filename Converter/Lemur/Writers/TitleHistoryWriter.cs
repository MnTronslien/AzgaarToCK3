using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class TitleHistoryWriter
{
    private const string StartDate = "1066.1.1";

    public static async Task Write(L.Map map, string outputDirectory)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var empire in map.Empires!)
        {
            // Emperors are never assigned (de jure only)

            foreach (var kingdom in empire.Kingdoms)
            {
                if (kingdom.Holder != null)
                    sb.AppendLine(TitleEntry(kingdom.Ck3_Id(), kingdom.Holder.Id, liege: null, governmentKey: kingdom.Government?.Key));

                foreach (var duchy in kingdom.Duchies)
                {
                    if (duchy.Holder != null)
                        sb.AppendLine(TitleEntry(duchy.Ck3_Id(), duchy.Holder.Id, duchy.DeFactoLiege?.Ck3_Id(), governmentKey: duchy.Government?.Key));

                    // Always write county liege — even without a holder.
                    // CK3 will auto-spawn a count and route them to the correct liege.
                    // Without this, CK3 walks the de jure chain and assigns unspecified
                    // counties to the first titled holder it finds (often the wrong king).
                    foreach (var county in duchy.Counties)
                        sb.AppendLine(TitleEntry(
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
    private static string TitleEntry(string titleId, string? holderId, string? liege, string? governmentKey = null, int? development = null)
    {
        var holderClause = holderId != null ? $" holder = {holderId}" : "";
        var liegeClause = liege != null ? $" liege = {liege}" : "";
        var govClause = governmentKey != null ? $" government = {governmentKey}" : "";
        var devClause = development.HasValue ? $" change_development_level = {development.Value}" : "";
        return $"{titleId} = {{ {StartDate} = {{{holderClause}{liegeClause}{govClause}{devClause} }} }}";
    }
}
