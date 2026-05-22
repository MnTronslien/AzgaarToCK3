using System.Text;
using System.Text.RegularExpressions;

namespace Converter.Lemur;

public static class Logger
{
    private const string Yellow = "\u001b[33m";
    private const string Green  = "\u001b[92m";
    private const string Red    = "\u001b[31m";
    private const string Cyan   = "\u001b[96m";
    private const string Bold   = "\u001b[1m";
    private const string Reset  = "\u001b[0m";

    private static LogLevel CurrentLevel =>
        Settings.Instance is null ? LogLevel.Info : Settings.Instance.LogLevel;

    // File logging — opened by Program.Main before any other log call so the
    // banner and settings dump are captured. Mirrors everything that passes
    // the LogLevel gate to a .log file alongside the exe. AutoFlush + the
    // UnhandledException + ProcessExit hooks below keep the file durable
    // through managed crashes.
    private static StreamWriter? _fileWriter;
    private static string? _fileWriterPath;
    private static readonly object _fileLock = new();
    private static readonly Regex AnsiRegex = new("\\[[\\d;]*m", RegexOptions.Compiled);

    /// <summary>Absolute path of the active log file, or null if file logging is disabled.</summary>
    public static string? LogFilePath => _fileWriterPath;

    public static void EnableFileLogging(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            _fileWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
            {
                AutoFlush = true,
            };
            _fileWriterPath = path;

            AppDomain.CurrentDomain.UnhandledException += (_, _) => Flush();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: couldn't open log file at {path}: {ex.Message}");
            _fileWriter = null;
            _fileWriterPath = null;
        }
    }

    public static void Flush()
    {
        lock (_fileLock) { _fileWriter?.Flush(); }
    }

    public static void Close()
    {
        lock (_fileLock) { _fileWriter?.Dispose(); _fileWriter = null; }
    }

    private static void WriteToFile(LogLevel level, string message)
    {
        if (_fileWriter == null) return;
        var line = $"[{DateTime.Now:HH:mm:ss.fff} {level,-7}] {AnsiRegex.Replace(message, "")}";
        lock (_fileLock) { _fileWriter.WriteLine(line); }
    }

    private static void WriteRawToFile(string text)
    {
        if (_fileWriter == null) return;
        lock (_fileLock) { _fileWriter.WriteLine(AnsiRegex.Replace(text, "")); }
    }

    public static void Verbose(string message) => Write(LogLevel.Verbose, message);
    public static void Debug(string message)   => Write(LogLevel.Debug,   message);
    public static void Info(string message)    => Write(LogLevel.Info,    message);
    public static void Warning(string message) => Write(LogLevel.Warning, $"{Yellow}{message}{Reset}");
    public static void Error(string message)   => Write(LogLevel.Error,   $"{Red}{message}{Reset}");

    private const string TitleArt =
        " █████╗ ███████╗ ██████╗  █████╗  █████╗ ██████╗     ████████╗ ██████╗      ██████╗██╗  ██╗██████╗ \n" +
        "██╔══██╗╚══███╔╝██╔════╝ ██╔══██╗██╔══██╗██╔══██╗    ╚══██╔══╝██╔═══██╗    ██╔════╝██║ ██╔╝╚════██╗\n" +
        "███████║  ███╔╝ ██║  ███╗███████║███████║██████╔╝       ██║   ██║   ██║    ██║     █████╔╝  █████╔╝\n" +
        "██╔══██║ ███╔╝  ██║   ██║██╔══██║██╔══██║██╔══██╗       ██║   ██║   ██║    ██║     ██╔═██╗  ╚═══██╗\n" +
        "██║  ██║███████╗╚██████╔╝██║  ██║██║  ██║██║  ██║       ██║   ╚██████╔╝    ╚██████╗██║  ██╗██████╔╝\n" +
        "╚═╝  ╚═╝╚══════╝ ╚═════╝ ╚═╝  ╚═╝╚═╝  ╚═╝╚═╝  ╚═╝       ╚═╝    ╚═════╝      ╚═════╝╚═╝  ╚═╝╚═════╝\n" +
        "\n" +
        "                        ██╗     ███████╗███╗   ███╗██╗   ██╗██████╗               \n" +
        "                        ██║     ██╔════╝████╗ ████║██║   ██║██╔══██╗              \n" +
        "                        ██║     █████╗  ██╔████╔██║██║   ██║██████╔╝              \n" +
        "                        ██║     ██╔══╝  ██║╚██╔╝██║██║   ██║██╔══██╗              \n" +
        "                        ███████╗███████╗██║ ╚═╝ ██║╚██████╔╝██║  ██║              \n" +
        "                        ╚══════╝╚══════╝╚═╝     ╚═╝ ╚═════╝ ╚═╝  ╚═╝              ";

    private const string SuccessArt =
        "                                            ▄▄ \n" +
        "▄█████ ▄▄ ▄▄  ▄▄▄▄  ▄▄▄▄ ▄▄▄▄▄  ▄▄▄▄  ▄▄▄▄  ██ \n" +
        "▀▀▀▄▄▄ ██ ██ ██▀▀▀ ██▀▀▀ ██▄▄  ███▄▄ ███▄▄  ██ \n" +
        "█████▀ ▀███▀ ▀████ ▀████ ██▄▄▄ ▄▄██▀ ▄▄██▀  ▄▄ \n" +
        "                                               ";

    public static void Title()
    {
        Console.WriteLine($"\n{Yellow}{Bold}{TitleArt}{Reset}\n");
        WriteRawToFile(TitleArt);
    }
    public static void Success()
    {
        Console.WriteLine($"\n{Green}{Bold}{SuccessArt}{Reset}\n");
        WriteRawToFile(SuccessArt);
    }

    public static void Section(string header)
    {
        if (CurrentLevel > LogLevel.Info) return;
        string line = new('=', header.Length);
        Console.WriteLine($"{Cyan}{Bold}{line}");
        Console.WriteLine(header);
        Console.WriteLine($"{line}{Reset}");
        WriteRawToFile(line);
        WriteRawToFile(header);
        WriteRawToFile(line);
    }

    private static void Write(LogLevel level, string message)
    {
        if (level < CurrentLevel) return;
        Console.WriteLine(message);
        WriteToFile(level, message);
    }
}
