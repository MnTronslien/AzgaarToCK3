using System.Text.RegularExpressions;

namespace Converter.Lemur.Writers;

public static class ModDescriptorWriter
{
    public static async Task Write(string modName, string modsDirectory, string outputDirectory)
    {
        // descriptor.mod goes inside the mod folder (CK3 reads it from there at runtime).
        var descriptorPath = Helper.GetPath(outputDirectory, "descriptor.mod");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(descriptorPath, BuildDescriptorContent(modName, includePath: false), Helper.Utf8Bom);

        // The launcher .mod in the Paradox mods directory must point at the real mod folder.
        // When --output-dir redirects output to a temp/benchmark path, skip this write so we
        // don't break the user's live CK3 setup.
        if (Settings.OutputDirectoryOverride != null)
        {
            Logger.Info($"--output-dir active — skipping launcher {modName}.mod (live CK3 mod untouched)");
            Logger.Info("Wrote descriptor.mod");
            return;
        }

        var launcherPath = Helper.GetPath(modsDirectory, $"{modName}.mod");
        var version = NextVersion(launcherPath);
        var launcherContent = BuildDescriptorContent(modName, includePath: true, outputDirectory: outputDirectory, version: version);
        Directory.CreateDirectory(modsDirectory);
        await File.WriteAllTextAsync(launcherPath, launcherContent, Helper.Utf8Bom);

        Logger.Info($"Wrote descriptor.mod and {modName}.mod (mod v{version}, game {SettingsManager.Ck3SupportedVersion})");
    }

    /// <summary>
    /// Reads the current mod version from the launcher .mod file and returns the next one.
    /// Returns "1.0" if the file does not exist or has no parseable version.
    /// </summary>
    private static string NextVersion(string launcherPath)
    {
        if (!File.Exists(launcherPath))
            return "1.0";

        var content = File.ReadAllText(launcherPath);
        var match = Regex.Match(content, @"version=""(\d+)\.(\d+)""");
        if (!match.Success)
            return "1.0";

        int major = int.Parse(match.Groups[1].Value);
        int minor = int.Parse(match.Groups[2].Value);
        return $"{major}.{minor + 1}";
    }

    private static string BuildDescriptorContent(
        string modName,
        bool includePath,
        string? outputDirectory = null,
        string version = "1.0")
    {
        var lines = new List<string>
        {
            $"version=\"{version}\"",
            "tags={",
            "\t\"Total Conversion\"",
            "}",
            $"name=\"{modName}\"",
            $"supported_version=\"{SettingsManager.Ck3SupportedVersion}\"",
        };

        if (includePath && outputDirectory != null)
            lines.Add($"path=\"{outputDirectory.Replace('\\', '/')}\"");

        return string.Join("\n", lines) + "\n";
    }
}
