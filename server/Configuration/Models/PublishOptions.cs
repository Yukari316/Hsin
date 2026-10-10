namespace Hsin.Configuration.Models;

public sealed record PublishOptions
{
    public const string BEARER_PREFIX = "publisher:";

    public string BearerToken { get; init; } = "";
}
