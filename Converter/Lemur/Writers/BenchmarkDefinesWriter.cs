using Converter.Lemur;
using L = Converter.Lemur.Entities;

namespace Converter.Lemur.Writers;

public static class BenchmarkDefinesWriter
{
    public static async Task Write(L.Map map, string outputDirectory)
    {
        // The "-benchmark" launch flag boots straight into the map and observes the holder of
        // the title named by NGame.BENCHMARK_OBSERVE_CHARACTER. Vanilla ships k_england, which
        // does not exist in a generated world, so the benchmark has no character to observe.
        // Point it at the first generated kingdom instead.
        var kingdom = map.Kingdoms?.FirstOrDefault();
        if (kingdom is null)
        {
            Logger.Warning("No kingdoms generated — skipping BENCHMARK_OBSERVE_CHARACTER define.");
            return;
        }

        var titleId = kingdom.Ck3_Id();

        var definesDir = Helper.GetPath(outputDirectory, "common", "defines");
        Directory.CreateDirectory(definesDir);

        // Partial NGame block: overrides only BENCHMARK_OBSERVE_CHARACTER and leaves every other
        // vanilla NGame define intact (common/defines is additive, not replace_path'd). The zz_
        // prefix keeps this last in load order so our value wins.
        var content =
            "NGame = {\n" +
            $"\tBENCHMARK_OBSERVE_CHARACTER = {titleId}\n" +
            "}\n";

        var path = Helper.GetPath(definesDir, "zz_benchmark_defines.txt");
        await File.WriteAllTextAsync(path, content, Helper.Utf8Bom);

        Logger.Info($"Wrote zz_benchmark_defines.txt (BENCHMARK_OBSERVE_CHARACTER = {titleId}, kingdom '{kingdom.Name}')");
    }
}
