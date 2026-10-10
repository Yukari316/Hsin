using Hsin.Configuration;
using Hsin.Logging;
using Xunit.Sdk;

namespace Hsin.Tests;

public sealed class LoggingTests
{
    [Fact]
    public async Task FileLoggingUsesStartupTimestampAndCanBeDisabled()
    {
        using TemporaryDirectory directory = new();
        string configPath = Path.Combine(
            directory.Path,
            "hsin.json");
        await File.WriteAllTextAsync(
            configPath,
            "{ \"Logging\": { \"FileEnabled\": true, \"Directory\": \"custom-logs\" } }");
        using ResolvedConfiguration resolved = await ConfigurationLoader.LoadAsync(configPath);
        DateTimeOffset start = new(
            2026,
            9,
            28,
            12,
            34,
            56,
            TimeSpan.FromHours(8));
        using (LogSession session = LogSetup.Create(
                   resolved,
                   start))
        {
            session.Logger.Information("test event");
            Assert.Equal(
                Path.Combine(
                    directory.Path,
                    "custom-logs",
                    "2026-09-28_12-34-56.log"),
                session.FilePath);
        }

        Assert.True(
            File.Exists(
                Path.Combine(
                    directory.Path,
                    "custom-logs",
                    "2026-09-28_12-34-56.log")));

        string disabledPath = Path.Combine(
            directory.Path,
            "disabled.json");
        await File.WriteAllTextAsync(
            disabledPath,
            "{ \"Logging\": { \"FileEnabled\": false, \"Directory\": \"does-not-exist\" } }");
        using ResolvedConfiguration disabled = await ConfigurationLoader.LoadAsync(disabledPath);
        using LogSession disabledSession = LogSetup.Create(
            disabled,
            start);
        Assert.Null(disabledSession.FilePath);
        Assert.False(
            Directory.Exists(
                Path.Combine(
                    directory.Path,
                    "does-not-exist")));
    }

    [Fact]
    public async Task RetentionDeletesExpiredTimestampFilesButPreservesActiveAndUnknownFiles()
    {
        using TemporaryDirectory directory = new();
        string configPath = Path.Combine(
            directory.Path,
            "hsin.json");
        await File.WriteAllTextAsync(
            configPath,
            "{ \"Logging\": { \"Directory\": \"logs\", \"RetentionDays\": 7 } }");
        using ResolvedConfiguration resolved = await ConfigurationLoader.LoadAsync(configPath);
        using LogSession session = LogSetup.Create(
            resolved,
            DateTimeOffset.UtcNow);
        string logDirectory = resolved.LogDirectory ?? throw new InvalidOperationException("File logging is expected.");
        string active       = session.FilePath ?? throw new InvalidOperationException("File logging is expected.");
        string expired = Path.Combine(
            logDirectory,
            "2020-01-01_00-00-00.log");
        string unknown = Path.Combine(
            logDirectory,
            "keep.me");
        await File.WriteAllTextAsync(
            expired,
            "old");
        await File.WriteAllTextAsync(
            unknown,
            "keep");
        File.SetLastWriteTimeUtc(
            expired,
            DateTime.UtcNow.AddDays(-8));

        using LogRetentionService retention = new(
            logDirectory,
            active,
            LoggingOptions.DEFAULT_RETENTION_DAYS,
            session.Logger);
        retention.Cleanup(DateTimeOffset.UtcNow);
        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(unknown));
        Assert.True(File.Exists(active));
    }

    [Fact]
    public async Task FileLoggingRejectsExistingDirectoryAtFilePath()
    {
        using TemporaryDirectory directory = new();
        string configPath = Path.Combine(
            directory.Path,
            "hsin.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            { "Logging": { "Directory": "logs" } }
            """);
        using ResolvedConfiguration configuration = await ConfigurationLoader.LoadAsync(configPath);
        string logDirectory = configuration.LogDirectory
                              ?? throw new InvalidOperationException("File logging is expected.");
        Directory.CreateDirectory(
            Path.Combine(
                logDirectory,
                "2026-09-28_12-34-56.log"));

        DateTimeOffset started = new(
            2026,
            9,
            28,
            12,
            34,
            56,
            TimeSpan.FromHours(8));
        Exception failure = Record.Exception(() => LogSetup.Create(
                                                 configuration,
                                                 started))
                            ?? throw new XunitException("LogSetup.Create should fail for an unwritable log path.");
        Assert.True(failure is IOException or UnauthorizedAccessException);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "hsin-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(
                    Path,
                    true);
        }
    }
}
