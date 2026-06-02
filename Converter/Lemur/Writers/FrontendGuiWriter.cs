using System.Reflection;

namespace Converter.Lemur.Writers;

/// <summary>
/// Ships a stripped copy of CK3's <c>gui/frontend_main.gui</c> into the mod.
///
/// CK3's main menu renders the default bookmark character's 3D portrait. On a fully generated,
/// TCS-free map that render faults with EXCEPTION_ACCESS_VIOLATION at the main menu — the game
/// crashes on load before a game can be started. Our override is vanilla's frontend with the
/// main-menu portrait widget and the challenge-character window set to <c>visible = no</c>, which
/// stops the portrait render and avoids the crash (the same fix TCS shipped). In-game portraits are
/// unaffected. See bugs/BUG_tcs-breakaway-bootcrash.md.
///
/// The .gui is a STATIC embedded asset pinned to a CK3 version — re-sync on front-end updates per
/// the bundled frontend_main.info. We ship that .info alongside it (CK3 ignores .info files) so the
/// rationale travels with the mod.
/// </summary>
public static class FrontendGuiWriter
{
    private const string GuiResource = "Converter.Lemur.Assets.frontend_main.gui";
    private const string InfoResource = "Converter.Lemur.Assets.frontend_main.info";

    public static async Task Write(string outputDirectory)
    {
        using var _ = OperationTimer.Start("Writing frontend GUI override");

        var guiDir = Helper.GetPath(outputDirectory, "gui");
        Directory.CreateDirectory(guiDir);

        await ExtractResource(GuiResource, Helper.GetPath(guiDir, "frontend_main.gui"));
        await ExtractResource(InfoResource, Helper.GetPath(guiDir, "frontend_main.info"));

        Logger.Info("Wrote gui/frontend_main.gui override (hides main-menu portrait — prevents TCS-free boot crash)");
    }

    private static async Task ExtractResource(string resourceName, string destPath)
    {
        var asm = Assembly.GetExecutingAssembly();
        await using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found — check Converter.csproj EmbeddedResource items.");
        await using var dest = File.Create(destPath);
        await stream.CopyToAsync(dest);
    }
}
