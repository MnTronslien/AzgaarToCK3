namespace Converter;

public static class ModManager
{
    public static async Task CreateMod()
    {
        // Ensure the mod output folder exists. The Lemur writers produce a complete, self-contained
        // mod, so this no longer seeds the folder from TCS.
        Directory.CreateDirectory(Helper.GetPath(Settings.Instance.ModsDirectory, Settings.Instance.ModName));
        await Task.CompletedTask;
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
