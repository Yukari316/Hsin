namespace Hsin.Configuration.Models;

public sealed record TlsOptions
{
    public string CertificateFile { get; init; } = "";
    public string PrivateKeyFile  { get; init; } = "";
}
