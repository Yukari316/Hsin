namespace Hsin.Configuration.Models;

public sealed record ControlApiOptions
{
    public const int DEFAULT_PORT = 24517;

    public string                ListenAddress { get; init; } = "127.0.0.1";
    public int                   Port          { get; init; } = DEFAULT_PORT;
    public string                User          { get; init; } = "hsin-control";
    public string                Password      { get; init; } = "";
    public IReadOnlyList<string> AllowedIps    { get; init; } = ["127.0.0.1/32", "::1/128"];
}
