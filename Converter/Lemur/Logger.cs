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

    public static void Title() => Console.WriteLine($"\n{Yellow}{Bold}{TitleArt}{Reset}\n");
    public static void Success() => Console.WriteLine($"\n{Green}{Bold}{SuccessArt}{Reset}\n");

    public static void Section(string header)
    {
        if (CurrentLevel > LogLevel.Info) return;
        string line = new('=', header.Length);
        Console.WriteLine($"{Cyan}{Bold}{line}");
        Console.WriteLine(header);
        Console.WriteLine($"{line}{Reset}");
    }

    private static void Write(LogLevel level, string message)
    {
        if (level >= CurrentLevel)
            Console.WriteLine(message);
    }
}
