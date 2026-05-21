using Converter.Lemur;

namespace Converter.Lemur.Writers;

/// <summary>
/// Writes <c>events/easteregg_events.txt</c>: no-op overrides for vanilla events
/// that crash on converted worlds. Not redundant with <see cref="LandlessTitleStubsWriter"/>
/// — that fix targets missing-title scopes, this targets missing-character-anchor
/// scopes (e.g. easteregg characters that exist but have no faith/realm).
/// Each override sets <c>trigger = { always = no }</c>.
/// See <c>bugs/BUG_ck3-1.19-compat.md</c>.
/// </summary>
public static class VanillaEventOverridesWriter
{
    public static async Task Write(string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing vanilla event overrides");

        var eventsDir = Helper.GetPath(outputDirectory, "events");
        Directory.CreateDirectory(eventsDir);

        await File.WriteAllTextAsync(
            Helper.GetPath(eventsDir, "easteregg_events.txt"),
            "namespace = easteregg_event\n" +
            "\n" +
            "# Override: vanilla immediate crashes on converted worlds because\n" +
            "# easteregg_charna_frostwhisper has no faith/realm anchor. See\n" +
            "# bugs/BUG_ck3-1.19-compat.md.\n" +
            "easteregg_event.0001 = {\n" +
            "\thidden = yes\n" +
            "\tscope = none\n" +
            "\ttrigger = { always = no }\n" +
            "\timmediate = { }\n" +
            "}\n",
            Helper.Utf8Bom);

        Logger.Info("Wrote 1 event override to events/easteregg_events.txt");
    }
}
