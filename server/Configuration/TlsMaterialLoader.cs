using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using Hsin.Configuration.Models;

namespace Hsin.Configuration;

internal static class TlsMaterialLoader
{
    public static TlsMaterial Load(string certificatePem, string privateKeyPem)
    {
        try
        {
            using X509Certificate2 pemCertificate = X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
            if (!pemCertificate.HasPrivateKey)
                throw new InvalidDataException("TLS certificate requires its matching private key.");
            DateTime now = DateTime.UtcNow;
            if (now < pemCertificate.NotBefore.ToUniversalTime() || now > pemCertificate.NotAfter.ToUniversalTime())
                throw new InvalidDataException("TLS certificate is outside its validity period.");
            X509KeyStorageFlags keyStorage = X509KeyStorageFlags.EphemeralKeySet;
            X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(
                pemCertificate.Export(X509ContentType.Pkcs12),
                null,
                keyStorage);
            try
            {
                X509Certificate2Collection chain = [];
                chain.ImportFromPem(certificatePem);
                return new TlsMaterial
                {
                    Certificate      = certificate,
                    CertificateChain = chain
                };
            }
            catch
            {
                certificate.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw new InvalidDataException("TLS certificate or private key is invalid.", exception);
        }
    }
}

internal sealed record TlsMaterial
{
    public required X509Certificate2           Certificate      { get; init; }
    public required X509Certificate2Collection CertificateChain { get; init; }
}
