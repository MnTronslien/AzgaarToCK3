using System.Text.Json;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace Converter;

public enum LogLevel { Verbose = 0, Debug = 1, Info = 2, Warning = 3, Error = 4 }

public class Settings
{
    public required string ModsDirectory { get; init; }
    public required string Ck3Directory { get; init; }
    public required string TotalConversionSandboxPath { get; init; }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public string InputJsonPath { get; set; }
    public string InputGeojsonPath { get; set; }
    public string InputRiversGeojsonPath { get; set; }
    public string ModName { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public bool? ShouldOverride { get; set; } = null;
    public bool OnlyCounts { get; set; } = false;

    [JsonIgnore]
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public static Settings Instance { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    [JsonIgnore]
    public static string OutputDirectory => Helper.GetPath(Instance.ModsDirectory, Instance.ModName);

    public LogLevel LogLevel { get; set; } = LogLevel.Info;
    public bool GenerateDebugImages { get; set; } = true;
    /// <summary>
    /// Wipe the mod output directory before each conversion to prevent stale file bleed.
    /// Disable with --no-wipe if you intentionally want incremental output.
    /// </summary>
    public bool AutoWipeOutput { get; set; } = true;

    // This is based on guestimate observations form CK3
    // Sparsley populated areas often have fewer baronies per county than densely populated areas

    /// <summary>
    /// High population threshold. If a duchy's total population exceeds this value,
    /// the <see cref="MinCounties"/> is used, resulting in fewer but larger counties.
    /// </summary>
    /// <value>The high population threshold, default is 13000.</value>
    public int HighPopulationThreshold { get; set; } = 13000;

    /// <summary>
    /// Low population threshold. If a duchy's total population is below this value,
    /// the <see cref="MaxCounties"/> is used, resulting in smaller counties with fewer baronies.
    /// </summary>
    /// <value>The low population threshold, default is 2000.</value>
    public int LowPopulationThreshold { get; set; } = 2000;

    /// <summary>
    /// Gets or sets the minimum number of counties. This is the minimum division of baronies within a duchy,
    /// used when the duchy's population exceeds the high population threshold.
    /// </summary>
    /// <value>The minimum number of counties, default is 2.</value>
    public int MinCounties { get; set; } = 2;

    /// <summary>
    /// Gets or sets the maximum number of counties. This is the maximum division of baronies within a duchy,
    /// used when the duchy's population is below the low population threshold.
    /// </summary>
    /// <value>The maximum number of counties, default is 5.</value>
    public int MaxCounties { get; set; } = 5;
    /// <summary>
    /// If true then de jure empires will form based on culture.
    /// If false then de jure empires will form based on religion.
    /// </summary>
    public bool EmpireFromCulture { get;  set; } = true;
    /// <summary>
    /// The minimum number of duchies per kingdom. 
    /// Kingdoms below this number will be merged into adjacent larger kingdoms in same empire.
    /// </summary>
    public int MinimumDuchiesPerKingdom { get;  set; } = 4;
    /// <summary>
    /// The minimum number of kingdoms per empire.
    /// Empires below this number will be merged into adjacent larger empires.
    /// </summary>
    public int MinimumKingdomsPerEmpire { get; set; } = 3;

    /// <summary>
    /// Enable river generation from Azgaar data.
    /// </summary>
    public bool EnableRivers { get; set; } = true;

    /// <summary>
    /// Discharge threshold for major rivers. Rivers with discharge >= this value are considered major/navigable.
    /// For testing purposes, set this very high (e.g. 999999) to treat all rivers as minor.
    /// </summary>
    public float MajorRiverThreshold { get; set; } = 999999f;  // Very high for testing - all rivers will be minor

    /// <summary>
    /// Target area per sea zone (Azgaar cell-area units). Zone grows until it hits this.
    /// </summary>
    public int SeaZoneTargetArea { get; set; } = 50000;

    /// <summary>
    /// Minimum area for a sea zone. Undersized zones merge or become impassable.
    /// </summary>
    public int SeaZoneMinimumArea { get; set; } = 5000;

    /// <summary>
    /// Auto-detect newer .json/.geojson files in the directory and prompt to use them.
    /// If false, always uses the paths specified in InputJsonPath, InputGeojsonPath, and InputRiversGeojsonPath.
    /// </summary>
    public bool AutoDetectInputs { get; set; } = false;

    /// <summary>
    /// Directory to scan for input files (.json, .geojson, rivers .geojson).
    /// When set, the converter auto-resolves the latest matching files from this directory.
    /// Individual --json/--geojson/--rivers-geojson flags always take precedence.
    /// Null or empty means not set; individual paths are used as-is.
    /// </summary>
    public string? InputDirectory { get; set; } = null;

    /// <summary>
    /// Number of far sea zone strips drawn behind the map to cover corner pixels.
    /// These prevent black (undefined) pixels that crash CK3's map generator.
    /// </summary>
    public int FarSeaZoneCount { get; set; } = 8;

    /// <summary>
    /// Optional path to the Azgaar SVG export. When set, FlatmapWriter renders
    /// the SVG as flatmap.dds. If null or the file is not found, a biome-colored
    /// fallback image is generated from cell data instead.
    /// </summary>
    public string? AzgaarSvgPath { get; set; } = null;

    /// <summary>
    /// Global seed for all non-deterministic decisions in the converter (doctrine/tenet selection,
    /// colour assignment, any future randomised steps). Null = fresh random seed each run.
    /// Set once at pipeline start; logged so any run can be reproduced with --seed.
    /// </summary>
    public int? Seed { get; set; } = null;

    /// <summary>Number of tenets per faith (1–5). Default: 3.</summary>
    public int TenetCount { get; set; } = 3;

    /// <summary>
    /// Probability (0.0–1.0) that a child faith mutates each doctrine/tenet slot
    /// away from its parent's value. 0 = identical to parent, 1 = fully random.
    /// Default: 0.3 (30% chance to mutate each slot).
    /// </summary>
    public float DoctrineMutationRate { get; set; } = 0.3f;

    /// <summary>
    /// Per-writer on/off switches. All default to true (current behaviour unchanged).
    /// Set individual flags to false in settings.json to skip specific writers during
    /// bisection testing.
    /// </summary>
    public WriterFlags Writers { get; set; } = new();

    public override string ToString()
    {
        var lines = new List<string>();
        foreach (var property in Instance.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            lines.Add($"{property.Name,-30}: {property.GetValue(Instance)}");
        }

        return string.Join('\n', lines);
    }
}

public class WriterFlags
{
    public bool DefinitionCsv { get; set; } = true;
    public bool DefaultMap { get; set; } = true;
    public bool Adjacencies { get; set; } = true;
    public bool MapStaticFiles { get; set; } = true;
    public bool GeographicalRegions { get; set; } = true;
    public bool LandedTitles { get; set; } = true;
    public bool ProvinceTerrain { get; set; } = true;
    public bool MapDefines { get; set; } = true;
    public bool Religion { get; set; } = true;
    public bool Faiths { get; set; } = true;
    public bool Cultures { get; set; } = true;
    public bool TerrainMasks { get; set; } = true;
    public bool Flatmap { get; set; } = true;
    public bool Locators { get; set; } = true;
    public bool Characters { get; set; } = true;
    public bool TitleHistory { get; set; } = true;
    public bool ProvinceHistory { get; set; } = true;
    public bool Heightmap { get; set; } = true;
}

[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(WriterFlags))]
[JsonSourceGenerationOptions(WriteIndented = true, AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
public partial class SettingsJsonContext : JsonSerializerContext { }

public static class SettingsManager
{
    private static readonly string settingsFileName = Helper.GetPath(ExecutablePath, "settings.json");
    private static readonly string defaultModsDirectory = Helper.GetPath(MyDocuments, "Paradox Interactive", "Crusader Kings III", "mod");
    private static string MyDocuments => Helper.GetPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    public static string ExecutablePath => Helper.GetPath(Directory.GetParent(Environment.ProcessPath!)!.FullName);

    private static string GetSteamLibraryFoldersPath()
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            throw new Exception("Only x64 systems are supported.");
        }

        if (OperatingSystem.IsWindows())
        {
            var steamPath = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Wow6432Node\Valve\Steam")
                ?.GetValue("InstallPath") as string
                ?? throw new Exception("Could not find steam InstallPath");

            return Helper.GetPath(steamPath, "steamapps", "libraryfolders.vdf");
        }
        else if (OperatingSystem.IsMacOS())
        {
            var steamPath = Helper.GetPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Steam");
            return Helper.GetPath(steamPath, "steamapps", "libraryfolders.vdf");
        }
        else
        {
            throw new Exception("Operating System not supported");
        }
    }

    private static string GetGameDirectory()
    {
        var libraries = File.ReadAllText(GetSteamLibraryFoldersPath());
        var pathRegex = new Regex("\"path\"\\s*\"(.+)\"");
        var paths = pathRegex.Matches(libraries).Select(n => n.Groups[1].Value);

        var ck3Directories = paths.Select(n => Helper.GetPath(n, "steamapps", "common", "Crusader Kings III", "game")).Where(Directory.Exists).ToArray();
        if (ck3Directories.Length > 1)
        {
            Debugger.Break();
            throw new Exception("Multiple game directories found.");
        }
        else if (ck3Directories.Length == 0)
        {
            Debugger.Break();
            throw new Exception("No game directories found.");
        }

        return ck3Directories[0];
    }
    private static string GetTotalConversionSandboxDirectory()
    {
        var libraries = File.ReadAllText(GetSteamLibraryFoldersPath());
        var pathRegex = new Regex("\"path\"\\s*\"(.+)\"");
        var paths = pathRegex.Matches(libraries).Select(n => n.Groups[1].Value);

        var ck3Directories = paths.Select(n => Helper.GetPath(n, "steamapps", "workshop", "content", "1158310", "2524797018")).Where(Directory.Exists).ToArray();
        if (ck3Directories.Length > 1)
        {
            Debugger.Break();
            throw new Exception("Multiple mod directories found.");
        }
        else if (ck3Directories.Length == 0)
        {
            Debugger.Break();
            throw new Exception("No mod directories found.");
        }

        return ck3Directories[0];
    }

    public static void Configure()
    {
        System.Globalization.CultureInfo customCulture = (System.Globalization.CultureInfo)System.Threading.Thread.CurrentThread.CurrentCulture.Clone();
        customCulture.NumberFormat.NumberDecimalSeparator = ".";
        System.Threading.Thread.CurrentThread.CurrentCulture = customCulture;
    }
    public static bool TryLoad()
    {
        string settings = "";
        try
        {
            if (!File.Exists(settingsFileName))
            {
                return false;
            }

            settings = File.ReadAllText(settingsFileName);
            Settings.Instance = JsonSerializer.Deserialize(settings, SettingsJsonContext.Default.Settings)!;
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to load settings: {e.Message} {e.StackTrace}");
            Console.WriteLine($"Content was: {settings}");
            return false;
        }
    }
    public static void CreateDefault()
    {
        Settings.Instance = new Settings
        {
            ModsDirectory = defaultModsDirectory,
            TotalConversionSandboxPath = GetTotalConversionSandboxDirectory(),
            Ck3Directory = GetGameDirectory(),
        };

        Save();
    }

    public static void Save()
    {
        File.WriteAllText(settingsFileName, JsonSerializer.Serialize(Settings.Instance, SettingsJsonContext.Default.Settings));
    }
}

