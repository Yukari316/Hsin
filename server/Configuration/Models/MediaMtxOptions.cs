namespace Hsin.Configuration.Models;

public sealed record MediaMtxOptions
{
    public const int DEFAULT_PORT = 24516;

    public string                          DataDirectory   { get; init; } = "data/mediamtx";
    public string                          ListenAddress   { get; init; } = "0.0.0.0";
    public int                             Port            { get; init; } = DEFAULT_PORT;
    public string                          PublicOrigin    { get; init; } = "http://localhost:24516";
    public IReadOnlyList<string>           AdditionalHosts { get; init; } = [];
    public IReadOnlyList<IceServerOptions> IceServers      { get; init; } = [];
    public ControlApiOptions               ControlApi      { get; init; } = new();
}
