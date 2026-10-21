using Hsin.Configuration;
using Hsin.Media.Configuration;
using Hsin.Media;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using YamlDotNet.Serialization;

namespace Hsin.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public async Task MissingConfigurationCreatesAndPersistsPublisherToken()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(
            directory.Path,
            "hsin.json");

        using ResolvedConfiguration first = await ConfigurationLoader.LoadAsync(path);
        string                      token = first.Options.Publish.BearerToken;

        Assert.StartsWith(
            "publisher:",
            token,
            StringComparison.Ordinal);
        Assert.Equal(
            string.Empty,
            first.Options.WebUi.Token);
        Assert.Contains(
            "\"BearerToken\"",
            await File.ReadAllTextAsync(path),
            StringComparison.Ordinal);

        using ResolvedConfiguration second = await ConfigurationLoader.LoadAsync(path);
        Assert.Equal(
            token,
            second.Options.Publish.BearerToken);
    }

    [Fact]
    public async Task RelativePathsUseConfigurationDirectoryAndReadResourcesAsSnapshot()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllBytesAsync(
            Path.Combine(
                directory.Path,
                "showcase.png"),
            [1, 2, 3]);
        await File.WriteAllTextAsync(
            Path.Combine(
                directory.Path,
                "notice.md"),
            "# Notice");
        await File.WriteAllTextAsync(
            Path.Combine(
                directory.Path,
                "hsin.json"),
            """
            {
              "Appearance": { "ShowcaseImageFile": "showcase.png", "AnnouncementFile": "notice.md" },
              "Publish": { "BearerToken": "publisher:fixed" },
              "Logging": { "FileEnabled": false }
            }
            """);

        using ResolvedConfiguration resolved =
            await ConfigurationLoader.LoadAsync(
                Path.Combine(
                    directory.Path,
                    "hsin.json"));
        Assert.Equal(
            Path.Combine(
                directory.Path,
                "data",
                "mediamtx"),
            resolved.DataDirectory);
        Assert.Equal(
            [1, 2, 3],
            resolved.Resources["showcase"].Content.ToArray());
        Assert.Equal(
            "# Notice",
            resolved.Announcement);
        Assert.Null(resolved.LogDirectory);
    }

    [Fact]
    public async Task MediaConfigurationContainsSingleProtectedLivePath()
    {
        using TemporaryDirectory directory = new();
        string configPath = Path.Combine(
            directory.Path,
            "hsin.json");
        await File.WriteAllTextAsync(
            configPath,
            "{ \"Publish\": { \"BearerToken\": \"publisher:secret\" }, \"WebUi\": { \"Token\": \"viewer-secret\" } }");
        using ResolvedConfiguration resolved = await ConfigurationLoader.LoadAsync(configPath);
        string output = Path.Combine(
            directory.Path,
            "run",
            "mediamtx.yml");

        await MediaMtxConfiguration.WriteAsync(
            resolved,
            output);
        string yaml = await File.ReadAllTextAsync(output);
        Assert.NotNull(new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(yaml));
        Assert.Contains(
            "overridePublisher: true",
            yaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "live:",
            yaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "user: viewer",
            yaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "pass: sha256:",
            yaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "viewer-secret",
            yaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "user: any",
            yaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "webrtcAddress:",
            yaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "24516",
            yaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "apiAddress:",
            yaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "24517",
            yaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialTlsConfigurationFailsBeforeReadingCertificate()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(
            directory.Path,
            "hsin.json");
        await File.WriteAllTextAsync(
            path,
            "{ \"Tls\": { \"CertificateFile\": \"cert.pem\" } }");

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(() => ConfigurationLoader.LoadAsync(path));
        Assert.Contains(
            "must be configured together",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "publisher:",
            await File.ReadAllTextAsync(path),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnicodeViewerTokenIsHashedForMediaMtx()
    {
        using TemporaryDirectory directory = new();
        string configPath = Path.Combine(
            directory.Path,
            "hsin.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            { "WebUi": { "Token": "中文密碼" }, "Publish": { "BearerToken": "publisher:secret" } }
            """);
        using ResolvedConfiguration configuration = await ConfigurationLoader.LoadAsync(configPath);
        string output = Path.Combine(
            directory.Path,
            "mediamtx.yml");

        await MediaMtxConfiguration.WriteAsync(
            configuration,
            output);
        string yaml = await File.ReadAllTextAsync(output);

        Assert.Equal(
            "中文密碼",
            configuration.Options.WebUi.Token);
        Assert.Contains(
            "pass: sha256:",
            yaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "中文密碼",
            yaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowcaseImageIsOptional()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(
            directory.Path,
            "hsin.json");

        using ResolvedConfiguration resolved = await ConfigurationLoader.LoadAsync(path);
        Assert.Equal(string.Empty, resolved.Options.Appearance.ShowcaseImageFile);
        Assert.DoesNotContain("showcase", resolved.Resources.Keys);
    }

    [Fact]
    public async Task ShowcaseImageRejectsUnsupportedImageFormats()
    {
        using TemporaryDirectory directory = new();
        string                   path      = Path.Combine(directory.Path, "hsin.json");
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "showcase.bmp"), [1, 2, 3]);
        await File.WriteAllTextAsync(
            path,
            "{ \"Appearance\": { \"ShowcaseImageFile\": \"showcase.bmp\" } }");

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(() => ConfigurationLoader.LoadAsync(path));
        Assert.Contains("ShowcaseImageFile", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://localhost:24515/path")]
    [InlineData("http://user:password@localhost:24515")]
    [InlineData("http://localhost:24515?x=1")]
    [InlineData("https://localhost:24515")]
    public async Task InvalidPublicOriginDoesNotPersistGeneratedPublisherToken(string origin)
    {
        using TemporaryDirectory directory = new();
        string configPath = Path.Combine(
            directory.Path,
            "hsin.json");
        await File.WriteAllTextAsync(
            configPath,
            """
                { "WebUi": { "PublicOrigin": "__ORIGIN__" } }
                """.Replace(
                "__ORIGIN__",
                origin,
                StringComparison.Ordinal));

        await Assert.ThrowsAsync<InvalidDataException>(() => ConfigurationLoader.LoadAsync(configPath));
        Assert.DoesNotContain(
            "publisher:",
            await File.ReadAllTextAsync(configPath),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "any",
        "secret",
        "[\"127.0.0.1/32\"]")]
    [InlineData(
        "viewer",
        "secret",
        "[\"127.0.0.1/32\"]")]
    [InlineData(
        "hsin-control",
        "",
        "[\"127.0.0.1/32\"]")]
    [InlineData(
        "hsin-control",
        "secret",
        "[]")]
    public async Task RemoteControlApiRejectsMissingOrOverlappingAuthority(
        string user,
        string password,
        string allowedIps)
    {
        using TemporaryDirectory directory = new();
        string configPath = Path.Combine(
            directory.Path,
            "hsin.json");
        await File.WriteAllTextAsync(
            configPath,
            """
                { "MediaMtx": { "ControlApi": { "ListenAddress": "0.0.0.0", "User": "__USER__", "Password": "__PASSWORD__", "AllowedIps": __IPS__ } } }
                """.Replace(
                       "__USER__",
                       user,
                       StringComparison.Ordinal)
                   .Replace(
                       "__PASSWORD__",
                       password,
                       StringComparison.Ordinal)
                   .Replace(
                       "__IPS__",
                       allowedIps,
                       StringComparison.Ordinal));

        await Assert.ThrowsAsync<InvalidDataException>(() => ConfigurationLoader.LoadAsync(configPath));
    }

    [Fact]
    public async Task RemoteControlApiRejectsViewerPasswordReuse()
    {
        using TemporaryDirectory directory = new();
        string configPath = Path.Combine(
            directory.Path,
            "hsin.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "WebUi": { "Token": "shared-secret" },
              "MediaMtx": { "ControlApi": {
                "ListenAddress": "0.0.0.0", "User": "hsin-control",
                "Password": "shared-secret", "AllowedIps": ["127.0.0.1/32"]
              } }
            }
            """);

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(() => ConfigurationLoader.LoadAsync(configPath));
        Assert.Contains(
            "must differ",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoteControlApiRequiresLocalHsinSourceInAllowedIps()
    {
        using TemporaryDirectory directory = new();
        string configPath = Path.Combine(
            directory.Path,
            "hsin.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "MediaMtx": { "ControlApi": {
                "ListenAddress": "0.0.0.0", "User": "hsin-control",
                "Password": "separate-secret", "AllowedIps": ["192.0.2.0/24"]
              } }
            }
            """);

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(() => ConfigurationLoader.LoadAsync(configPath));
        Assert.Contains(
            "local API connection",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TlsInputsAreCapturedOnceForMediaAndWebServer()
    {
        using TemporaryDirectory directory = new();
        using RSA                rsa       = RSA.Create(2048);
        CertificateRequest request = new(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using X509Certificate2 sourceCertificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        string originalCertificate = sourceCertificate.ExportCertificatePem();
        string originalKey         = rsa.ExportPkcs8PrivateKeyPem();
        await File.WriteAllTextAsync(
            Path.Combine(
                directory.Path,
                "cert.pem"),
            originalCertificate);
        await File.WriteAllTextAsync(
            Path.Combine(
                directory.Path,
                "key.pem"),
            originalKey);
        string configPath = Path.Combine(
            directory.Path,
            "hsin.json");
        await File.WriteAllTextAsync(
            configPath,
            """
            {
              "WebUi": { "PublicOrigin": "https://localhost:24515" },
              "MediaMtx": { "PublicOrigin": "https://localhost:24516" },
              "Tls": { "CertificateFile": "cert.pem", "PrivateKeyFile": "key.pem" }
            }
            """);

        using ResolvedConfiguration configuration = await ConfigurationLoader.LoadAsync(configPath);
        await File.WriteAllTextAsync(
            Path.Combine(
                directory.Path,
                "cert.pem"),
            "changed");
        await File.WriteAllTextAsync(
            Path.Combine(
                directory.Path,
                "key.pem"),
            "changed");
        string output = Path.Combine(
            directory.Path,
            "run",
            "mediamtx.yml");
        await MediaMtxConfiguration.WriteAsync(
            configuration,
            output);

        Assert.True(configuration.Certificate?.HasPrivateKey);
        Assert.Equal(
            originalCertificate,
            await File.ReadAllTextAsync(
                Path.Combine(
                    directory.Path,
                    "run",
                    "tls",
                    "server.crt")));
        Assert.Equal(
            originalKey,
            await File.ReadAllTextAsync(
                Path.Combine(
                    directory.Path,
                    "run",
                    "tls",
                    "server.key")));
        Assert.Contains(
            "webrtcEncryption: true",
            await File.ReadAllTextAsync(output),
            StringComparison.Ordinal);

        using ResolvedConfiguration noTls =
            await ConfigurationLoader.LoadAsync(
                Path.Combine(
                    directory.Path,
                    "plain.json"));
        await MediaMtxConfiguration.WriteAsync(
            noTls,
            output);
        Assert.False(
            File.Exists(
                Path.Combine(
                    directory.Path,
                    "run",
                    "tls",
                    "server.key")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "hsin-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(
                    Path,
                    true);
        }
    }
}
