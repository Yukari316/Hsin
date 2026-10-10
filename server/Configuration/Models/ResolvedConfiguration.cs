using System.Security.Cryptography.X509Certificates;

namespace Hsin.Configuration.Models;

public sealed record ResolvedConfiguration : IDisposable
{
    public required HsinOptions                                     Options          { get; init; }
    public required string                                          ConfigPath       { get; init; }
    public required string                                          ConfigDirectory  { get; init; }
    public required string                                          DataDirectory    { get; init; }
    public          string?                                         LogDirectory     { get; init; }
    public required Uri                                             ControlApiUri    { get; init; }
    public required IReadOnlyDictionary<string, AppearanceResource> Resources        { get; init; }
    public required string                                          Announcement     { get; init; }
    public          X509Certificate2?                               Certificate      { get; init; }
    public          X509Certificate2Collection?                     CertificateChain { get; init; }
    public          string?                                         CertificatePem   { get; init; }
    public          string?                                         PrivateKeyPem    { get; init; }

    public string FullConfigPath => ConfigPath;
    public string Directory      => ConfigDirectory;

    public void Dispose()
    {
        Certificate?.Dispose();
        if (CertificateChain is not null)
            foreach (X509Certificate2 item in CertificateChain)
                item.Dispose();
    }
}
