namespace Hsin.Configuration.Models;

public sealed record AppearanceResource
{
    public required ReadOnlyMemory<byte> Content     { get; init; }
    public required string               ContentType { get; init; }
    public required string               ETag        { get; init; }
}
