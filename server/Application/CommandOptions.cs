namespace Hsin.Application;

internal enum CommandKind
{
    Server,
    Update,
    Rollback,
    Guardian
}

internal sealed record CommandOptions
{
    public required CommandKind Kind               { get; init; }
    public          string?     ConfigPath         { get; init; }
    public          string?     Version            { get; init; }
    public          string?     MediaPackage       { get; init; }
    public          string?     MediaExecutable    { get; init; }
    public          string?     MediaConfiguration { get; init; }
}
