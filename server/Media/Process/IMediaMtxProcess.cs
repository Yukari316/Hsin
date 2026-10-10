namespace Hsin.Media.Process;

internal interface IMediaMtxProcess : IAsyncDisposable
{
    bool HasExited { get; }
    int? ExitCode  { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void Start(string executable, string configurationPath);

    Task StopAsync(CancellationToken cancellationToken);
}
