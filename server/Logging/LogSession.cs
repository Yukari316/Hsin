using Serilog.Core;

namespace Hsin.Logging;

public sealed class LogSession : IDisposable, IAsyncDisposable
{
    internal LogSession(Logger logger, string? filePath)
    {
        Logger   = logger;
        FilePath = filePath;
    }

    public Logger  Logger   { get; }
    public string? FilePath { get; }

    public void Dispose()
    {
        Logger.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Logger.Dispose();
        return ValueTask.CompletedTask;
    }
}
