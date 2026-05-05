using Converter;
using Converter.Lemur;
using Converter.Lemur.Provinces;
using Converter.Lemur.Rivers;

namespace ConsoleUI;

internal class Program
{
    static async Task Run(string? jsonPath = null, string? geojsonPath = null, string? riversGeojsonPath = null,
        LogLevel? logLevel = null, bool noImages = false, bool? empireFromCulture = null,
        int? minDuchiesPerKingdom = null, int? minKingdomsPerEmpire = null,
        bool noRivers = false, bool noWipe = false, string? svgPath = null, string? inputDir = null,
        int? seed = null, int? tenetCount = null, float? doctrineMutationRate = null, string? outputDir = null,
        string? dumpCellsPath = null)
    {
        Logger.Title();

        if (!SettingsManager.TryLoad())
        {
            SettingsManager.CreateDefault();
            Logger.Info("Default Settings file has been created.");
        }

        // Override settings with command-line arguments if provided
        if (logLevel.HasValue)
            Settings.Instance.LogLevel = logLevel.Value;
        if (noImages)
            Settings.Instance.GenerateDebugImages = false;
        if (noWipe)
            Settings.Instance.AutoWipeOutput = false;
        if (!string.IsNullOrWhiteSpace(svgPath))
            Settings.Instance.AzgaarSvgPath = svgPath;
        if (!string.IsNullOrWhiteSpace(inputDir))
            Settings.Instance.InputDirectory = inputDir;
        if (!string.IsNullOrWhiteSpace(outputDir))
            Settings.OutputDirectoryOverride = outputDir;

        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            Settings.Instance.InputJsonPath = jsonPath;
            Logger.Info($"Using JSON path from argument: {jsonPath}");
        }
        if (!string.IsNullOrWhiteSpace(geojsonPath))
        {
            Settings.Instance.InputGeojsonPath = geojsonPath;
            Logger.Info($"Using GeoJSON path from argument: {geojsonPath}");
        }
        if (!string.IsNullOrWhiteSpace(riversGeojsonPath))
        {
            Settings.Instance.InputRiversGeojsonPath = riversGeojsonPath;
            Logger.Info($"Using Rivers GeoJSON path from argument: {riversGeojsonPath}");
        }
        if (empireFromCulture.HasValue)
        {
            Settings.Instance.EmpireFromCulture = empireFromCulture.Value;
            Logger.Info($"Empire formation from culture: {empireFromCulture.Value}");
        }
        if (minDuchiesPerKingdom.HasValue)
        {
            Settings.Instance.MinimumDuchiesPerKingdom = minDuchiesPerKingdom.Value;
            Logger.Info($"Minimum duchies per kingdom: {minDuchiesPerKingdom.Value}");
        }
        if (minKingdomsPerEmpire.HasValue)
        {
            Settings.Instance.MinimumKingdomsPerEmpire = minKingdomsPerEmpire.Value;
            Logger.Info($"Minimum kingdoms per empire: {minKingdomsPerEmpire.Value}");
        }
        if (seed.HasValue)
            Settings.Instance.Seed = seed.Value;
        if (tenetCount.HasValue)
            Settings.Instance.TenetCount = tenetCount.Value;
        if (doctrineMutationRate.HasValue)
            Settings.Instance.DoctrineMutationRate = doctrineMutationRate.Value;

        // Print settings (debug level)
        Logger.Debug(Settings.Instance.ToString());

        // Configure NumberDecimalSeparator. Writing files will not work otherwise.
        SettingsManager.Configure();

        Logger.Info(string.Empty);
        Logger.Info("The app has been configured. Feel free to change the settings in 'settings.json' file.");
        Logger.Info("Check https://github.com/MnTronslien/AzgaarToCK3 for instructions or feedback.");
        Logger.Info(string.Empty);

        if (string.IsNullOrWhiteSpace(Settings.Instance.ModName))
        {
            Logger.Info("Name your mod: ");
            Settings.Instance.ModName = Console.ReadLine()!;
        }

        // Resolve inputs from --input-dir / InputDirectory if set
        if (!string.IsNullOrWhiteSpace(Settings.Instance.InputDirectory))
        {
            var (dirJson, dirGeojson, dirRivers) = ModManager.FindLatestInputs(Settings.Instance.InputDirectory);
            if (string.IsNullOrWhiteSpace(jsonPath) && dirJson != null)
            {
                Settings.Instance.InputJsonPath = dirJson;
                Logger.Info($"Auto-resolved JSON from input directory: {dirJson}");
            }
            if (string.IsNullOrWhiteSpace(geojsonPath) && dirGeojson != null)
            {
                Settings.Instance.InputGeojsonPath = dirGeojson;
                Logger.Info($"Auto-resolved GeoJSON from input directory: {dirGeojson}");
            }
            if (string.IsNullOrWhiteSpace(riversGeojsonPath) && dirRivers != null)
            {
                Settings.Instance.InputRiversGeojsonPath = dirRivers;
                Logger.Info($"Auto-resolved rivers GeoJSON from input directory: {dirRivers}");
            }
        }

        // Only search for inputs if paths were not provided via command line AND auto-detect is enabled
        if (string.IsNullOrWhiteSpace(jsonPath) && string.IsNullOrWhiteSpace(geojsonPath) &&
            string.IsNullOrWhiteSpace(riversGeojsonPath) && Settings.Instance.AutoDetectInputs)
        {
            FindInputs();
        }

        if (!File.Exists(Settings.Instance.InputJsonPath))
        {
            Logger.Error($".json file has not been found.");
            Logger.Error($"Please, place it in '{Settings.Instance.InputJsonPath}' or change '{nameof(Settings.Instance.InputJsonPath)}' in 'settings.json'.");
            Exit();
        }
        if (!File.Exists(Settings.Instance.InputGeojsonPath))
        {
            Logger.Error($".geojson file has not been found.");
            Logger.Error($"Please, place it in '{Settings.Instance.InputGeojsonPath}' or change '{nameof(Settings.Instance.InputGeojsonPath)}' in 'settings.json'.");
            Exit();
        }
        // --dump-cells: run pipeline through major river insertion, write cell dump, exit
        if (dumpCellsPath != null)
        {
            SettingsManager.Configure();
            await Converter.Lemur.ConversionManager.DumpCellsAfterRivers(dumpCellsPath);
            return;
        }

        Logger.Info("Start conversion?");
        if (YesNo())
        {
            // Copy sandbox mod files.
            if (!ModManager.DoesModExist())
            {
                await ModManager.CreateMod();
            }

            if (Settings.Instance.AutoWipeOutput)
                WipeOutputDirectory();

            try
            {
                await Converter.Lemur.ConversionManager.Run(noRivers);
            }
            catch (Exception ex)
            {
                Logger.Error("An error has occured.");
                Logger.Error(ex.Message);
                Logger.Error(ex.StackTrace ?? string.Empty);
            }
        }

#if DEBUG
        SettingsManager.Save();
        Environment.Exit(0);
#endif

        Logger.Info("Map conversion finished successfully!");

        Exit();
    }
    static async Task Main(string[] args)
    {
        try
        {
            string? validateRiversPath = null;
            string? validateProvincesPath = null;
            string? definitionCsvPath = null;
            string? dumpCellsPath = null;
            string? manifestDir = null;
            string? jsonPath = null;
            string? geojsonPath = null;
            string? riversGeojsonPath = null;
            string? svgPath = null;
            string? inputDir = null;
            string? outputDir = null;
            LogLevel? logLevel = null;
            bool noImages = false;
            bool noRivers = false;
            bool noWipe = false;
            bool? empireFromCulture = null;
            int? minDuchiesPerKingdom = null;
            int? minKingdomsPerEmpire = null;
            int? seed = null;
            int? tenetCount = null;
            float? doctrineMutationRate = null;

            // Parse command-line arguments
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--json" || args[i] == "-j") && i + 1 < args.Length)
                {
                    jsonPath = args[i + 1];
                    i++; // Skip the next argument
                }
                else if ((args[i] == "--geojson" || args[i] == "-g") && i + 1 < args.Length)
                {
                    geojsonPath = args[i + 1];
                    i++; // Skip the next argument
                }
                else if ((args[i] == "--rivers-geojson" || args[i] == "-r") && i + 1 < args.Length)
                {
                    riversGeojsonPath = args[i + 1];
                    i++; // Skip the next argument
                }
                else if (args[i] == "--no-rivers")
                {
                    noRivers = true;
                }
                else if ((args[i] == "--input-dir" || args[i] == "-d") && i + 1 < args.Length)
                {
                    inputDir = args[i + 1];
                    i++;
                }
                else if ((args[i] == "--output-dir" || args[i] == "-o") && i + 1 < args.Length)
                {
                    outputDir = args[i + 1];
                    i++;
                }
                else if ((args[i] == "--svg" || args[i] == "-s") && i + 1 < args.Length)
                {
                    svgPath = args[i + 1];
                    i++;
                }
                else if (args[i] == "--no-images")
                {
                    noImages = true;
                }
                else if (args[i] == "--no-wipe")
                {
                    noWipe = true;
                }
                else if (args[i] == "--log-level" && i + 1 < args.Length)
                {
                    logLevel = Enum.Parse<LogLevel>(args[i + 1], ignoreCase: true);
                    i++; // Skip the next argument
                }
                else if (args[i] == "--empire-from-culture" && i + 1 < args.Length)
                {
                    empireFromCulture = bool.Parse(args[i + 1]);
                    i++; // Skip the next argument
                }
                else if (args[i] == "--min-duchies-per-kingdom" && i + 1 < args.Length)
                {
                    minDuchiesPerKingdom = int.Parse(args[i + 1]);
                    i++; // Skip the next argument
                }
                else if (args[i] == "--min-kingdoms-per-empire" && i + 1 < args.Length)
                {
                    minKingdomsPerEmpire = int.Parse(args[i + 1]);
                    i++; // Skip the next argument
                }
                else if (args[i] == "--seed" && i + 1 < args.Length)
                {
                    seed = int.Parse(args[i + 1]);
                    i++;
                }
                else if (args[i] == "--tenet-count" && i + 1 < args.Length)
                {
                    tenetCount = int.Parse(args[i + 1]);
                    i++;
                }
                else if (args[i] == "--doctrine-mutation-rate" && i + 1 < args.Length)
                {
                    doctrineMutationRate = float.Parse(args[i + 1]);
                    i++;
                }
                else if ((args[i] == "--validate-rivers" || args[i] == "-vr") && i + 1 < args.Length)
                {
                    validateRiversPath = args[i + 1];
                    i++;
                }
                else if ((args[i] == "--validate-provinces" || args[i] == "-vp") && i + 1 < args.Length)
                {
                    validateProvincesPath = args[i + 1];
                    i++;
                }
                else if ((args[i] == "--definition-csv" || args[i] == "-dc") && i + 1 < args.Length)
                {
                    definitionCsvPath = args[i + 1];
                    i++;
                }
                else if (args[i] == "--dump-cells" && i + 1 < args.Length)
                {
                    dumpCellsPath = args[++i];
                }
                else if (args[i] == "--manifest" && i + 1 < args.Length)
                {
                    manifestDir = args[++i];
                }
                else if (args[i] == "--help" || args[i] == "-h")
                {
                    PrintUsage();
                    return;
                }
                else if (!args[i].StartsWith("-"))
                {
                    // Positional arguments: first is JSON, second is GeoJSON, third is Rivers GeoJSON
                    if (jsonPath == null)
                    {
                        jsonPath = args[i];
                    }
                    else if (geojsonPath == null)
                    {
                        geojsonPath = args[i];
                    }
                    else if (riversGeojsonPath == null)
                    {
                        riversGeojsonPath = args[i];
                    }
                }
            }

            // --manifest: hash all files in <dir> and print a sorted SHA256 manifest
            if (manifestDir != null)
            {
                RunManifest(manifestDir);
                return;
            }

            // --validate-provinces: validate a provinces.png without full conversion
            if (!string.IsNullOrWhiteSpace(validateProvincesPath))
            {
                bool ok = ProvinceImageValidator.Validate(
                    validateProvincesPath, definitionCsvPath, out var errors);
                foreach (var e in errors) Logger.Info($"  {e}");
                Logger.Info(ok ? "✓ Valid" : $"✗ Invalid ({errors.Count} errors)");
                return;
            }

            // --validate-rivers: validate a rivers.png without full conversion
            if (!string.IsNullOrWhiteSpace(validateRiversPath))
            {
                Logger.Info($"Validating: {validateRiversPath}");
                bool ok = RiverImageValidator.ValidateRivers(
                    validateRiversPath,
                    out var badPixels,
                    out var msg);

                if (msg != null)
                    Logger.Info($"  Format: {msg}");
                if (badPixels != null)
                {
                    Logger.Info($"  Invalid pixels: {badPixels.Count}");
                    foreach (var p in badPixels.Take(50))
                        Logger.Info($"    ({p.X},{p.Y})");
                    if (badPixels.Count > 50)
                        Logger.Info($"  ... and {badPixels.Count - 50} more");
                }
                Logger.Info(ok ? "✓ Valid" : "✗ Invalid");
                return;
            }

            await Run(jsonPath, geojsonPath, riversGeojsonPath, logLevel, noImages, empireFromCulture, minDuchiesPerKingdom, minKingdomsPerEmpire, noRivers, noWipe, svgPath, inputDir, seed, tenetCount, doctrineMutationRate, outputDir, dumpCellsPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error has occurred.");
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static void RunManifest(string dir)
    {
        if (!Directory.Exists(dir))
        {
            Console.WriteLine($"Error: directory not found: {dir}");
            return;
        }

        Console.WriteLine($"# manifest {dir} @ {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");

        var allFiles = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories);

        var entries = new List<(string RelPath, string Hash)>();
        using var sha256 = System.Security.Cryptography.SHA256.Create();

        foreach (var file in allFiles)
        {
            var relPath = Path.GetRelativePath(dir, file).Replace('\\', '/');
            using var stream = File.OpenRead(file);
            var hashBytes = sha256.ComputeHash(stream);
            var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
            entries.Add((relPath, hashHex));
        }

        entries.Sort((a, b) => string.Compare(a.RelPath, b.RelPath, StringComparison.OrdinalIgnoreCase));

        foreach (var (relPath, hash) in entries)
            Console.WriteLine($"{hash}  {relPath}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Azgaar to CK3 Converter");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ConsoleUI [options]");
        Console.WriteLine("  ConsoleUI <json-path> <geojson-path> <rivers-geojson-path>");
        Console.WriteLine();
        Console.WriteLine("Input Options:");
        Console.WriteLine("  --input-dir, -d <dir>            Directory to scan for input files (see detection rules below)");
        Console.WriteLine("  --output-dir, -o <dir>           Override output directory (replaces ModsDirectory+ModName)");
        Console.WriteLine("  --json, -j <path>                Path to the full data .json file (overrides --input-dir)");
        Console.WriteLine("  --geojson, -g <path>             Path to the cells .geojson file (overrides --input-dir)");
        Console.WriteLine("  --rivers-geojson, -r <path>      Path to the rivers .geojson file (overrides --input-dir)");
        Console.WriteLine();
        Console.WriteLine("Auto-detection rules (used by --input-dir and AutoDetectInputs):");
        Console.WriteLine("  Files are ranked newest-first by creation time. The first match wins for each slot.");
        Console.WriteLine("  Full data  : *.json  — excludes settings.json and ConsoleUI*.json");
        Console.WriteLine("  Rivers     : *.geojson whose filename contains \"rivers\" or \"river\" (case-insensitive)");
        Console.WriteLine("  Cells      : *.geojson that does not match the rivers rule");
        Console.WriteLine("  Tip: name your exports \"<map> Full ...\", \"<map> Cells ...\", \"<map> Rivers ...\"");
        Console.WriteLine();
        Console.WriteLine("Conversion Options:");
        Console.WriteLine("  --no-rivers                      Skip river drawing; write a blank rivers.png (runtime only, not saved)");
        Console.WriteLine("  --svg, -s <path>                 Path to Azgaar SVG export for flatmap.dds (optional; fallback uses cell biome colors)");
        Console.WriteLine("  --log-level <verbose|debug|info|warning|error>  Set log verbosity (default: info)");
        Console.WriteLine("  --no-images                      Suppress debug image generation (provinces.png and rivers.png still written)");
        Console.WriteLine("  --no-wipe                        Skip auto-wipe of mod output directory before conversion (default: wipe enabled)");
        Console.WriteLine("  --empire-from-culture <bool>     Form empires by culture instead of religion");
        Console.WriteLine("  --min-duchies-per-kingdom <int>  Minimum duchies per kingdom (default: 4)");
        Console.WriteLine("  --min-kingdoms-per-empire <int>  Minimum kingdoms per empire (default: 3)");
        Console.WriteLine("  --seed <int>                     Global converter seed for all randomised decisions (omit for a new random seed each run)");
        Console.WriteLine("  --tenet-count <int>              Number of tenets per faith, 1–5 (default: 3)");
        Console.WriteLine("  --doctrine-mutation-rate <float> Child faith mutation rate 0.0–1.0 (default: 0.3)");
        Console.WriteLine();
        Console.WriteLine("Validation:");
        Console.WriteLine("  --validate-rivers, -vr <path>    Validate a rivers.png against CK3 requirements and exit");
        Console.WriteLine("  --validate-provinces, -vp <path> Validate a provinces.png against CK3 requirements and exit");
        Console.WriteLine("  --definition-csv, -dc <path>     Cross-check provinces.png against a definition.csv (use with -vp)");
        Console.WriteLine("  --dump-cells <path.json>         Load data, run major river insertion, write cell dump JSON and exit");
        Console.WriteLine("                                   Use with HeightmapLab --cells to iterate on heightmap with river data");
        Console.WriteLine("  --manifest <dir>                 Hash all files in <dir> and print a sorted SHA256 manifest");
        Console.WriteLine();
        Console.WriteLine("Other:");
        Console.WriteLine("  --help, -h                       Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ConsoleUI --input-dir C:/TestData --no-rivers");
        Console.WriteLine("  ConsoleUI -d C:/TestData --rivers-geojson C:/other/rivers.geojson");
        Console.WriteLine("  ConsoleUI --json map.json --geojson map.geojson --rivers-geojson rivers.geojson");
        Console.WriteLine("  ConsoleUI -j map.json -g map.geojson -r rivers.geojson --log-level verbose");
        Console.WriteLine("  ConsoleUI map.json map.geojson rivers.geojson --min-duchies-per-kingdom 3");
        Console.WriteLine();
        Console.WriteLine("If no arguments are provided, the program will use the settings.json file.");
    }

    private static bool YesNo(bool defaultIsYes = true)
    {
        if (Settings.Instance.LogLevel <= LogLevel.Debug)
        {
            //print the response to the console
            Console.WriteLine($"{(defaultIsYes ? "- Yes" : "- No")} (auto-answer: log level <= debug)");

            return defaultIsYes;
        }

        int maxTries = 10;
        string response = "";
        for (int i = 0; i < maxTries; i++)

        {
            Console.WriteLine("1. Yes.");
            Console.WriteLine("2. No.");

            response = Console.ReadLine()!;
            if (response == "1")
            {
                return true;
            }
            else if (response == "2")
            {
                return false;
            }
        }
        Console.WriteLine("Failed to read supported response.");
        Exit();
        return false;
    }

    private static void WipeOutputDirectory()
    {
        var outputDir = Settings.OutputDirectory;
        if (!Directory.Exists(outputDir))
        {
            Logger.Info("Output directory does not exist — nothing to wipe.");
            return;
        }
        Logger.Info($"Wiping output directory: {outputDir}");
        foreach (var file in Directory.GetFiles(outputDir))
            File.Delete(file);
        foreach (var dir in Directory.GetDirectories(outputDir))
            if (Path.GetFileName(dir) != ".git")
                Directory.Delete(dir, recursive: true);
        Logger.Info("Output directory wiped.");
    }

    private static void Exit()
    {
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey();
        SettingsManager.Save();
        Environment.Exit(0);
    }

    private static void FindInputs()
    {
        if (ModManager.FindLatestInputs() is ({ } jsonName, { } geojsonName, { } riversGeojsonName) &&
            (jsonName != Settings.Instance.InputJsonPath ||
             geojsonName != Settings.Instance.InputGeojsonPath ||
             riversGeojsonName != Settings.Instance.InputRiversGeojsonPath))
        {
            Console.WriteLine("Found new inputs in the directory:");
            Console.WriteLine(Path.GetFileName(jsonName));
            Console.WriteLine(Path.GetFileName(geojsonName));
            Console.WriteLine(Path.GetFileName(riversGeojsonName));
            Console.WriteLine("Use them as inputs?");

            if (YesNo())
            {
                Settings.Instance.InputJsonPath = jsonName;
                Settings.Instance.InputGeojsonPath = geojsonName;
                Settings.Instance.InputRiversGeojsonPath = riversGeojsonName;
            }
            else
            {
                EnsureInputsExist();

                Console.WriteLine("Previously used inputs will be used:");
                Console.WriteLine(Settings.Instance.InputJsonPath);
                Console.WriteLine(Settings.Instance.InputGeojsonPath);
                Console.WriteLine(Settings.Instance.InputRiversGeojsonPath);
            }
        }
        else
        {
            EnsureInputsExist();
        }

        // Exit if required inputs not found (rivers are optional — missing rivers → blank rivers.png)
        static void EnsureInputsExist()
        {
            var jsonExists = File.Exists(Settings.Instance.InputJsonPath);
            var geojsonExists = File.Exists(Settings.Instance.InputGeojsonPath);

            if (!jsonExists)
                Console.WriteLine(".json input was not found.");
            if (!geojsonExists)
                Console.WriteLine(".geojson input was not found.");

            if (!jsonExists || !geojsonExists)
            {
                Console.WriteLine($"-------------------------------------------------");
                Console.WriteLine($"Put your exported .json and .geojson files to this app's folder ({SettingsManager.ExecutablePath}).");
                Console.WriteLine("Make sure they have the latest 'modification date'.");
                Console.WriteLine("If the files cannot be found, open 'settings.json' and modify 'InputJsonPath' and 'InputGeojsonPath' values.");
                Console.WriteLine($"-------------------------------------------------");

                Exit();
            }

            if (!File.Exists(Settings.Instance.InputRiversGeojsonPath))
                Console.WriteLine("Rivers .geojson input was not found — a blank rivers.png will be written.");
        }
    }

}
