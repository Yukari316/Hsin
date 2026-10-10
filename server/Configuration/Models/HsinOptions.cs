namespace Hsin.Configuration.Models;

public sealed record HsinOptions
{
    public WebUiOptions      WebUi      { get; init; } = new();
    public PublishOptions    Publish    { get; init; } = new();
    public AppearanceOptions Appearance { get; init; } = new();
    public TlsOptions        Tls        { get; init; } = new();
    public LoggingOptions    Logging    { get; init; } = new();
    public MediaMtxOptions   MediaMtx   { get; init; } = new();
}
