using System.Text.Json;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace Converter;

public enum LogLevel { Verbose = 0, Debug = 1, Info = 2, Warning = 3, Error = 4 }

public class Settings
{
    public required string ModsDirectory { get; init; }
    public required string Ck3Directory { get; init; }
    // Unused by the converter; kept nullable so old settings.json files still deserialize.
    public string? TotalConversionSandboxPath { get; init; }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public string InputJsonPath { get; set; }
    public string InputGeojsonPath { get; set; }
    public string InputRiversGeojsonPath { get; set; }
    public string ModName { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public bool OnlyCounts { get; set; } = false;

    [JsonIgnore]
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public static Settings Instance { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    [JsonIgnore]
    public static string? OutputDirectoryOverride { get; set; } = null;

    [JsonIgnore]
    public static string OutputDirectory => OutputDirectoryOverride ?? Helper.GetPath(Instance.ModsDirectory, Instance.ModName);

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
    /// Discharge threshold for major rivers. Rivers with discharge >= this value are processed
    /// as navigable river provinces (carved cells). Below this they only appear as minor river
    /// pixels on rivers.png. 2000 is the real-world sensible default verified on Showcase +
    /// Cerbois. Raise toward 999999 (treats all rivers as minor) if a map's mouth-width data
    /// triggers the "major rivers swallow cells" bug.
    /// </summary>
    public float MajorRiverThreshold { get; set; } = 2000f;

    /// <summary>
    /// Number of river cells per major river province segment.
    /// Each major river is divided into multiple MajorRiverProvince objects of this size.
    /// </summary>
    public int RiverProvinceCellCount { get; set; } = 2;

    /// <summary>
    /// Target area per sea zone (Azgaar cell-area units). Zone grows until it hits this.
    /// </summary>
    public int SeaZoneTargetArea { get; set; } = 25000;

    /// <summary>
    /// Minimum area for a sea zone. Undersized zones merge or become impassable.
    /// </summary>
    public int SeaZoneMinimumArea { get; set; } = 2500;

    // ── Straits (sea crossings → adjacencies.csv). See PLAN_straits.md. ──
    // All distances are in CK3 image-pixel units (8192 × 4096). Max-distance + self-separation
    // tuned on Oncyia (2026-06-17); clearance + ocean-area still starting points. Tune via the
    // TerrainLab --strait-map harness.

    /// <summary>
    /// Rule 1: two cells joined by an overland path shorter than this (accumulated cell-centroid pixel
    /// distance) get no strait — you can just walk around. Distinguishes a bay worth bridging from a
    /// shoreline. In PIXELS, not hops, so it is independent of Azgaar cell density (a 10k-cell and a
    /// 100k-cell map at the same resolution behave identically) — hence a single global value, not
    /// interpolated. Binary-tuned + maintainer-approved on Showcase (2026-06-18): 500px admits the
    /// genuine same-landmass bay crossings (Showcase 57→59, Oncyia 10→12). First crossing appears at
    /// the ~900px transition; 500 lets in the next valid bay.
    /// </summary>
    public double StraitMinimumSelfSeparation { get; set; } = 500;

    /// <summary>
    /// Rule 2: maximum crossing length (cell-centre to cell-centre), in image pixels. Also bounds how
    /// wide a strait may be. <c>null</c> (default) auto-scales it by map cell count via
    /// <see cref="Converter.Lemur.Straits.StraitKnobs.ResolveMaxDistance"/> — the one density-sensitive
    /// knob (see STRAITS_TUNING.md). Set a value to pin it explicitly (explicit always wins).
    /// </summary>
    public double? StraitMaxDistance { get; set; } = null;

    /// <summary>
    /// Rule 3: minimum spacing between two straits joining the same landmass pair (midpoint to
    /// midpoint), in image pixels. Shorter straits win; nearby parallel crossings are pruned.
    /// </summary>
    public double StraitMinimumClearance { get; set; } = 400;

    /// <summary>
    /// Water bodies whose summed cell area is at or above this are oceans (strait-able); smaller
    /// bodies are lakes (never crossed by a sea strait). Azgaar cell-area units. Azgaar's one-ocean
    /// limit means large seas are often mislabelled lakes, so we classify by size, not feature type.
    /// 2000 (maintainer-approved on Showcase 2026-06-18): reaches lake-island enclaves without bridging
    /// ponds — the MaxDistance/over-water/barony gates already block pond crossings, so this is low-risk.
    /// May warrant per-map tuning like MaxDistance; verify on other maps.
    /// </summary>
    public int StraitOceanMinimumArea { get; set; } = 2000;

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
    /// Weight multiplier applied to a candidate tenet whose baked themes overlap the faith's
    /// form-derived themes (see <see cref="Converter.Lemur.FormThemes"/>). 1.0 = no boost,
    /// higher = stronger pull toward on-theme tenets. Default: 4.
    /// </summary>
    public float FaithFormThemeBoost { get; set; } = 4f;

    /// <summary>
    /// Probability (0.0–1.0) that a child faith mutates each doctrine/tenet slot
    /// away from its parent's value. 0 = identical to parent, 1 = fully random.
    /// Default: 0.3 (30% chance to mutate each slot).
    /// </summary>
    public float DoctrineMutationRate { get; set; } = 0.3f;

    // ── Tech levels — culture innovations & eras. See PLAN_tech_levels.md. ──

    /// <summary>
    /// Baseline culture era for the whole world (an authorial choice, NOT derived from the start
    /// date). Per-culture development variance shifts individual cultures ±1 era around it. The
    /// baseline era unlocks at the start date; higher eras step forward by <see cref="EraStepYears"/>.
    /// </summary>
    public Converter.Lemur.Entities.CultureEra WorldTechLevel { get; set; } =
        Converter.Lemur.Entities.CultureEra.EarlyMedieval;

    /// <summary>Years between consecutive era unlocks (post-start era-year cadence). Default 150 (vanilla medieval cadence).</summary>
    public int EraStepYears { get; set; } = 150;

    /// <summary>"Good pacing" tail after late-medieval, used to derive END_DATE. Default 250 (vanilla 1453−1200, simplified).</summary>
    public int EndTailYears { get; set; } = 250;

    /// <summary>
    /// Fraction of cultures (by rank of average territory development) in each of the backward
    /// (bottom) and enlightened (top) bands. Bottom band demotes one era, top band promotes one,
    /// middle stays at <see cref="WorldTechLevel"/>. Default 0.2 (bottom/top fifth).
    /// </summary>
    public double TechVarianceBandFraction { get; set; } = 0.2;

    /// <summary>
    /// Tech-count gradient knob A: how many innovations a culture holds at its OWN frontier era
    /// (distance 0), as an absolute count independent of pool size — so the frontier stays this many
    /// even as the general pool grows (e.g. when region innovations are demoted into it). Default 3.
    /// </summary>
    public int TechFrontierCount { get; set; } = 3;

    /// <summary>
    /// Tech-count gradient knob B: additional fraction of the pool per era below the frontier.
    /// fill(d) = min(1, TechFrontierFraction + TechFillStep × d). Default 0.30.
    /// </summary>
    public double TechFillStep { get; set; } = 0.30;

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
    public bool Vegetation { get; set; } = true;
    public bool Characters { get; set; } = true;
    public bool TitleHistory { get; set; } = true;
    public bool ProvinceHistory { get; set; } = true;
    public bool Heightmap { get; set; } = true;
    public bool Bookmark { get; set; } = true;
    public bool Flavorization { get; set; } = true;
    /// <summary>Tech levels: culture innovations, start eras, era-year remap, END_DATE. See PLAN_tech_levels.md.</summary>
    public bool Tech { get; set; } = true;
    // Editor-oriented terrain output: rasterise the per-material editor masks from the splatmap
    // so the CK3 map editor opens onto a fully painted, smoothly-blended map. Default ON — the
    // splatmap is already computed for runtime, so painting the masks is near-free. Turn off to
    // emit blank masks (the 8-bit depth fix that lets the editor load at all is unconditional).
    public bool MapEditor { get; set; } = true;
}

/// <summary>
/// One candidate Total Conversion Sandbox install discovered in the Steam workshop folder.
/// Workshop IDs change when the mod is re-uploaded, so we identify TCS by reading each
/// <c>descriptor.mod</c>'s <c>name</c> field rather than trusting a hardcoded ID.
/// </summary>
public record TcsCandidate(
    string Path,
    string Name,
    string? Version,
    string WorkshopId,
    DateTime LastModified);

[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(WriterFlags))]
[JsonSourceGenerationOptions(WriteIndented = true, AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
public partial class SettingsJsonContext : JsonSerializerContext { }

public static class SettingsManager
{
    /// <summary>
    /// The CK3 game version this converter targets. Update this when a new major CK3 release drops.
    /// Written into every generated descriptor.mod / LemurTest.mod as supported_version.
    /// </summary>
    public const string Ck3SupportedVersion = "1.19.*";

    private static readonly string settingsFileName = Helper.GetPath(ExecutablePath, "settings.json");
    public static readonly string DefaultModsDirectory = Helper.GetPath(MyDocuments, "Paradox Interactive", "Crusader Kings III", "mod");
    private static string MyDocuments => Helper.GetPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    public static string ExecutablePath => Helper.GetPath(Directory.GetParent(Environment.ProcessPath!)!.FullName);
    public static string SettingsFilePath => settingsFileName;

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

    private static IEnumerable<string> GetSteamLibraryPaths()
    {
        var libraries = File.ReadAllText(GetSteamLibraryFoldersPath());
        var pathRegex = new Regex("\"path\"\\s*\"(.+)\"");
        return pathRegex.Matches(libraries).Select(n => n.Groups[1].Value);
    }

    /// <summary>
    /// Scan Steam libraries for a CK3 install. Returns the install root (the folder
    /// containing <c>game/</c>), or <c>null</c> if not found. Never throws.
    /// </summary>
    public static string? TryFindCk3InstallRoot()
    {
        try
        {
            var roots = GetSteamLibraryPaths()
                .Select(lib => Helper.GetPath(lib, "steamapps", "common", "Crusader Kings III"))
                .Where(root => Directory.Exists(Helper.GetPath(root, "game")))
                .ToArray();
            return roots.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Scan Steam workshop folders for Total Conversion Sandbox by reading each
    /// <c>descriptor.mod</c> and matching <c>name="Total Conversion Sandbox"</c>
    /// (case-insensitive substring). Returns all matches so callers can disambiguate
    /// when forks or stale subscriptions are present. Workshop IDs are intentionally
    /// not hardcoded — they change on re-upload. Never throws.
    /// </summary>
    public static List<TcsCandidate> TryFindTotalConversionSandbox()
    {
        var results = new List<TcsCandidate>();
        try
        {
            foreach (var library in GetSteamLibraryPaths())
            {
                var ck3WorkshopRoot = Helper.GetPath(library, "steamapps", "workshop", "content", "1158310");
                if (!Directory.Exists(ck3WorkshopRoot)) continue;

                foreach (var modFolder in Directory.EnumerateDirectories(ck3WorkshopRoot))
                {
                    var candidate = TryReadTcsCandidate(modFolder);
                    if (candidate != null) results.Add(candidate);
                }
            }
        }
        catch
        {
            // Best-effort scan; any error short-circuits to whatever we've collected.
        }
        return results;
    }

    private static readonly Regex DescriptorNameRegex = new("^\\s*name\\s*=\\s*\"(.+)\"\\s*$", RegexOptions.Multiline);
    private static readonly Regex DescriptorVersionRegex = new("^\\s*version\\s*=\\s*\"(.+)\"\\s*$", RegexOptions.Multiline);

    private static TcsCandidate? TryReadTcsCandidate(string modFolder)
    {
        var descriptorPath = Helper.GetPath(modFolder, "descriptor.mod");
        if (!File.Exists(descriptorPath)) return null;

        string content;
        try { content = File.ReadAllText(descriptorPath); }
        catch { return null; }

        var nameMatch = DescriptorNameRegex.Match(content);
        if (!nameMatch.Success) return null;

        var name = nameMatch.Groups[1].Value;
        if (name.IndexOf("Total Conversion Sandbox", StringComparison.OrdinalIgnoreCase) < 0)
            return null;

        var versionMatch = DescriptorVersionRegex.Match(content);
        var version = versionMatch.Success ? versionMatch.Groups[1].Value : null;

        return new TcsCandidate(
            Path: modFolder,
            Name: name,
            Version: version,
            WorkshopId: Path.GetFileName(modFolder),
            LastModified: Directory.GetLastWriteTime(modFolder));
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
    public static void Save()
    {
        // Save can be called from generic exit paths (Exit()) before Settings.Instance
        // is populated — e.g. when the user aborts FirstTimeSetup at the first prompt.
        // Treat that as a no-op rather than NRE-ing into the fatal handler.
        if (Settings.Instance == null) return;
        File.WriteAllText(settingsFileName, JsonSerializer.Serialize(Settings.Instance, SettingsJsonContext.Default.Settings));
    }
}

