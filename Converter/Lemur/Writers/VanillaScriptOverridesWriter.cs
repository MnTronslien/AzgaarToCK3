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
        var onActionDir = Helper.GetPath(outputDirectory, "common", "on_action");
        Directory.CreateDirectory(eventsDir);
        Directory.CreateDirectory(onActionDir);

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

        // common/on_action/game_start.txt — replace vanilla entirely.
        // Vanilla on_game_start references many specific titles (c_chandax,
        // c_byzantion, c_tourraine, e_byzantium, h_roman_empire,
        // h_eastern_roman_empire, k_magyar, ...) which TCS removes via
        // replace_path="common/landed_titles" + history/titles. Looking up a
        // non-existent title in 1.19 yields a null Landed_title scope; the
        // subsequent set_important_location / context-switch effects crash
        // the engine (was silent script error on 1.18). Empty override skips
        // the entire vanilla on_game_start; converted-world-specific effects
        // can be added back as needs surface.
        await File.WriteAllTextAsync(
            Helper.GetPath(onActionDir, "game_start.txt"),
            "# Override: vanilla on_game_start references titles that don't exist\n" +
            "# in converted worlds (TCS removes vanilla titles). On CK3 1.19+ the\n" +
            "# null-scope effects crash; on 1.18 they were silent script errors.\n" +
            "# Empty for now — add converted-world setup here as needs surface.\n" +
            "# See bugs/BUG_ck3-1.19-compat.md.\n" +
            "on_game_start = {\n" +
            "\teffect = {\n" +
            "\t}\n" +
            "}\n",
            Helper.Utf8Bom);

        Logger.Info("Wrote vanilla script overrides (events/easteregg_events.txt, common/on_action/game_start.txt)");
    }
}
