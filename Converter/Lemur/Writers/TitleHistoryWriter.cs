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
            if (empire.Holder != null)
                sb.AppendLine(TitleEntry(LandedTitlesWriter.ToCk3Id("e", empire.Name, empire.Id), empire.Holder.Id));

            foreach (var kingdom in empire.Kingdoms)
            {
                if (kingdom.Holder != null)
                    sb.AppendLine(TitleEntry(LandedTitlesWriter.ToCk3Id("k", kingdom.Name, kingdom.Id), kingdom.Holder.Id));

                foreach (var duchy in kingdom.Duchies)
                {
                    if (duchy.Holder != null)
                        sb.AppendLine(TitleEntry(LandedTitlesWriter.ToCk3Id("d", duchy.Name, duchy.Id), duchy.Holder.Id));

                    foreach (var county in duchy.Counties)
                    {
                        if (county.Holder != null)
                            sb.AppendLine(TitleEntry(LandedTitlesWriter.ToCk3Id("c", county.Name, county.Id), county.Holder.Id));
                    }
                }
            }
        }

        var path = Helper.GetPath(outputDirectory, "history", "titles", "00_lemur_titles.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, sb.ToString(), Helper.Utf8Bom);
        Logger.Info($"Wrote 00_lemur_titles.txt");
    }

    private static string TitleEntry(string titleId, string holderId) =>
        $"{titleId} = {{ {StartDate} = {{ holder = {holderId} }} }}";
}
