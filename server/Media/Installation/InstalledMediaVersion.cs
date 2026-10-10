namespace Hsin.Media.Installation;

public sealed record InstalledMediaVersion
{
    public required string             Id              { get; init; }
    public required string             ReportedVersion { get; init; }
    public required MediaPackageSource Source          { get; init; }
    public required string             ArchiveSha256   { get; init; }
    public required string             BinarySha256    { get; init; }
}
