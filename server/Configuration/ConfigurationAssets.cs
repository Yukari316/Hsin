using System.Security.Cryptography;
using Hsin.Configuration.Models;

namespace Hsin.Configuration;

internal static class ConfigurationAssets
{
    public static async Task<ConfigurationAssetsSnapshot> LoadAsync(
        string            configDirectory,
        AppearanceOptions appearance,
        CancellationToken cancellationToken)
    {
        Dictionary<string, AppearanceResource> resources = new(StringComparer.Ordinal);
        await ReadResourceAsync("logo", appearance.LogoFile, "LogoFile");
        await ReadResourceAsync("showcase", appearance.ShowcaseImageFile, "ShowcaseImageFile");
        await ReadResourceAsync("idle-icon", appearance.IdleIconFile, "IdleIconFile");
        string announcement = string.Empty;
        if (!string.IsNullOrWhiteSpace(appearance.AnnouncementFile))
            announcement = await File.ReadAllTextAsync(
                ResolvePath(configDirectory, appearance.AnnouncementFile),
                cancellationToken);

        return new ConfigurationAssetsSnapshot
        {
            Resources    = resources,
            Announcement = announcement
        };

        async Task ReadResourceAsync(string key, string configuredPath, string field)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return;
            string path    = ResolvePath(configDirectory, configuredPath);
            byte[] content = await File.ReadAllBytesAsync(path, cancellationToken);
            resources[key] = new AppearanceResource
            {
                Content     = content,
                ContentType = ContentType(path, field),
                ETag        = Convert.ToHexString(SHA256.HashData(content))
            };
        }
    }

    private static string ResolvePath(string baseDirectory, string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));

    private static string ContentType(string path, string field)
        => Path.GetExtension(path).ToLowerInvariant() switch
           {
               ".png"            => "image/png",
               ".jpg" or ".jpeg" => "image/jpeg",
               ".gif"            => "image/gif",
               ".webp"           => "image/webp",
               ".svg"            => "image/svg+xml",
               _                 => throw new InvalidDataException($"{field} must reference a supported image format.")
           };
}

internal sealed record ConfigurationAssetsSnapshot
{
    public required IReadOnlyDictionary<string, AppearanceResource> Resources    { get; init; }
    public required string                                          Announcement { get; init; }
}
