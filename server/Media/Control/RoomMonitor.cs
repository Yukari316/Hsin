using SerilogLogger = Serilog.ILogger;

namespace Hsin.Media.Control;

public sealed class RoomMonitor : IAsyncDisposable
{
    private static readonly TimeSpan                SAMPLE_INTERVAL = TimeSpan.FromSeconds(1);
    private readonly        MediaMtxApiClient       api;
    private readonly        SerilogLogger           logger;
    private readonly        CancellationTokenSource stop      = new();
    private readonly        string                  startupId = Guid.NewGuid().ToString("N");
    private                 Task?                   worker;
    private                 RoomSnapshot            snapshot = new();
    private                 bool                    disposed;

    public RoomMonitor(MediaMtxApiClient api, SerilogLogger logger)
    {
        this.api    = api;
        this.logger = logger;
    }

    public RoomSnapshot Snapshot => Volatile.Read(ref snapshot);

    public void Start()
    {
        worker ??= Task.Run(PollAsync);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await stop.CancelAsync();
        if (worker is not null) await worker;

        stop.Dispose();
    }

    private async Task PollAsync()
    {
        using PeriodicTimer timer = new(SAMPLE_INTERVAL);
        try
        {
            do
            {
                await SampleAsync(stop.Token);
            } while (await timer.WaitForNextTickAsync(stop.Token));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        try
        {
            MediaPathSnapshot? path = await api.GetLivePathAsync(cancellationToken);
            RoomSnapshot       next;
            if (path is null || !path.Online)
                next = new RoomSnapshot
                {
                    Status     = "idle",
                    Generation = startupId + ":idle"
                };
            else
                next = new RoomSnapshot
                {
                    Status     = "live",
                    Generation = startupId + ":" + (path.SourceId ?? "unknown")
                };

            Volatile.Write(ref snapshot, next);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Volatile.Write(
                ref snapshot,
                new RoomSnapshot
                {
                    Status     = "unavailable",
                    Generation = startupId + ":unavailable"
                });
            logger.Warning(exception, "MediaMTX room status could not be read");
        }
    }
}
