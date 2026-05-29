using Converter.Lemur.Governments;
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
                    sb.AppendLine(TitleEntry(kingdom.Ck3_Id(), kingdom.Holder.Id, liege: null));

                foreach (var duchy in kingdom.Duchies)
                {
                    if (duchy.Holder != null)
                        sb.Append(DuchyEntry(duchy));

                    // Always write county liege — even without a holder.
                    // CK3 will auto-spawn a count and route them to the correct liege.
                    // Without this, CK3 walks the de jure chain and assigns unspecified
                    // counties to the first titled holder it finds (often the wrong king).
                    foreach (var county in duchy.Counties)
                        sb.AppendLine(TitleEntry(
                            county.Ck3_Id(),
                            county.Holder?.Id,
                            county.DeFactoLiege!.Ck3_Id(),
                            development: county.GetDevelopmentLevel()));
                }
            }
        }

        var path = Helper.GetPath(outputDirectory, "history", "titles", "00_lemur_titles.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, sb.ToString(), Helper.Utf8Bom);
        Logger.Info($"Wrote 00_lemur_titles.txt");
    }

    private static string TitleEntry(string titleId, string? holderId, string? liege, int? development = null)
    {
        var holderClause = holderId != null ? $" holder = {holderId}" : "";
        var liegeClause = liege != null ? $" liege = {liege}" : "";
        var devClause = development.HasValue ? $" change_development_level = {development.Value}" : "";
        return $"{titleId} = {{ {StartDate} = {{{holderClause}{liegeClause}{devClause} }} }}";
    }

    /// <summary>
    /// Duchy history entry. Same single-line shape as <see cref="TitleEntry"/> when the duchy
    /// has no resolved government, or when the resolved government is a base-game one (plain
    /// <c>government = X</c> clause). DLC-gated governments switch to a multi-line block
    /// that emits a <c>has_dlc_feature</c> conditional so the Jomini engine picks the real
    /// government or its fallback at game-start. Pattern modelled on vanilla character history
    /// dispatch (e.g. <c>history/characters/cuman.txt</c>).
    /// </summary>
    private static string DuchyEntry(L.Duchy duchy)
    {
        var titleId = duchy.Ck3_Id();
        var holderId = duchy.Holder!.Id;
        var liege = duchy.DeFactoLiege?.Ck3_Id();
        var liegeClause = liege != null ? $" liege = {liege}" : "";
        var gov = duchy.Government;

        if (gov is null)
            return $"{titleId} = {{ {StartDate} = {{ holder = {holderId}{liegeClause} }} }}" + Environment.NewLine;

        if (gov.DlcFeature is null)
            return $"{titleId} = {{ {StartDate} = {{ holder = {holderId}{liegeClause} government = {gov.Key} }} }}" + Environment.NewLine;

        // DLC-gated: emit a conditional so the engine picks at game-start.
        var fallbackKey = gov.Fallback!.Key;
        var nl = Environment.NewLine;
        return
            $"{titleId} = {{{nl}" +
            $"\t{StartDate} = {{{nl}" +
            $"\t\tholder = {holderId}{nl}" +
            (liege != null ? $"\t\tliege = {liege}{nl}" : "") +
            $"\t\teffect = {{{nl}" +
            $"\t\t\tif = {{{nl}" +
            $"\t\t\t\tlimit = {{ has_dlc_feature = {gov.DlcFeature} }}{nl}" +
            $"\t\t\t\tholder ?= {{ change_government = {gov.Key} }}{nl}" +
            $"\t\t\t}}{nl}" +
            $"\t\t\telse = {{{nl}" +
            $"\t\t\t\tholder ?= {{ change_government = {fallbackKey} }}{nl}" +
            $"\t\t\t}}{nl}" +
            $"\t\t}}{nl}" +
            $"\t}}{nl}" +
            $"}}{nl}";
    }
}
