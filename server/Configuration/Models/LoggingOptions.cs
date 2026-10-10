namespace Hsin.Configuration.Models;

public sealed record LoggingOptions
{
    public const int DEFAULT_RETENTION_DAYS = 7;

    public bool   FileEnabled   { get; init; } = true;
    public string Directory     { get; init; } = "logs";
    public int    RetentionDays { get; init; } = DEFAULT_RETENTION_DAYS;
    public string MinimumLevel  { get; init; } = "Information";
}
