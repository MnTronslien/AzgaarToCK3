using System.Reflection;

namespace Converter.Lemur.Writers;

/// <summary>
/// Ships a copy of CK3's <c>gui/frontend_main.gui</c> with the main-menu portrait widget hidden
/// (<c>visible = no</c>) — that 3D portrait render crashes the main menu on a generated map.
/// The .gui is a static embedded asset pinned to a CK3 version; re-sync on front-end updates per the
/// bundled frontend_main.info (shipped alongside; CK3 ignores .info files).
/// See bugs/BUG_tcs-breakaway-bootcrash.md.
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
