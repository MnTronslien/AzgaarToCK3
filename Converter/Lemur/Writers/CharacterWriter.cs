using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class CharacterWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var character in map.Characters)
        {
            sb.AppendLine($"{character.Id} = {{");
            if (character.Name != null)
                sb.AppendLine($"\tname = \"{character.Name}\"");
            sb.AppendLine($"\tculture = {character.Culture.CK3Key}");
            sb.AppendLine($"\treligion = {character.Faith.CK3Key}");
            sb.AppendLine($"\t{character.BirthYear}.1.1 = {{");
            sb.AppendLine($"\t\tbirth = \"{character.BirthYear}.1.1\"");
            sb.AppendLine($"\t}}");
            sb.AppendLine($"}}");
            sb.AppendLine();
        }

        var path = Helper.GetPath(outputDirectory, "history", "characters", "00_lemur_characters.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, sb.ToString(), Helper.Utf8Bom);
        Logger.Info($"Wrote 00_lemur_characters.txt ({map.Characters.Count} characters)");
    }
}
