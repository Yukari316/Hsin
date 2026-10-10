namespace Hsin.Configuration.Models;

public sealed record IceServerOptions
{
    public string Url        { get; init; } = "";
    public string Username   { get; init; } = "";
    public string Password   { get; init; } = "";
    public bool   ClientOnly { get; init; }
}
