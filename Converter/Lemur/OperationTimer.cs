using System.Diagnostics;

namespace Converter.Lemur;

/// <summary>
/// Scoped timer — logs elapsed time when disposed.
/// Usage: using var _ = OperationTimer.Start("operation name");
/// </summary>
public sealed class OperationTimer : IDisposable
{
    private readonly string _name;
    private readonly Stopwatch _stopwatch;
    private bool _disposed;

    private OperationTimer(string name)
    {
        _name = name;
        _stopwatch = Stopwatch.StartNew();
    }

    public static OperationTimer Start(string name) => new(name);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopwatch.Stop();
        Logger.Info($"[Timer] {_name}: {_stopwatch.Elapsed.TotalSeconds:F3}s");
    }
}
