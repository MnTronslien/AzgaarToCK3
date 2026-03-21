using Converter;

namespace ConsoleUI;

internal class Program
{
    static async Task Run(string? jsonPath = null, string? geojsonPath = null, bool? debug = null,
        bool? empireFromCulture = null, int? minDuchiesPerKingdom = null, int? minKingdomsPerEmpire = null)
    {
        if (!SettingsManager.TryLoad())
        {
            SettingsManager.CreateDefault();
            Console.WriteLine("Default Settings file has been created.");
        }

        // Override settings with command-line arguments if provided
        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            Settings.Instance.InputJsonPath = jsonPath;
            Console.WriteLine($"Using JSON path from argument: {jsonPath}");
        }
        if (!string.IsNullOrWhiteSpace(geojsonPath))
        {
            Settings.Instance.InputGeojsonPath = geojsonPath;
            Console.WriteLine($"Using GeoJSON path from argument: {geojsonPath}");
        }
        if (debug.HasValue)
        {
            Settings.Instance.Debug = debug.Value;
            Console.WriteLine($"Debug mode: {debug.Value}");
        }
        if (empireFromCulture.HasValue)
        {
            Settings.Instance.EmpireFromCulture = empireFromCulture.Value;
            Console.WriteLine($"Empire formation from culture: {empireFromCulture.Value}");
        }
        if (minDuchiesPerKingdom.HasValue)
        {
            Settings.Instance.MinimumDuchiesPerKingdom = minDuchiesPerKingdom.Value;
            Console.WriteLine($"Minimum duchies per kingdom: {minDuchiesPerKingdom.Value}");
        }
        if (minKingdomsPerEmpire.HasValue)
        {
            Settings.Instance.MinimumKingdomsPerEmpire = minKingdomsPerEmpire.Value;
            Console.WriteLine($"Minimum kingdoms per empire: {minKingdomsPerEmpire.Value}");
        }

        // Print settings
        Console.WriteLine(Settings.Instance);

        // Configure NumberDecimalSeparator. Writing files will not work otherwise.
        SettingsManager.Configure();

        Console.WriteLine();
        Console.WriteLine("The app has been configured. Feel free to change the settings in 'settings.json' file.");
        Console.WriteLine("Check https://github.com/pryvyd9/AzgaarToCK3 for instructions or feedback.");
        Console.WriteLine();

        if (string.IsNullOrWhiteSpace(Settings.Instance.ModName))
        {
            Console.Write("Name your mod: ");
            Settings.Instance.ModName = Console.ReadLine()!;
        }

        CheckIfShouldOverride();

        // Only search for inputs if paths were not provided via command line
        if (string.IsNullOrWhiteSpace(jsonPath) && string.IsNullOrWhiteSpace(geojsonPath))
        {
            FindInputs();
        }

        if (!File.Exists(Settings.Instance.InputJsonPath))
        {
            Console.WriteLine($".json file has not been found.");
            Console.WriteLine($"Please, place it in '{Settings.Instance.InputJsonPath}' or change '{nameof(Settings.Instance.InputJsonPath)}' in 'settings.json'.");
            Exit();
        }
        if (!File.Exists(Settings.Instance.InputGeojsonPath))
        {
            Console.WriteLine($".geojson file has not been found.");
            Console.WriteLine($"Please, place it in '{Settings.Instance.InputGeojsonPath}' or change '{nameof(Settings.Instance.InputGeojsonPath)}' in 'settings.json'.");
            Exit();
        }

        Console.WriteLine("Start conversion?");
        if (YesNo())
        {
            // Copy sandbox mod files.
            if (!ModManager.DoesModExist())
            {
                await ModManager.CreateMod();
            }

            try
            {
                await Converter.Lemur.ConversionManager.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error has occured.");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }

#if DEBUG
        SettingsManager.Save();
        Environment.Exit(0);
#endif

        Console.WriteLine("Map conversion finished successfully!");

        Exit();
    }
    static async Task Main(string[] args)
    {
        try
        {
            string? jsonPath = null;
            string? geojsonPath = null;
            bool? debug = null;
            bool? empireFromCulture = null;
            int? minDuchiesPerKingdom = null;
            int? minKingdomsPerEmpire = null;

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
                else if ((args[i] == "--debug" || args[i] == "-d") && i + 1 < args.Length)
                {
                    debug = bool.Parse(args[i + 1]);
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
                else if (args[i] == "--help" || args[i] == "-h")
                {
                    PrintUsage();
                    return;
                }
                else if (!args[i].StartsWith("-"))
                {
                    // Positional arguments: first is JSON, second is GeoJSON
                    if (jsonPath == null)
                    {
                        jsonPath = args[i];
                    }
                    else if (geojsonPath == null)
                    {
                        geojsonPath = args[i];
                    }
                }
            }

            await Run(jsonPath, geojsonPath, debug, empireFromCulture, minDuchiesPerKingdom, minKingdomsPerEmpire);
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error has occured.");
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Azgaar to CK3 Converter");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ConsoleUI [options]");
        Console.WriteLine("  ConsoleUI <json-path> <geojson-path>");
        Console.WriteLine();
        Console.WriteLine("Input Options:");
        Console.WriteLine("  --json, -j <path>                Path to the input .json file");
        Console.WriteLine("  --geojson, -g <path>             Path to the input .geojson file");
        Console.WriteLine();
        Console.WriteLine("Conversion Options:");
        Console.WriteLine("  --debug, -d <true|false>         Enable/disable debug mode (default: true)");
        Console.WriteLine("  --empire-from-culture <bool>     Form empires by culture instead of religion");
        Console.WriteLine("  --min-duchies-per-kingdom <int>  Minimum duchies per kingdom (default: 4)");
        Console.WriteLine("  --min-kingdoms-per-empire <int>  Minimum kingdoms per empire (default: 3)");
        Console.WriteLine();
        Console.WriteLine("Other:");
        Console.WriteLine("  --help, -h                       Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ConsoleUI --json map.json --geojson map.geojson");
        Console.WriteLine("  ConsoleUI -j map.json -g map.geojson --debug false");
        Console.WriteLine("  ConsoleUI map.json map.geojson --min-duchies-per-kingdom 3");
        Console.WriteLine();
        Console.WriteLine("If no arguments are provided, the program will use the settings.json file.");
    }

    private static bool YesNo(bool defaultIsYes = true)
    {
        if (Settings.Instance.Debug)
        {
            //print the response to the console
            Console.WriteLine($"{(defaultIsYes ? "- Yes" : "- No")} (Debug mode)");

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

    private static void Exit()
    {
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey();
        SettingsManager.Save();
        Environment.Exit(0);
    }

    private static void CheckIfShouldOverride()
    {
        while (ModManager.DoesModExist())
        {
            if (Settings.Instance.ShouldOverride is not null)
            {
                break;
            }

            Console.WriteLine("Mod already exists. Override?");
            Settings.Instance.ShouldOverride = YesNo();
            if (!Settings.Instance.ShouldOverride.Value)
            {
                Console.WriteLine("ChangeModName?");
                if (!YesNo())
                {
                    Console.WriteLine("Exiting... Please, change mod name in 'settings.json' if needed and try again");
                    Exit();
                }
                Console.WriteLine("Name your mod:");
                Settings.Instance.ModName = Console.ReadLine()!;
                break;
            }
            else
            {
                break;
            }
        }

        if (Settings.Instance.ShouldOverride ?? false)
        {
            Console.WriteLine($"Mod will be overriden in all future runs. If you wish to change it change '{nameof(Settings.Instance.ShouldOverride)}' in 'settings.json' file.");
        }

    }

    private static void FindInputs()
    {
        if (ModManager.FindLatestInputs() is ({ } jsonName, { } geojsonName) &&
            (jsonName != Settings.Instance.InputJsonPath || geojsonName != Settings.Instance.InputGeojsonPath))
        {
            Console.WriteLine("Found new inputs in the directory:");
            Console.WriteLine(Path.GetFileName(jsonName));
            Console.WriteLine(Path.GetFileName(geojsonName));
            Console.WriteLine("Use them as inputs?");

            if (YesNo())
            {
                Settings.Instance.InputJsonPath = jsonName;
                Settings.Instance.InputGeojsonPath = geojsonName;
            }
            else
            {
                EnsureInputsExist();

                Console.WriteLine("Previously used inputs will be used:");
                Console.WriteLine(Settings.Instance.InputJsonPath);
                Console.WriteLine(Settings.Instance.InputGeojsonPath);
            }
        }
        else
        {
            EnsureInputsExist();
        }

        // Exit if inputs not found
        static void EnsureInputsExist()
        {
            var jsonExists = File.Exists(Settings.Instance.InputJsonPath);
            var geojsonExists = File.Exists(Settings.Instance.InputGeojsonPath);

            if (!jsonExists)
            {
                Console.WriteLine(".json input was not found.");
            }
            if (!geojsonExists)
            {
                Console.WriteLine(".geojson input was not found.");
            }

            if (!jsonExists || !geojsonExists)
            {
                Console.WriteLine($"-------------------------------------------------");
                Console.WriteLine($"Put your exported .json, .geojson files to this app's folder ({SettingsManager.ExecutablePath}).");
                Console.WriteLine("Make sure they are they have the latest 'modification date'.");
                Console.WriteLine("If the wrong files are found delete other exported .json, .geojson files from the folder.");
                Console.WriteLine("If the files cannot be found open 'settings.json' and modify 'InputJsonPath' and 'InputGeojsonPath' values to point to your files.");
                Console.WriteLine($"-------------------------------------------------");

                Exit();
            }
        }
    }

}
