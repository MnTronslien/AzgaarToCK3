namespace Converter.Lemur.Writers;

public static class ReligionWriter
{
    public static async Task Write(string ck3Directory, string outputDirectory)
    {
        // ck3Directory is the CK3 root (e.g. .../Crusader Kings III); game files live under "game"
        var sourceDir = Path.Combine(ck3Directory, "game", "common", "religion");
        var destDir = Path.Combine(outputDirectory, "common", "religion");

        if (!Directory.Exists(sourceDir))
        {
            Logger.Warning($"ReligionWriter: source path does not exist, skipping: {sourceDir}");
            return;
        }

        int fileCount = 0;
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, sourceFile);
            var destFile = Path.Combine(destDir, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            // Use async read/write to stay consistent with the rest of the codebase
            var bytes = await File.ReadAllBytesAsync(sourceFile);
            await File.WriteAllBytesAsync(destFile, bytes);
            fileCount++;
        }

        Logger.Info($"Copied religion folder: {fileCount} files from '{sourceDir}'");
    }
}
