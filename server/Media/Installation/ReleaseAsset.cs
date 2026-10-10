namespace Hsin.Media.Installation;

public sealed record ReleaseAsset
{
    public string Version     { get; init; } = "";
    public string Name        { get; init; } = "";
    public Uri    DownloadUrl { get; init; } = new("https://github.com");
    public string Sha256      { get; init; } = "";
    public long   Size        { get; init; }
}
