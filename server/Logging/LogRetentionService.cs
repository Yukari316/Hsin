using SerilogLogger = Serilog.ILogger;

namespace Hsin.Logging;

public sealed class LogRetentionService : IDisposable
{
    private const int CLEANUP_INTERVAL_HOURS = 1;

    private readonly string        directory;
    private readonly string        activeFilePath;
    private readonly int           retentionDays;
    private readonly SerilogLogger logger;
    private readonly TimeSpan      interval;
    private          Timer?        timer;
    private          int           running;

    public LogRetentionService(
        string        directory,
        string        activeFilePath,
        int           retentionDays,
        SerilogLogger logger,
        TimeSpan?     interval = null)
    {
        if (retentionDays <= 0) throw new ArgumentOutOfRangeException(nameof(retentionDays));
        this.directory      = Path.GetFullPath(directory);
        this.activeFilePath = Path.GetFullPath(activeFilePath);
        this.retentionDays  = retentionDays;
        this.logger         = logger;
        this.interval       = interval ?? TimeSpan.FromHours(CLEANUP_INTERVAL_HOURS);
    }

    public void Start()
    {
        timer ??= new Timer(
            static state =>
            {
                if (state is LogRetentionService service)
                    service.Cleanup(DateTimeOffset.UtcNow);
            },
            this,
            Timeout.InfiniteTimeSpan,
            interval);
        timer.Change(TimeSpan.Zero, interval);
    }

    public void Cleanup(DateTimeOffset now)
    {
        if (Interlocked.Exchange(ref running, 1) != 0) return;
        try
        {
            if (!Directory.Exists(directory)) return;
            DateTime cutoff = now.UtcDateTime - TimeSpan.FromDays(retentionDays);
            StringComparison pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            foreach (string path in Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFullPath(path), activeFilePath, pathComparison)) continue;
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(path);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Unable to inspect log file {Path}", path);
                    continue;
                }

                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) continue;
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Unable to remove expired log file {Path}", path);
                }
            }
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Unable to scan log directory {Directory}", directory);
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }

    public void Dispose()
    {
        if (timer is not null) timer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        timer = null;
    }
}
