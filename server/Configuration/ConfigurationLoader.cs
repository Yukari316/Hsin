using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Hsin.Infrastructure;

namespace Hsin.Configuration;

public static class ConfigurationLoader
{
    private const int    PUBLISHER_SECRET_BYTES   = 32;
    private const string PUBLISHER_SECRET_PATTERN = "^[A-Za-z0-9_-]+$";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
        WriteIndented               = true
    };

    public static async Task<HsinOptions> ReadOptionsAsync(
        string            configPath,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullPath)) return new HsinOptions();

        await using FileStream stream = File.OpenRead(fullPath);
        HsinOptions? options =
            await JsonSerializer.DeserializeAsync<HsinOptions>(stream, JsonOptions, cancellationToken);
        return options ?? new HsinOptions();
    }

    public static async Task<string> ReadDataDirectoryAsync(
        string            configPath,
        CancellationToken cancellationToken = default)
    {
        HsinOptions options = await ReadOptionsAsync(configPath, cancellationToken);
        string configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))
                                 ?? throw new InvalidDataException("Configuration path has no directory.");
        return ResolvePath(configDirectory, options.MediaMtx?.DataDirectory ?? new MediaMtxOptions().DataDirectory);
    }

    public static async Task<ResolvedConfiguration> LoadAsync(
        string            configPath,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(configPath);
        string configDirectory = Path.GetDirectoryName(fullPath)
                                 ?? throw new InvalidDataException("Configuration path has no directory.");
        Directory.CreateDirectory(configDirectory);

        JsonObject document;
        if (File.Exists(fullPath))
        {
            await using FileStream stream = File.OpenRead(fullPath);
            document = await JsonNode.ParseAsync(
                           stream,
                           null,
                           new JsonDocumentOptions
                           {
                               CommentHandling     = JsonCommentHandling.Skip,
                               AllowTrailingCommas = true
                           },
                           cancellationToken) as JsonObject
                       ?? throw new InvalidDataException(
                           $"Configuration file '{fullPath}' must contain a JSON object.");
        }
        else
        {
            JsonNode node = JsonSerializer.SerializeToNode(new HsinOptions(), JsonOptions)
                            ?? throw new InvalidDataException("Unable to create the initial configuration.");
            document = node.AsObject();
        }

        HsinOptions options = document.Deserialize<HsinOptions>(JsonOptions)
                              ?? throw new InvalidDataException("Configuration must contain a JSON object.");
        ConfigurationValidator.ValidateSections(options);
        string publisherToken = options.Publish.BearerToken;
        bool   persistConfig  = false;
        if (string.IsNullOrEmpty(publisherToken))
        {
            publisherToken = PublishOptions.BEARER_PREFIX + CreateSecret();
            options = options with
            {
                Publish = options.Publish with
                {
                    BearerToken = publisherToken
                }
            };
            SetJsonProperty(document, "Publish", "BearerToken", publisherToken);
            persistConfig = true;
        }

        if (!publisherToken.StartsWith(PublishOptions.BEARER_PREFIX, StringComparison.Ordinal)
            || publisherToken.Length == PublishOptions.BEARER_PREFIX.Length)
            throw new InvalidDataException("Publish.BearerToken must use the non-empty 'publisher:' prefix.");
        if (!Regex.IsMatch(publisherToken[PublishOptions.BEARER_PREFIX.Length..], PUBLISHER_SECRET_PATTERN))
            throw new InvalidDataException(
                "Publish.BearerToken secret must contain only ASCII letters, digits, '-' or '_'.");
        ConfigurationValidator.Validate(options, fullPath);
        options = options with
        {
            WebUi = options.WebUi with
            {
                PublicOrigin = new Uri(options.WebUi.PublicOrigin).GetLeftPart(UriPartial.Authority)
            },
            MediaMtx = options.MediaMtx with
            {
                PublicOrigin = new Uri(options.MediaMtx.PublicOrigin).GetLeftPart(UriPartial.Authority),
                AdditionalHosts = [.. options.MediaMtx.AdditionalHosts],
                IceServers = [.. options.MediaMtx.IceServers],
                ControlApi = options.MediaMtx.ControlApi with
                {
                    AllowedIps = [.. options.MediaMtx.ControlApi.AllowedIps]
                }
            }
        };

        string dataDirectory = ResolvePath(configDirectory, options.MediaMtx.DataDirectory);
        string? logDirectory = options.Logging.FileEnabled
            ? ResolvePath(configDirectory, options.Logging.Directory)
            : null;
        ConfigurationAssetsSnapshot assets = await ConfigurationAssets.LoadAsync(
            configDirectory,
            options.Appearance,
            cancellationToken);

        string?                     certificateFile = ResolveOptionalPath(configDirectory, options.Tls.CertificateFile);
        string?                     privateKeyFile = ResolveOptionalPath(configDirectory, options.Tls.PrivateKeyFile);
        X509Certificate2?           certificate = null;
        X509Certificate2Collection? certificateChain = null;
        string?                     certificatePem = null;
        string?                     privateKeyPem = null;
        if (certificateFile is not null && privateKeyFile is not null)
        {
            certificatePem = await File.ReadAllTextAsync(certificateFile, cancellationToken);
            privateKeyPem  = await File.ReadAllTextAsync(privateKeyFile, cancellationToken);
            TlsMaterial material = TlsMaterialLoader.Load(certificatePem, privateKeyPem);
            certificate      = material.Certificate;
            certificateChain = material.CertificateChain;
        }

        if (persistConfig)
            try
            {
                await AtomicFile.WriteAsync(
                    fullPath,
                    document.ToJsonString(JsonOptions) + Environment.NewLine,
                    cancellationToken);
            }
            catch
            {
                certificate?.Dispose();
                if (certificateChain is not null)
                    foreach (X509Certificate2 item in certificateChain)
                        item.Dispose();

                throw;
            }

        string controlHost = ConfigurationValidator.ControlTargetAddress(options.MediaMtx.ControlApi.ListenAddress);
        string controlUriHost = controlHost.Contains(':', StringComparison.Ordinal)
                                && !controlHost.StartsWith('[')
            ? $"[{controlHost}]"
            : controlHost;

        return new ResolvedConfiguration
        {
            Options          = options,
            ConfigPath       = fullPath,
            ConfigDirectory  = configDirectory,
            DataDirectory    = dataDirectory,
            LogDirectory     = logDirectory,
            ControlApiUri    = new Uri($"http://{controlUriHost}:{options.MediaMtx.ControlApi.Port}"),
            Resources        = assets.Resources,
            Announcement     = assets.Announcement,
            Certificate      = certificate,
            CertificateChain = certificateChain,
            CertificatePem   = certificatePem,
            PrivateKeyPem    = privateKeyPem
        };
    }

    private static string CreateSecret()
    {
        Span<byte> bytes = stackalloc byte[PUBLISHER_SECRET_BYTES];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string ResolvePath(string baseDirectory, string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));

    private static string? ResolveOptionalPath(string baseDirectory, string path)
        => string.IsNullOrWhiteSpace(path) ? null : ResolvePath(baseDirectory, path);

    private static void SetJsonProperty(JsonObject document, string sectionName, string propertyName, string value)
    {
        KeyValuePair<string, JsonNode?> sectionEntry =
            document.FirstOrDefault(entry => string.Equals(
                                        entry.Key,
                                        sectionName,
                                        StringComparison.OrdinalIgnoreCase));
        if (sectionEntry.Value is not JsonObject section)
        {
            section               = new JsonObject();
            document[sectionName] = section;
        }

        KeyValuePair<string, JsonNode?> existing =
            section.FirstOrDefault(entry => string.Equals(
                                       entry.Key,
                                       propertyName,
                                       StringComparison.OrdinalIgnoreCase));
        section[existing.Key ?? propertyName] = value;
    }

    private static bool EndpointConflicts(string firstAddress, int firstPort, string secondAddress, int secondPort)
    {
        if (firstPort != secondPort) return false;
        if (IsWildcard(firstAddress) || IsWildcard(secondAddress)) return true;
        return string.Equals(
            firstAddress.Trim('[', ']'),
            secondAddress.Trim('[', ']'),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWildcard(string address)
        => address is "0.0.0.0" or "::" or "*";
}
