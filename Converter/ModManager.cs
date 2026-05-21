using Microsoft.VisualBasic.FileIO;

namespace Converter;

public static class ModManager
{
    public static async Task CreateMod()
    {
        // CK3's mod folder may not exist yet on a fresh CK3 install (or if the user
        // pointed ModsDirectory at a custom location). File.WriteAllTextAsync won't
        // create missing parents, so we make sure the directory exists first.
        Directory.CreateDirectory(Settings.Instance.ModsDirectory);

        var outsideDescriptor = $@"version=""1.0""
tags={{
	""Total Conversion""
}}
name=""{Settings.Instance.ModName}""
supported_version=""1.12.4""
path=""mod/{Settings.Instance.ModName}""";

        await File.WriteAllTextAsync(Helper.GetPath(Settings.Instance.ModsDirectory, $"{Settings.Instance.ModName}.mod"), outsideDescriptor);

        FileSystem.CopyDirectory(Settings.Instance.TotalConversionSandboxPath, Helper.GetPath(Settings.Instance.ModsDirectory, Settings.Instance.ModName), true);

        var insideDescriptor = $@"version=""1.0""
tags={{
	""Total Conversion""
}}
name=""{Settings.Instance.ModName}""
supported_version=""1.12.4""";
        await File.WriteAllTextAsync(Helper.GetPath(Settings.Instance.ModsDirectory, Settings.Instance.ModName, "descriptor.mod"), insideDescriptor);
    }
    public static bool DoesModExist()
    {
        return Directory.Exists(Helper.GetPath(Settings.Instance.ModsDirectory, Settings.Instance.ModName));
    }

    public static (string? jsonName, string? geojsonName, string? riversGeojsonName) FindLatestInputs(string? directory = null)
    {
        string? jsonName = null;
        string? geojsonName = null;
        string? riversGeojsonName = null;

        var scanDir = directory ?? SettingsManager.ExecutablePath;

        var filesToCheck = new DirectoryInfo(scanDir)
            .EnumerateFiles()
            .OrderByDescending(n => n.CreationTime)
            .Select(n => Path.Combine(scanDir, n.Name))
            .Where(n => Settings.Instance.InputJsonPath != n &&
                        Settings.Instance.InputGeojsonPath != n &&
                        Settings.Instance.InputRiversGeojsonPath != n);

        foreach (var f in filesToCheck)
        {
            if (f.EndsWith(".json"))
            {
                var p = Path.GetFileName(f);
                if (!p.EndsWith("settings.json") && !p.StartsWith("ConsoleUI"))
                {
                    jsonName = f;
                }
            }
            else if (f.EndsWith(".geojson"))
            {
                var fileName = Path.GetFileName(f).ToLower();
                // Check if it's a rivers geojson (contains "rivers" in the name)
                if (fileName.Contains("rivers") || fileName.Contains("river"))
                {
                    riversGeojsonName = f;
                }
                else
                {
                    geojsonName = f;
                }
            }

            if (jsonName is not null && geojsonName is not null && riversGeojsonName is not null)
            {
                break;
            }
        }

        return (jsonName, geojsonName, riversGeojsonName);
    }
}
