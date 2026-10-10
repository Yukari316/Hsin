using System.Globalization;
using Hsin.Configuration;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;

namespace Hsin.Logging;

public static class LogSetup
{
    private const string FILE_TIMESTAMP_FORMAT = "yyyy-MM-dd_HH-mm-ss";

    private const string LOG_OUTPUT_TEMPLATE =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{Component}] {Message:lj}{NewLine}{Exception}";

    public static LogSession Create(ResolvedConfiguration configuration, DateTimeOffset? startupTime = null)
    {
        LoggingOptions options = configuration.Options.Logging;
        LogEventLevel  level   = ParseLevel(options.MinimumLevel);
        SelfLog.Enable(TextWriter.Synchronized(Console.Error));
        LoggerConfiguration builder = new LoggerConfiguration()
                                      .MinimumLevel.Is(level)
                                      .Enrich.FromLogContext()
                                      .Enrich.WithProperty("Component", "Hsin")
                                      .Enrich.WithProperty("ProcessId", Environment.ProcessId)
                                      .WriteTo.Console(outputTemplate: LOG_OUTPUT_TEMPLATE);

        string? filePath = null;
        if (options.FileEnabled)
        {
            string directory = configuration.LogDirectory
                               ?? throw new InvalidOperationException(
                                   "A log directory is required when file logging is enabled.");
            Directory.CreateDirectory(directory);
            string timestamp = (startupTime ?? DateTimeOffset.Now)
                               .ToLocalTime()
                               .ToString(FILE_TIMESTAMP_FORMAT, CultureInfo.InvariantCulture);
            filePath = Path.Combine(directory, timestamp + ".log");
            // 文件 sink 会把初始化异常交给 SelfLog；先直接打开，确保不可写路径令启动失败。
            using (FileStream probe = new(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                probe.Flush(true);
            }

            builder.WriteTo.File(
                filePath,
                outputTemplate: LOG_OUTPUT_TEMPLATE,
                restrictedToMinimumLevel: level,
                rollingInterval: RollingInterval.Infinite,
                rollOnFileSizeLimit: false,
                fileSizeLimitBytes: null,
                retainedFileCountLimit: null,
                shared: true);
        }

        return new LogSession(builder.CreateLogger(), filePath);
    }

    private static LogEventLevel ParseLevel(string configured)
    {
        if (Enum.TryParse(configured, true, out LogEventLevel level)
            && Enum.IsDefined(level))
            return level;
        throw new InvalidDataException($"Logging.MinimumLevel '{configured}' is not a valid Serilog level.");
    }
}
