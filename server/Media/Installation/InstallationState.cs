namespace Hsin.Media.Installation;

public sealed record InstallationState
{
    public InstalledMediaVersion? Active   { get; init; }
    public InstalledMediaVersion? Previous { get; init; }
    public InstalledMediaVersion? Pending  { get; init; }
}
