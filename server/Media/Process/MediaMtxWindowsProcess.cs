using Hsin.Media.Process.Native;
using SerilogLogger = Serilog.ILogger;

namespace Hsin.Media.Process;

internal sealed class MediaMtxWindowsProcess : IMediaMtxProcess
{
    private const    int                           MEDIA_STOP_TIMEOUT_SECONDS = 10;
    private readonly SerilogLogger                 logger;
    private          WindowsNative.StartedProcess? owned;
    private          Task?                         standardOutput;
    private          Task?                         standardError;
    private          bool                          stopped = true;

    public MediaMtxWindowsProcess(SerilogLogger logger)
    {
        this.logger = logger.ForContext("Component", "MediaMTX");
    }

    public bool HasExited => owned?.Process.HasExited ?? true;

    public int? ExitCode => owned is { Process.HasExited: true }
        ? owned.Process.ExitCode
        : null;

    public void Start(string executable, string configurationPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("MediaMtxWindowsProcess is available only on Windows.");
        if (!stopped)
            throw new InvalidOperationException("The Windows MediaMTX process was already started.");

        WindowsNative.StartedProcess started =
            WindowsNative.StartInJob(executable, configurationPath);
        owned          = started;
        stopped        = false;
        standardOutput = ProcessUtils.PumpAsync(started.StandardOutput, logger, false);
        standardError  = ProcessUtils.PumpAsync(started.StandardError, logger, true);
        logger.Information("Started owned MediaMTX process {ProcessId}", started.Process.Id);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
        => owned?.Process.WaitForExitAsync(cancellationToken)
           ?? Task.FromException(new InvalidOperationException("The Windows MediaMTX process has not started."));

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (stopped)
            return;
        stopped = true;
        if (owned is null)
            return;

        if (!owned.Process.HasExited)
        {
            logger.Information("Stopping owned MediaMTX process {ProcessId}", owned.Process.Id);
            owned.TerminateJob();
            try
            {
                await ProcessUtils.WaitForExitAsync(
                    owned.Process,
                    TimeSpan.FromSeconds(MEDIA_STOP_TIMEOUT_SECONDS),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await owned.Process.WaitForExitAsync(CancellationToken.None);
            }
        }

        if (standardOutput is not null && standardError is not null)
            await Task.WhenAll(standardOutput, standardError);
        logger.Information(
            "MediaMTX process {ProcessId} exited with code {ExitCode}",
            owned.Process.Id,
            owned.Process.ExitCode);
        owned.Dispose();
        owned          = null;
        standardOutput = null;
        standardError  = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }
}
