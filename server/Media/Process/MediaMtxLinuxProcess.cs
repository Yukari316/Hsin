using System.Diagnostics;
using Serilog;
using SystemProcess = System.Diagnostics.Process;
using SerilogLogger = Serilog.ILogger;

namespace Hsin.Media.Process;

internal sealed class MediaMtxLinuxProcess : IMediaMtxProcess
{
    private const    int            GUARDIAN_STOP_TIMEOUT_SECONDS = 10;
    private readonly SerilogLogger  logger;
    private          SystemProcess? guardian;
    private          Task?          standardOutput;
    private          Task?          standardError;
    private          bool           stopped = true;

    public MediaMtxLinuxProcess(SerilogLogger logger)
    {
        this.logger = logger.ForContext("Component", "MediaMTX.Guardian");
    }

    public bool HasExited => guardian?.HasExited ?? true;
    public int? ExitCode  => guardian is { HasExited: true } ? guardian.ExitCode : null;

    public void Start(string executable, string configurationPath)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("MediaMtxLinuxProcess is available only on Linux.");
        if (!stopped)
            throw new InvalidOperationException("The Linux MediaMTX process was already started.");

        string hsinExecutable = Environment.ProcessPath
                                ?? throw new InvalidOperationException("The Hsin executable path is unavailable.");
        List<string> arguments = [];
        if (Path.GetFileNameWithoutExtension(hsinExecutable) == "dotnet")
            arguments.Add(typeof(LinuxMtxGuardian).Assembly.Location);
        arguments.Add("--media-guardian");
        arguments.Add(executable);
        arguments.Add(configurationPath);
        guardian = ProcessUtils.Start(
            hsinExecutable,
            arguments,
            Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            true,
            true,
            true);
        stopped = false;
        standardOutput = ProcessUtils.PumpAsync(
            guardian.StandardOutput,
            logger.ForContext("Component", "MediaMTX"),
            false);
        standardError = ProcessUtils.PumpAsync(
            guardian.StandardError,
            logger,
            true);
        logger.Information(
            "Started MediaMTX guardian process {GuardianProcessId}",
            guardian.Id);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
        => guardian?.WaitForExitAsync(cancellationToken)
           ?? Task.FromException(new InvalidOperationException("The Linux MediaMTX process has not started."));

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (stopped)
            return;
        stopped = true;
        if (guardian is null)
            return;

        if (!guardian.HasExited)
        {
            logger.Information("Stopping MediaMTX guardian process {GuardianProcessId}", guardian.Id);
            try
            {
                await guardian.StandardInput.WriteLineAsync("STOP");
                guardian.StandardInput.Close();
            }
            catch (IOException exception)
            {
                logger.Warning(
                    exception,
                    "MediaMTX guardian {GuardianProcessId} closed its control pipe",
                    guardian.Id);
            }

            try
            {
                await ProcessUtils.WaitForExitAsync(
                    guardian,
                    TimeSpan.FromSeconds(GUARDIAN_STOP_TIMEOUT_SECONDS),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.Warning(
                    "Guardian {GuardianProcessId} did not stop in time; terminating its process tree",
                    guardian.Id);
                await ProcessUtils.StopProcessTreeAsync(
                    guardian,
                    TimeSpan.FromSeconds(GUARDIAN_STOP_TIMEOUT_SECONDS),
                    cancellationToken);
            }
        }

        if (standardOutput is not null && standardError is not null)
            await Task.WhenAll(standardOutput, standardError);
        logger.Information(
            "MediaMTX guardian {GuardianProcessId} exited with code {ExitCode}",
            guardian.Id,
            guardian.ExitCode);
        guardian.Dispose();
        guardian       = null;
        standardOutput = null;
        standardError  = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }
}
