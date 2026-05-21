using Converter.Lemur;

namespace Converter.Lemur.Writers;

/// <summary>
/// Writes <c>&lt;mod&gt;/events/easteregg_events.txt</c> with overrides for specific
/// vanilla events that crash on converted worlds.
///
/// Pattern: each problematic vanilla event is redefined with the same
/// <c>namespace.id</c> but <c>trigger = { always = no }</c>, so the event never fires.
/// Mod file precedence means our redefinition replaces vanilla's for that ID.
/// Other vanilla events in the same file (e.g. follow-up events in the chain)
/// remain at vanilla because we only redeclare the specific IDs we override.
///
/// Currently overrides:
/// <list type="bullet">
/// <item><c>easteregg_event.0001</c> — Charna &amp; Jakub duel. Vanilla immediate
/// calls <c>set_variable</c> on <c>easteregg_charna_frostwhisper</c>; on converted
/// worlds her scope is invalid for variables (no faith / realm anchor), and on
/// CK3 1.19+ the script error recurses into <c>EXCEPTION_GUARD_PAGE</c>.</item>
/// </list>
///
/// Extend as more event-driven crashes surface. See <c>bugs/BUG_ck3-1.19-compat.md</c>.
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
