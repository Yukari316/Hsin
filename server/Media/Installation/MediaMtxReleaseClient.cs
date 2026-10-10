using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hsin.Media.Installation;

public sealed class MediaMtxReleaseClient
{
    public const     string     DEFAULT_VERSION    = "v1.21.1";
    private const    long       MAX_DOWNLOAD_BYTES = 128 * 1024 * 1024;
    private const    string     API_ROOT           = "https://api.github.com/repos/bluenviron/mediamtx/releases/";
    private const    int        BUFFER_BYTES       = 64 * 1024;
    private readonly HttpClient http;

    public MediaMtxReleaseClient(HttpClient http)
    {
        this.http = http;
    }

    public static void ValidateVersion(string version)
    {
        if (!Regex.IsMatch(version, @"^v\d+\.\d+\.\d+$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("MediaMTX version must be a stable tag such as v1.21.1.");
    }

    public async Task<ReleaseAsset> ResolveAsync(string? version, CancellationToken cancellationToken)
    {
        if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("This Hsin release supports Windows x64 and Linux x64.");
        if (version is not null) ValidateVersion(version);
        string endpoint = version is null ? "latest" : "tags/" + version;

        using HttpRequestMessage request = new(HttpMethod.Get, API_ROOT + endpoint);
        request.Headers.UserAgent.ParseAdd("Hsin/0.1");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        JsonElement release = document.RootElement;

        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("MediaMTX prereleases and drafts are not installable.");

        string tag = release.GetProperty("tag_name").GetString()
                     ?? throw new InvalidDataException("Release tag is absent.");
        ValidateVersion(tag);
        string suffix = OperatingSystem.IsWindows() ? "windows_amd64.zip" : "linux_amd64.tar.gz";

        string        assetName = $"mediamtx_{tag}_{suffix}";
        JsonElement[] assets    = release.GetProperty("assets").EnumerateArray().ToArray();
        JsonElement   asset     = assets.SingleOrDefault(item => item.GetProperty("name").GetString() == assetName);
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException($"Official release is missing {assetName}.");

        string url = asset.GetProperty("browser_download_url").GetString()
                     ?? throw new InvalidDataException("Asset URL is absent.");
        Uri downloadUrl = new(url);
        ValidateAssetUrl(downloadUrl);
        long size = asset.GetProperty("size").GetInt64();
        if (size is <= 0 or > MAX_DOWNLOAD_BYTES)
            throw new InvalidDataException("Release asset size is outside the download limit.");

        string digest = asset.TryGetProperty("digest", out JsonElement digestElement)
            ? digestElement.GetString() ?? ""
            : "";
        string sha256;
        if (digest.StartsWith("sha256:", StringComparison.Ordinal))
        {
            sha256 = digest["sha256:".Length..];
        }
        else
        {
            JsonElement checksumAsset =
                assets.SingleOrDefault(item => (item.GetProperty("name").GetString() ?? "")
                                           .EndsWith("checksums.sha256", StringComparison.Ordinal));
            if (checksumAsset.ValueKind == JsonValueKind.Undefined)
                throw new InvalidDataException(
                    "The official release does not provide an asset digest or checksum list.");

            Uri checksumUrl = new(checksumAsset.GetProperty("browser_download_url").GetString() ?? "");
            ValidateAssetUrl(checksumUrl);
            using HttpResponseMessage checksumResponse = await http.GetAsync(checksumUrl, cancellationToken);
            checksumResponse.EnsureSuccessStatusCode();
            Uri actualChecksumUrl = checksumResponse.RequestMessage?.RequestUri ?? checksumUrl;
            ValidateFinalDownloadUrl(actualChecksumUrl);
            string checksums = await checksumResponse.Content.ReadAsStringAsync(cancellationToken);
            string? line = checksums.Split('\n').FirstOrDefault(item =>
            {
                string[] parts = item.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length == 2 && parts[1].TrimStart('*') == assetName;
            });
            sha256 = line?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        }

        if (!Regex.IsMatch(sha256, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("The release asset has no valid official SHA-256 checksum.");

        return new ReleaseAsset
        {
            Version     = tag,
            Name        = assetName,
            DownloadUrl = downloadUrl,
            Sha256      = sha256,
            Size        = size
        };
    }

    public async Task DownloadAsync(ReleaseAsset asset, string outputPath, CancellationToken cancellationToken)
    {
        ValidateAssetUrl(asset.DownloadUrl);
        using HttpResponseMessage response = await http.GetAsync(
            asset.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        Uri finalUrl = response.RequestMessage?.RequestUri ?? asset.DownloadUrl;
        ValidateFinalDownloadUrl(finalUrl);

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream output = new(
            outputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BUFFER_BYTES,
            FileOptions.Asynchronous);
        using IncrementalHash hash     = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[]                buffer   = new byte[BUFFER_BYTES];
        long                  received = 0;
        int                   count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            received += count;
            if (received > MAX_DOWNLOAD_BYTES || received > asset.Size)
                throw new InvalidDataException("Release download exceeds its advertised size.");

            hash.AppendData(buffer.AsSpan(0, count));
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        if (received != asset.Size
            || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(asset.Sha256)))
            throw new InvalidDataException(
                "MediaMTX download size or SHA-256 checksum does not match the official release.");
    }

    private static void ValidateAssetUrl(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps
            || url.Host != "github.com"
            || !url.AbsolutePath.StartsWith(
                "/bluenviron/mediamtx/releases/download/",
                StringComparison.Ordinal))
            throw new InvalidDataException("MediaMTX must be downloaded from its official GitHub release.");
    }

    private static void ValidateFinalDownloadUrl(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps
            || (url.Host != "github.com"
                && url.Host != "release-assets.githubusercontent.com"
                && url.Host != "objects.githubusercontent.com"))
            throw new InvalidDataException("Release download redirected outside official HTTPS asset hosts.");
    }
}
