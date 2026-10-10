namespace Hsin.Media.Control;

public sealed record MediaPathSnapshot
{
    public bool    Online   { get; init; }
    public string? SourceId { get; init; }
}
