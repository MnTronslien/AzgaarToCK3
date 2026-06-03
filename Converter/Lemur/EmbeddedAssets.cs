using System.Reflection;

namespace Converter.Lemur;

/// <summary>
/// Extracts files embedded in the Converter assembly (see Converter.csproj EmbeddedResource items)
/// to disk, so static mod assets ship without a TCS or CK3 install at build time.
/// </summary>
internal static class EmbeddedAssets
{
    public static async Task ExtractAsync(string resourceName, string destPath)
    {
        var asm = Assembly.GetExecutingAssembly();
        await using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found — check Converter.csproj EmbeddedResource items.");
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        await using var dest = File.Create(destPath);
        await stream.CopyToAsync(dest);
    }
}
