using Hsin.Media.Control;
using Hsin.Media.Process;
using SerilogLogger = Serilog.ILogger;

namespace Hsin.Application;

internal sealed class MediaMtxProcessService : IAsyncDisposable
{
    private const    int               MEDIA_START_TIMEOUT_SECONDS    = 15;
    private const    int               MEDIA_RETRY_DELAY_MILLISECONDS = 250;
    private readonly IMediaMtxProcess  process;
    private readonly MediaMtxApiClient api;
    private readonly Uri               controlApiUri;
    private readonly SerilogLogger     logger;
    private          bool              stopped = true;

    public MediaMtxProcessService(
        IMediaMtxProcess      process,
        MediaMtxApiClient     api,
        ResolvedConfiguration configuration,
        SerilogLogger         logger)
    {
        this.process  = process;
        this.api      = api;
        controlApiUri = configuration.ControlApiUri;
        this.logger   = logger.ForContext("Component", "MediaMTX.Service");
    }

    public bool HasExited => process.HasExited;
    public int? ExitCode  => process.ExitCode;

    public async Task StartAsync(
        string            executable,
        string            configurationPath,
        CancellationToken cancellationToken)
    {
        if (!stopped)
            throw new InvalidOperationException("MediaMTX is already running.");
        stopped = false;
        try
        {
            process.Start(executable, configurationPath);
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(MEDIA_START_TIMEOUT_SECONDS));
            Exception? lastFailure = null;
            while (!timeout.IsCancellationRequested)
            {
                if (process.HasExited)
                    throw new IOException($"MediaMTX process exited during startup with code {process.ExitCode}.");

                try
                {
                    if (await api.HasLivePathConfigurationAsync(timeout.Token) && !process.HasExited)
                        return;
                    lastFailure = new InvalidDataException(
                        "MediaMTX Control API does not contain the configured live path.");
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastFailure = exception;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(MEDIA_RETRY_DELAY_MILLISECONDS), timeout.Token);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    break;
                }
            }

            throw new TimeoutException(
                $"MediaMTX Control API at {controlApiUri} did not become ready within "
                + $"{MEDIA_START_TIMEOUT_SECONDS} seconds. Last check: {lastFailure?.Message ?? "no response"}.",
                lastFailure);
        }
        catch
        {
            await StopAsync(CancellationToken.None);
            throw;
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
        => process.WaitForExitAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (stopped)
            return;
        stopped = true;
        await process.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "MediaMTX process service failed while stopping");
            throw;
        }
    }
}
