using System.Security.Cryptography;
using System.Text;
using Hsin.Infrastructure;
using Serilog.Events;
using YamlDotNet.Serialization;

namespace Hsin.Media.Configuration;

public static class MediaMtxConfiguration
{
    private const string SNAPSHOT_CERTIFICATE_FILENAME = "server.crt";
    private const string SNAPSHOT_KEY_FILENAME         = "server.key";

    public static async Task WriteAsync(
        ResolvedConfiguration configuration,
        string                outputPath,
        CancellationToken     cancellationToken = default)
    {
        HsinOptions       options         = configuration.Options;
        ControlApiOptions control         = options.MediaMtx.ControlApi;
        string            publisherSecret = options.Publish.BearerToken[PublishOptions.BEARER_PREFIX.Length..];
        List<Dictionary<string, object?>> users =
        [
            User("publisher", publisherSecret, [Permission("publish", "live")]),
            string.IsNullOrEmpty(options.WebUi.Token)
                ? User("any", null, [Permission("read", "live")])
                : User("viewer", HashCredential(options.WebUi.Token), [Permission("read", "live")])
        ];

        if (string.IsNullOrEmpty(control.Password))
            users.Add(
                new Dictionary<string, object?>
                {
                    ["user"] = "any",
                    ["pass"] = null,
                    ["ips"]  = control.AllowedIps,
                    ["permissions"] = new object[]
                    {
                        Permission("api", null)
                    }
                });
        else
            users.Add(
                User(control.User, HashCredential(control.Password), [Permission("api", null)], control.AllowedIps));

        bool   tlsEnabled      = configuration.Certificate is not null;
        string certificatePath = "";
        string keyPath         = "";
        string runDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath))
                              ?? throw new InvalidDataException("Generated configuration path has no directory.");
        string tlsDirectory = Path.Combine(runDirectory, "tls");
        if (Directory.Exists(tlsDirectory) && (File.GetAttributes(tlsDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Generated TLS snapshot directory cannot be a symbolic link.");
        if (tlsEnabled)
        {
            certificatePath = Path.Combine(tlsDirectory, SNAPSHOT_CERTIFICATE_FILENAME);
            keyPath         = Path.Combine(tlsDirectory, SNAPSHOT_KEY_FILENAME);
            string certificatePem = configuration.CertificatePem
                                    ?? throw new InvalidOperationException("TLS certificate snapshot is unavailable.");
            string privateKeyPem = configuration.PrivateKeyPem
                                   ?? throw new InvalidOperationException("TLS private key snapshot is unavailable.");
            await AtomicFile.WriteAsync(certificatePath, certificatePem, cancellationToken);
            await AtomicFile.WriteAsync(keyPath, privateKeyPem, cancellationToken);
        }
        else if (Directory.Exists(tlsDirectory))
        {
            // 关闭 TLS 后清理 Hsin 上次生成的密钥快照，避免旧私钥继续留在运行目录。
            string previousKey         = Path.Combine(tlsDirectory, SNAPSHOT_KEY_FILENAME);
            string previousCertificate = Path.Combine(tlsDirectory, SNAPSHOT_CERTIFICATE_FILENAME);
            if (File.Exists(previousKey)) File.Delete(previousKey);
            if (File.Exists(previousCertificate)) File.Delete(previousCertificate);
        }

        Dictionary<string, object?> root = new()
        {
            ["logLevel"]              = MediaLogLevel(options.Logging.MinimumLevel),
            ["logDestinations"]       = new[] { "stdout" },
            ["authMethod"]            = "internal",
            ["authInternalUsers"]     = users,
            ["api"]                   = true,
            ["apiAddress"]            = FormatAddress(control.ListenAddress, control.Port),
            ["apiAllowOrigins"]       = new[] { options.WebUi.PublicOrigin },
            ["metrics"]               = false,
            ["pprof"]                 = false,
            ["playback"]              = false,
            ["rtsp"]                  = false,
            ["rtmp"]                  = false,
            ["hls"]                   = false,
            ["srt"]                   = false,
            ["moq"]                   = false,
            ["webrtc"]                = true,
            ["webrtcAddress"]         = FormatAddress(options.MediaMtx.ListenAddress, options.MediaMtx.Port),
            ["webrtcEncryption"]      = tlsEnabled,
            ["webrtcServerKey"]       = keyPath,
            ["webrtcServerCert"]      = certificatePath,
            ["webrtcAllowOrigins"]    = new[] { options.WebUi.PublicOrigin },
            ["webrtcLocalUDPAddress"] = FormatAddress(options.MediaMtx.ListenAddress, options.MediaMtx.Port),
            ["webrtcLocalTCPAddress"] = "",
            ["webrtcAdditionalHosts"] = options.MediaMtx.AdditionalHosts,
            ["webrtcICEServers2"] = options.MediaMtx.IceServers.Select(server => new Dictionary<string, object?>
            {
                ["url"]        = server.Url,
                ["username"]   = server.Username,
                ["password"]   = server.Password,
                ["clientOnly"] = server.ClientOnly
            }).ToArray(),
            ["pathDefaults"] = new Dictionary<string, object?>
            {
                ["source"]          = "publisher",
                ["alwaysAvailable"] = false,
                ["record"]          = false
            },
            ["paths"] = new Dictionary<string, object?>
            {
                ["live"] = new Dictionary<string, object?>
                {
                    ["source"]            = "publisher",
                    ["overridePublisher"] = true,
                    ["alwaysAvailable"]   = false,
                    ["record"]            = false
                }
            }
        };

        string yaml = new SerializerBuilder().Build().Serialize(root);
        await AtomicFile.WriteAsync(outputPath, yaml, cancellationToken);
    }

    private static Dictionary<string, object?> User(
        string                 user,
        string?                password,
        object[]               permissions,
        IReadOnlyList<string>? ips = null)
        => new()
        {
            ["user"]        = user,
            ["pass"]        = password,
            ["ips"]         = ips ?? [],
            ["permissions"] = permissions
        };

    private static Dictionary<string, object?> Permission(string action, string? path)
        => new()
        {
            ["action"] = action,
            ["path"]   = path
        };

    private static string HashCredential(string credential)
        => "sha256:" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));

    private static string MediaLogLevel(string minimumLevel)
    {
        LogEventLevel level = Enum.Parse<LogEventLevel>(minimumLevel, true);
        return level switch
               {
                   LogEventLevel.Verbose     => "debug",
                   LogEventLevel.Debug       => "debug",
                   LogEventLevel.Information => "info",
                   LogEventLevel.Warning     => "warn",
                   LogEventLevel.Error       => "error",
                   LogEventLevel.Fatal       => "error",
                   _                         => throw new InvalidDataException("Unsupported media log level.")
               };
    }

    private static string FormatAddress(string address, int port)
    {
        if (address is "0.0.0.0" or "*" or "::") return $":{port}";
        string host = address.Contains(':', StringComparison.Ordinal) && !address.StartsWith('[')
            ? $"[{address}]"
            : address;
        return $"{host}:{port}";
    }
}
