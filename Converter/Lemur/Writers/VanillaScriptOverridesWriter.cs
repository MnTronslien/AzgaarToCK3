using Converter.Lemur;

namespace Converter.Lemur.Writers;

/// <summary>
/// Overrides specific vanilla CK3 scripts that crash on converted worlds.
/// Each override is targeted by namespace.id so we replace only the offending
/// entry, not the whole vanilla file. See bugs/BUG_ck3-1.19-compat.md for the
/// per-override rationale.
/// </summary>
public static class VanillaScriptOverridesWriter
{
    public static async Task Write(string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing vanilla script overrides");

        var eventsDir = Helper.GetPath(outputDirectory, "events");
        Directory.CreateDirectory(eventsDir);

        // easteregg_event.0001 — Charna & Jakub duel.
        // On CK3 1.19+ the vanilla immediate block runs set_variable on
        // easteregg_charna_frostwhisper, but on converted worlds her scope is
        // not valid for variables (no faith / realm anchoring). The script
        // error recurses into EXCEPTION_GUARD_PAGE at on_game_start.
        // Override with always-false trigger to skip the chain entirely.
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

        Logger.Info("Wrote vanilla script overrides (events/easteregg_events.txt)");
    }
}
