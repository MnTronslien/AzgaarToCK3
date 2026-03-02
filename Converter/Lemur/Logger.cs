namespace Converter.Lemur;

public static class Logger
{
    private static LogLevel CurrentLevel =>
        Settings.Instance is null ? LogLevel.Info : Settings.Instance.LogLevel;

    public static void Verbose(string message) => Write(LogLevel.Verbose, message);
    public static void Debug(string message)   => Write(LogLevel.Debug,   message);
    public static void Info(string message)    => Write(LogLevel.Info,    message);
    public static void Warning(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message)   => Write(LogLevel.Error,   message);

    public static void Section(string header)  // replaces Helper.PrintSectionHeader
    {
        if (CurrentLevel > LogLevel.Info) return;
        string line = new('=', header.Length);
        Console.WriteLine(line);
        Console.WriteLine(header);
        Console.WriteLine(line);
    }

    private static void Write(LogLevel level, string message)
    {
        if (level >= CurrentLevel)
            Console.WriteLine(message);
    }
}
