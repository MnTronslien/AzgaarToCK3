using Converter;
using Converter.Lemur;
using Converter.Lemur.Provinces;
using Converter.Lemur.Rivers;

namespace ConsoleUI;

internal class Program
{
    static async Task Run(string? jsonPath = null, string? geojsonPath = null, string? riversGeojsonPath = null,
        LogLevel? logLevel = null, bool? empireFromCulture = null,
        int? minDuchiesPerKingdom = null, int? minKingdomsPerEmpire = null,
        bool noRivers = false, bool noWipe = false, string? svgPath = null, string? inputDir = null,
        int? seed = null, int? tenetCount = null, float? doctrineMutationRate = null, string? outputDir = null,
        string? dumpCellsPath = null, bool riversOnly = false, bool? generateDebugImages = null)
    {
        Logger.Title();

        if (!SettingsManager.TryLoad())
        {
            FirstTimeSetup();
        }

        // Override settings with command-line arguments if provided
        if (logLevel.HasValue)
            Settings.Instance.LogLevel = logLevel.Value;
        // --generate-debug-images overrides the settings.json default in either direction.
        if (generateDebugImages.HasValue)
            Settings.Instance.GenerateDebugImages = generateDebugImages.Value;
        Logger.Info($"Debug images: {Settings.Instance.GenerateDebugImages} (from {(generateDebugImages.HasValue ? "CLI flag" : "settings.json")})");
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
            if (!string.IsNullOrWhiteSpace(Settings.OutputDirectoryOverride))
            {
                // -o given: name the mod after the output folder rather than prompting, so scripted runs
                // (which pass -o) never block on Console.ReadLine.
                Settings.Instance.ModName = Path.GetFileName(Settings.OutputDirectoryOverride.TrimEnd('/', '\\'));
                Logger.Info($"ModName not set; using output-dir name '{Settings.Instance.ModName}'.");
            }
            else if (Console.IsInputRedirected)
            {
                // Non-interactive (piped/CI/background) with no -o: fall back to a default instead of hanging.
                Settings.Instance.ModName = "LemurConverter";
                Logger.Info($"ModName not set and input is redirected; defaulting to '{Settings.Instance.ModName}'.");
            }
            else
            {
                Logger.Info("Name your mod: ");
                Settings.Instance.ModName = Console.ReadLine()!;
            }
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

        // Interactive prompt for the export folder when no input paths are configured
        // (typical right after FirstTimeSetup, or on an old settings.json with blank paths).
        // Skipped when stdin is redirected so scripted/CI runs fall through to the
        // explicit error below instead of hanging on ReadLine.
        if (!Console.IsInputRedirected
            && (string.IsNullOrWhiteSpace(Settings.Instance.InputJsonPath)
                || string.IsNullOrWhiteSpace(Settings.Instance.InputGeojsonPath)))
        {
            PromptForInputDirectory();
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
        // --rivers-only: draw rivers.png and exit, skipping the rest of the pipeline (fast iteration)
        if (riversOnly)
        {
            SettingsManager.Configure();
            await Converter.Lemur.ConversionManager.DrawRiversOnly(noRivers);
            return;
        }

        Console.Write("Start conversion? ");
        bool conversionRan = false;
        bool conversionSucceeded = false;
        if (YesNo())
        {
            conversionRan = true;
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
                conversionSucceeded = true;
            }
            catch (Exception ex)
            {
                Logger.Error("An error has occured.");
                Logger.Error(ex.Message);
                Logger.Error(ex.StackTrace ?? string.Empty);
            }
        }

#if DEBUG
        // DEBUG exits here before the messages below, so it signals a failed run
        // via a non-zero exit code (the launch scripts rely on this) rather than text.
        SettingsManager.Save();
        Environment.Exit(conversionRan && !conversionSucceeded ? 1 : 0);
#endif

        // Don't claim success when the conversion threw — the previous unconditional
        // "finished successfully!" line made a crashed run (empty mod folder) look
        // like it had worked, which masked failures such as issue #32.
        if (conversionRan && !conversionSucceeded)
            Logger.Error("Map conversion failed — see the errors above. The output folder may be empty or incomplete.");
        else if (conversionSucceeded)
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
            bool? generateDebugImages = null;
            bool noRivers = false;
            bool noWipe = false;
            bool riversOnly = false;
            bool? empireFromCulture = null;
            int? minDuchiesPerKingdom = null;
            int? minKingdomsPerEmpire = null;
            int? seed = null;
            int? tenetCount = null;
            float? doctrineMutationRate = null;
            bool noLogFile = false;
            string? logFilePath = null;

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
                else if (args[i] == "--rivers-only")
                {
                    riversOnly = true;
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
                else if (args[i] == "--generate-debug-images" && i + 1 < args.Length)
                {
                    generateDebugImages = bool.Parse(args[i + 1]);
                    i++;
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
                else if (args[i] == "--no-log-file")
                {
                    noLogFile = true;
                }
                else if (args[i] == "--log-file" && i + 1 < args.Length)
                {
                    logFilePath = args[++i];
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

            // Open the log file before any other work so the banner and the entire
            // run land in the file. Disabled by --no-log-file. Path resolution order:
            //   1. --log-file <path>  (explicit override)
            //   2. <exe folder>/logs/AzgaarToCK3_<yyyy-MM-ddTHH-mm-ss>.log  (default)
            if (!noLogFile)
            {
                var resolvedLogPath = logFilePath ?? Path.Combine(
                    Converter.SettingsManager.ExecutablePath,
                    "logs",
                    $"AzgaarToCK3_{DateTime.Now:yyyy-MM-ddTHH-mm-ss}.log");
                CleanupOldLogs(Path.GetDirectoryName(resolvedLogPath)!, keep: 10);
                Logger.EnableFileLogging(resolvedLogPath);
            }

            // Version banner — the first informational lines of every log so Discord
            // bug reports can be attributed to a specific converter build.
            var asmVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = asmVersion is null
                ? "unknown"
                : $"{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}";
            Logger.Info($"AzgaarToCK3 v{versionStr} (target CK3 {SettingsManager.Ck3SupportedVersion})");
            var asmLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var buildDate = string.IsNullOrEmpty(asmLocation)
                ? DateTime.Now
                : File.GetLastWriteTime(asmLocation);
            Logger.Info($"Built: {buildDate:yyyy-MM-dd}");

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

            await Run(jsonPath, geojsonPath, riversGeojsonPath, logLevel, empireFromCulture, minDuchiesPerKingdom, minKingdomsPerEmpire, noRivers, noWipe, svgPath, inputDir, seed, tenetCount, doctrineMutationRate, outputDir, dumpCellsPath, riversOnly, generateDebugImages);
        }
        catch (Exception ex)
        {
            HandleFatal(ex);
        }
    }

    private static void HandleFatal(Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine("─────────────────────────────────────────────────────────────");
        Console.WriteLine("  Something went wrong and the converter has to stop.");
        Console.WriteLine("─────────────────────────────────────────────────────────────");
        Console.WriteLine(ex.Message);
        Console.WriteLine();
        Console.WriteLine("Full stack trace (please include this if you file a bug):");
        Console.WriteLine(ex.ToString());
        Console.WriteLine();
        if (Logger.LogFilePath != null)
        {
            Console.WriteLine($"Full log saved to: {Logger.LogFilePath}");
            Console.WriteLine("Attach that file when filing a bug — it contains the complete run history.");
            Console.WriteLine();
        }
        Console.WriteLine("Report issues at https://github.com/MnTronslien/AzgaarToCK3/issues");
        Logger.Flush();
        PauseOnExit();
    }

    /// <summary>
    /// Keep the logs/ folder from growing forever. Deletes any AzgaarToCK3_*.log file
    /// beyond the <paramref name="keep"/> most recent (by mtime). Silently ignores
    /// errors — log housekeeping should never block a run.
    /// </summary>
    private static void CleanupOldLogs(string dir, int keep)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            var files = new DirectoryInfo(dir)
                .EnumerateFiles("AzgaarToCK3_*.log")
                .OrderByDescending(f => f.LastWriteTime)
                .Skip(keep)
                .ToArray();
            foreach (var f in files)
            {
                try { f.Delete(); } catch { /* best effort */ }
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Pause for a keypress so a console window launched from Explorer doesn't vanish
    /// before the user can read what's on it. No-op when stdin is redirected (piped/CI)
    /// so scripted runs don't hang.
    /// </summary>
    private static void PauseOnExit()
    {
        if (Console.IsInputRedirected) return;
        Console.WriteLine("Press any key to exit...");
        try { Console.ReadKey(intercept: true); }
        catch { /* no console attached — nothing to wait on */ }
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
        Console.WriteLine("  AzgaarToCK3 [options]");
        Console.WriteLine("  AzgaarToCK3 <json-path> <geojson-path> <rivers-geojson-path>");
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
        Console.WriteLine("  Full data  : *.json  — excludes settings.json and the .NET sidecars (*.deps.json, *.runtimeconfig.json)");
        Console.WriteLine("  Rivers     : *.geojson whose filename contains \"rivers\" or \"river\" (case-insensitive)");
        Console.WriteLine("  Cells      : *.geojson that does not match the rivers rule");
        Console.WriteLine("  Tip: name your exports \"<map> Full ...\", \"<map> Cells ...\", \"<map> Rivers ...\"");
        Console.WriteLine();
        Console.WriteLine("Conversion Options:");
        Console.WriteLine("  --no-rivers                      Skip river drawing; write a blank rivers.png (runtime only, not saved)");
        Console.WriteLine("  --rivers-only                    Draw only rivers.png and exit (skips the rest of the pipeline; fast iteration)");
        Console.WriteLine("  --svg, -s <path>                 Path to Azgaar SVG export for flatmap.dds (optional; fallback uses cell biome colors)");
        Console.WriteLine("  --log-level <verbose|debug|info|warning|error>  Set log verbosity (default: info)");
        Console.WriteLine("  --generate-debug-images <bool>   Override settings.json GenerateDebugImages (true/false); the setting is the default");
        Console.WriteLine("  --no-wipe                        Skip auto-wipe of mod output directory before conversion (default: wipe enabled)");
        Console.WriteLine("  --no-log-file                    Disable writing a .log file alongside the exe (default: write to ./logs/)");
        Console.WriteLine("  --log-file <path>                Override the log file path (default: ./logs/AzgaarToCK3_<timestamp>.log)");
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
        Console.WriteLine("                                   Use with TerrainLab --cells to iterate on heightmap with river data");
        Console.WriteLine("  --manifest <dir>                 Hash all files in <dir> and print a sorted SHA256 manifest");
        Console.WriteLine();
        Console.WriteLine("Other:");
        Console.WriteLine("  --help, -h                       Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  AzgaarToCK3 --input-dir C:/TestData --no-rivers");
        Console.WriteLine("  AzgaarToCK3 -d C:/TestData --rivers-geojson C:/other/rivers.geojson");
        Console.WriteLine("  AzgaarToCK3 --json map.json --geojson map.geojson --rivers-geojson rivers.geojson");
        Console.WriteLine("  AzgaarToCK3 -j map.json -g map.geojson -r rivers.geojson --log-level verbose");
        Console.WriteLine("  AzgaarToCK3 map.json map.geojson rivers.geojson --min-duchies-per-kingdom 3");
        Console.WriteLine();
        Console.WriteLine("If no arguments are provided, the program will use the settings.json file.");
    }

    private static bool YesNo(bool defaultIsYes = true)
    {
        if (Settings.Instance.LogLevel <= LogLevel.Debug)
        {
            Console.WriteLine($"{(defaultIsYes ? "- Yes" : "- No")} (auto-answer: log level <= debug)");
            return defaultIsYes;
        }

        Console.Write(defaultIsYes ? "[Y/n]: " : "[y/N]: ");
        return ReadConfirm(defaultIsYes);
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
        PauseOnExit();
        SettingsManager.Save();
        Environment.Exit(0);
    }

    private static void FirstTimeSetup()
    {
        Console.WriteLine();
        Console.WriteLine("═════════════════════════════════════════════════════════════");
        Console.WriteLine("  AzgaarToCK3 — First-Time Setup");
        Console.WriteLine("═════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("Welcome! I need a few things before I can convert your map.");

        var ck3 = ResolveCk3Directory();
        var modsDir = ResolveModsDirectory();
        var modName = PromptModName();

        Settings.Instance = new Settings
        {
            ModsDirectory = modsDir,
            Ck3Directory = ck3,
            ModName = modName,
        };
        SettingsManager.Save();

        // Step 4/4 — Azgaar exports. PromptForInputDirectory writes its results
        // into Settings.Instance and saves again.
        PromptForInputDirectory();

        Console.WriteLine();
        Console.WriteLine($"   Saved {SettingsManager.SettingsFilePath}. You won't see this screen again.");
        Console.WriteLine("   Edit that file later if you need to change anything.");
        Console.WriteLine();
    }

    private static string ResolveCk3Directory()
    {
        Console.WriteLine();
        Console.WriteLine("──[ 1/4 ]── Crusader Kings III install");

        var found = SettingsManager.TryFindCk3InstallRoot();
        if (found != null)
        {
            Console.WriteLine($"   Found: {found}");
            Console.Write("   Use this? [Y/n]: ");
            if (ReadConfirm(defaultIsYes: true))
                return found;
        }
        else
        {
            Console.WriteLine("   I couldn't auto-detect your CK3 install.");
            Console.WriteLine("   Paste the install root (the folder containing 'game\\'):");
        }

        while (true)
        {
            Console.Write("   CK3 install path (or press Enter to exit): ");
            var raw = (Console.ReadLine() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                Console.WriteLine("   Exiting setup.");
                Exit();
            }
            var path = StripSurroundingQuotes(raw);
            if (Directory.Exists(Converter.Helper.GetPath(path, "game")))
                return path;
            Console.WriteLine("   That folder doesn't contain a 'game' subdirectory. Try again.");
        }
    }

    private static void PromptForInputDirectory()
    {
        Console.WriteLine();
        Console.WriteLine("─────────────────────────────────────────────────────────────");
        Console.WriteLine("  Point me at your Azgaar exports");
        Console.WriteLine("─────────────────────────────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine("I need a folder containing your map's exported files:");
        Console.WriteLine("  - The 'Full data' .json file");
        Console.WriteLine("  - The 'Cells' .geojson file");
        Console.WriteLine("  - (Optional) The 'Rivers' .geojson file");
        Console.WriteLine();
        Console.WriteLine("In Azgaar's Fantasy Map Generator, use Save -> Save full,");
        Console.WriteLine("then Export -> Cells data and Export -> Rivers data.");
        Console.WriteLine();
        Console.WriteLine("You can drag the folder from Explorer into this window.");
        Console.WriteLine();

        while (true)
        {
            Console.Write("Folder path (or press Enter to exit): ");
            var raw = (Console.ReadLine() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                Console.WriteLine("Exiting.");
                Exit();
            }
            var folder = StripSurroundingQuotes(raw);
            if (!Directory.Exists(folder))
            {
                Console.WriteLine($"   That folder doesn't exist: {folder}");
                continue;
            }
            var (json, geojson, rivers) = ModManager.FindLatestInputs(folder);
            if (json == null || geojson == null)
            {
                Console.WriteLine("   Couldn't find both a .json and a .geojson in that folder.");
                Console.WriteLine($"   Found: json={(json == null ? "no" : Path.GetFileName(json))}, geojson={(geojson == null ? "no" : Path.GetFileName(geojson))}");
                continue;
            }
            Console.WriteLine();
            Console.WriteLine("   Found:");
            Console.WriteLine($"     Full data : {Path.GetFileName(json)}");
            Console.WriteLine($"     Cells     : {Path.GetFileName(geojson)}");
            Console.WriteLine($"     Rivers    : {(rivers != null ? Path.GetFileName(rivers) : "(none — blank rivers.png will be written)")}");
            Console.Write("   Use these? [Y/n]: ");
            if (!ReadConfirm(defaultIsYes: true)) continue;

            Settings.Instance.InputDirectory = folder;
            Settings.Instance.InputJsonPath = json;
            Settings.Instance.InputGeojsonPath = geojson;
            if (rivers != null) Settings.Instance.InputRiversGeojsonPath = rivers;
            SettingsManager.Save();
            Console.WriteLine();
            Console.WriteLine("   Saved to settings.json.");
            Console.WriteLine();
            return;
        }
    }

    private static string ResolveModsDirectory()
    {
        Console.WriteLine();
        Console.WriteLine("──[ 2/4 ]── Where to put the converted mod");
        Console.WriteLine($"   CK3's standard mod folder (where the launcher looks for local mods):");
        Console.WriteLine($"     {SettingsManager.DefaultModsDirectory}");
        Console.WriteLine("   Your mod will be created as a subfolder there.");
        Console.Write("   Use this? [Y/n]: ");
        if (ReadConfirm(defaultIsYes: true))
        {
            if (TryAcceptModsDirectory(SettingsManager.DefaultModsDirectory))
                return SettingsManager.DefaultModsDirectory;

            // Default failed write test. If the default was OneDrive-managed,
            // offer the local-profile Documents path as a one-tap fix — this
            // is by far the most common reason the write test fails (Windows
            // OneDrive locks the Documents folder against external writers).
            if (SettingsManager.DefaultModsDirectory.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
            {
                var localFallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Documents", "Paradox Interactive", "Crusader Kings III", "mod");

                Console.WriteLine();
                Console.WriteLine("   That path is OneDrive-managed. Windows OneDrive often blocks");
                Console.WriteLine("   external programs from writing into a redirected Documents folder.");
                Console.WriteLine();
                Console.WriteLine("   The non-OneDrive Documents path (your CK3 may already be using it):");
                Console.WriteLine($"     {localFallback}");
                Console.Write("   Use this instead? [Y/n]: ");
                if (ReadConfirm(defaultIsYes: true) && TryAcceptModsDirectory(localFallback))
                    return localFallback;
            }
        }

        Console.WriteLine("   Paste a different mods folder path (drag-and-drop supported):");
        while (true)
        {
            Console.Write("   Path (or press Enter to exit): ");
            var raw = (Console.ReadLine() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) Exit();
            var path = StripSurroundingQuotes(raw);
            if (TryAcceptModsDirectory(path))
                return path;
        }
    }

    /// <summary>
    /// Check that we can actually create and write to the proposed mods directory before
    /// saving it to settings.json. Reports a short failure reason; the caller decides
    /// what recovery to offer (OneDrive fallback, manual paste, etc.).
    /// </summary>
    private static bool TryAcceptModsDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var testFile = Path.Combine(path, $".azgaartock3-writetest-{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "");
            File.Delete(testFile);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Can't write to {path}");
            Console.WriteLine($"   ({ex.Message})");
            return false;
        }
    }

    private static string PromptModName()
    {
        Console.WriteLine();
        Console.WriteLine("──[ 3/4 ]── What should we name your mod?");
        Console.Write("   ModName [MyAzgaarMod]: ");
        var raw = (Console.ReadLine() ?? "").Trim();
        return string.IsNullOrWhiteSpace(raw) ? "MyAzgaarMod" : raw;
    }

    private static bool ReadConfirm(bool defaultIsYes)
    {
        while (true)
        {
            var raw = (Console.ReadLine() ?? "").Trim();
            if (raw.Length == 0) return defaultIsYes;
            if (raw.Equals("y", StringComparison.OrdinalIgnoreCase) || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (raw.Equals("n", StringComparison.OrdinalIgnoreCase) || raw.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
            Console.Write("   Please answer y or n: ");
        }
    }

    private static string StripSurroundingQuotes(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            return s[1..^1];
        return s;
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
            Console.Write("Use them as inputs? ");
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
