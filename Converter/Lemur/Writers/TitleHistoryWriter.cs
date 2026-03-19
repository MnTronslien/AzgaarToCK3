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
                        sb.AppendLine(TitleEntry(duchy.Ck3_Id(), duchy.Holder.Id, duchy.DeFactoLiege?.Ck3_Id()));

                    foreach (var county in duchy.Counties)
                    {
                        if (county.Holder != null)
                            sb.AppendLine(TitleEntry(county.Ck3_Id(), county.Holder.Id, county.DeFactoLiege!.Ck3_Id()));
                    }
                }
            }
        }

        var path = Helper.GetPath(outputDirectory, "history", "titles", "00_lemur_titles.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, sb.ToString(), Helper.Utf8Bom);
        Logger.Info($"Wrote 00_lemur_titles.txt");
    }

    private static string TitleEntry(string titleId, string holderId, string? liege)
    {
        var liegeClause = liege != null ? $" liege = {liege}" : "";
        return $"{titleId} = {{ {StartDate} = {{ holder = {holderId}{liegeClause} }} }}";
    }
}
