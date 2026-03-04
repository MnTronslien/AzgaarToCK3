namespace Converter.Lemur.Writers;

/// <summary>
/// Writes empty stub files for every vanilla CK3 province history file.
///
/// CK3 loads vanilla history/provinces/ files (e.g. k_england.txt) which set
/// hardcoded culture/religion that conflict with our custom provinces. Because our
/// descriptor.mod has replace_path="history/provinces", empty files here suppress
/// the vanilla entries while our ProvinceHistoryWriter files take precedence.
/// </summary>
public static class VanillaPassthroughWriter
{
    public static async Task Write(string ck3Directory, string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing vanilla province history stubs");

        // ck3Directory is the CK3 install root; game files live under "game"
        var sourceDir = Helper.GetPath(ck3Directory, "game", "history", "provinces");
        var targetDir = Helper.GetPath(outputDirectory, "history", "provinces");

        if (!Directory.Exists(sourceDir))
        {
            Logger.Warning($"Vanilla province history directory not found: {sourceDir} — skipping stubs");
            return;
        }

        Directory.CreateDirectory(targetDir);

        var files = Directory.GetFiles(sourceDir, "*.txt", SearchOption.TopDirectoryOnly);
        var tasks = files.Select(file =>
        {
            var targetPath = Path.Combine(targetDir, Path.GetFileName(file));
            return File.WriteAllTextAsync(targetPath, string.Empty, Helper.Utf8Bom);
        });
        await Task.WhenAll(tasks);

        Logger.Info($"Wrote {files.Length} empty province history stubs to history/provinces/");
    }
}
