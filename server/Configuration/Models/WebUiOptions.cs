namespace Hsin.Configuration.Models;

public sealed record WebUiOptions
{
    public const int DEFAULT_PORT = 24515;

    public string ListenAddress { get; init; } = "0.0.0.0";
    public int    Port          { get; init; } = DEFAULT_PORT;
    public string PublicOrigin  { get; init; } = "http://localhost:24515";
    public string Token         { get; init; } = "";
}
