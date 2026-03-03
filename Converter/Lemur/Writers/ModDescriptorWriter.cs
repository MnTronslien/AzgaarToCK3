namespace Converter.Lemur.Writers;

public static class ModDescriptorWriter
{
    public static async Task Write(string modName, string modsDirectory, string outputDirectory)
    {
        var descriptorContent = BuildDescriptorContent(modName, includePath: false);
        var launcherContent = BuildDescriptorContent(modName, includePath: true, outputDirectory: outputDirectory);

        // Write descriptor.mod inside the mod folder
        var descriptorPath = Helper.GetPath(outputDirectory, "descriptor.mod");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(descriptorPath, descriptorContent, Helper.Utf8Bom);

        // Write <modname>.mod in the parent mods directory (launcher reads this)
        var launcherPath = Helper.GetPath(modsDirectory, $"{modName}.mod");
        Directory.CreateDirectory(modsDirectory);
        await File.WriteAllTextAsync(launcherPath, launcherContent, Helper.Utf8Bom);

        Logger.Info($"Wrote descriptor.mod and {modName}.mod");
    }

    private static string BuildDescriptorContent(string modName, bool includePath, string? outputDirectory = null)
    {
        var lines = new List<string>
        {
            $"version=\"1.0\"",
            $"tags={{",
            $"\t\"Total Conversion\"",
            $"}}",
            $"name=\"{modName}\"",
            $"supported_version=\"1.12.*\"",
            $"replace_path=\"map_data\"",
            $"replace_path=\"common/landed_titles\"",
            $"replace_path=\"common/province_terrain\"",
            $"replace_path=\"common/defines\"",
            $"replace_path=\"history/titles\"",
            $"replace_path=\"history/characters\"",
            $"replace_path=\"history/provinces\"",
            $"replace_path=\"history/province_mappings\"",
            $"replace_path=\"common/bookmarks\"",
            $"replace_path=\"common/bookmark_portraits\"",
            // NOTE: gfx/map/map_object_data was temporarily removed while Lemur lacked
            // locator files. Without this replace_path, TCS's locators loaded instead
            // (wrong pixel coords but no crash). Now that LocatorWriter generates proper
            // building/combat/player_stack/siege locator files for our map, we must
            // block TCS's locators so CK3 uses our correct coordinates.
            $"replace_path=\"gfx/map/map_object_data\"",
        };

        if (includePath && outputDirectory != null)
        {
            // Use forward slashes — CK3 launcher expects this
            var normalizedPath = outputDirectory.Replace('\\', '/');
            lines.Add($"path=\"{normalizedPath}\"");
        }

        return string.Join("\n", lines) + "\n";
    }
}
