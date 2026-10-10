using System.Diagnostics;
using System.Text.Json;
using Hsin.Media;
using Hsin.Media.Installation;

namespace Hsin.Tests;

public sealed class ProgramStartupTests
{
    private const int STARTUP_TIMEOUT_SECONDS = 20;

    [Fact]
    public async Task StartupFailureIsWrittenBeforeFileLoggerCloses()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hsin-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string configPath = Path.Combine(
                directory,
                "hsin.json");
            await File.WriteAllTextAsync(
                configPath,
                """
                { "Logging": { "FileEnabled": true, "Directory": "logs" } }
                """);
            string mediaDirectory = Path.Combine(
                directory,
                "data",
                "mediamtx");
            Directory.CreateDirectory(mediaDirectory);
            InstallationState state = new()
            {
                Active = new InstalledMediaVersion
                {
                    Id              = "v1.21.1",
                    ReportedVersion = "v1.21.1",
                    Source          = MediaPackageSource.Official,
                    ArchiveSha256 = new string(
                        '0',
                        64),
                    BinarySha256 = new string(
                        '0',
                        64)
                }
            };
            await File.WriteAllTextAsync(
                Path.Combine(
                    mediaDirectory,
                    "state.json"),
                JsonSerializer.Serialize(state));

            ProcessStartInfo start = new("dotnet")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add(configPath);
            using Process process = Process.Start(start) ?? throw new IOException("Hsin test process did not start.");
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(STARTUP_TIMEOUT_SECONDS));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync();
                }
            }

            Assert.Equal(
                1,
                process.ExitCode);
            string logPath = Assert.Single(
                Directory.GetFiles(
                    Path.Combine(
                        directory,
                        "logs"),
                    "*.log"));
            string log = await File.ReadAllTextAsync(logPath);
            Assert.Contains(
                "Hsin stopped before completing startup or operation",
                log,
                StringComparison.Ordinal);
            Assert.Contains(
                "MediaMTX executable is absent",
                log,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "ObjectDisposedException",
                log,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }
}
