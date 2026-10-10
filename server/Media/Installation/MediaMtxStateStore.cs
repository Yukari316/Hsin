using System.Text.Json;
using System.Text.RegularExpressions;
using Hsin.Infrastructure;

namespace Hsin.Media.Installation;

internal sealed class MediaMtxStateStore
{
    private static readonly JsonSerializerOptions JSON_OPTIONS = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public MediaMtxStateStore(string dataDirectory)
    {
        Root = Path.GetFullPath(dataDirectory);
    }

    public string Root { get; }

    public string ExecutablePath(string installationId)
    {
        ValidateInstallationId(installationId);
        return Path.Combine(
            Root,
            "versions",
            installationId,
            OperatingSystem.IsWindows() ? "mediamtx.exe" : "mediamtx");
    }

    public async Task<InstallationState> ReadAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(Root, "state.json");
        if (!File.Exists(path))
            return new InstallationState();

        await using FileStream stream = File.OpenRead(path);
        InstallationState state = await JsonSerializer.DeserializeAsync<InstallationState>(
                                      stream,
                                      JSON_OPTIONS,
                                      cancellationToken)
                                  ?? throw new InvalidDataException("MediaMTX installation state is empty.");
        foreach (InstalledMediaVersion version in new[] { state.Active, state.Previous, state.Pending }
                     .OfType<InstalledMediaVersion>())
        {
            ValidateInstallationId(version.Id);
            MediaMtxReleaseClient.ValidateVersion(version.ReportedVersion);
            if (!Regex.IsMatch(version.ArchiveSha256, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant))
                throw new InvalidDataException("MediaMTX installation state has an invalid archive checksum.");
            if (version.BinarySha256 is null
                || !Regex.IsMatch(version.BinarySha256, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant))
                throw new InvalidDataException(
                    "MediaMTX installation state has no valid binary checksum. Back up and move the MediaMTX data directory, then install the version again.");
        }

        return state;
    }

    public Task WriteAsync(InstallationState state, CancellationToken cancellationToken)
        => AtomicFile.WriteAsync(
            Path.Combine(Root, "state.json"),
            JsonSerializer.Serialize(state, JSON_OPTIONS) + Environment.NewLine,
            cancellationToken);

    private static void ValidateInstallationId(string installationId)
    {
        bool official = Regex.IsMatch(installationId, @"^v\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);
        bool custom   = Regex.IsMatch(installationId, "^custom-[a-f0-9]{64}$", RegexOptions.CultureInvariant);
        if (!official && !custom)
            throw new InvalidDataException("MediaMTX installation ID is invalid.");
    }
}
