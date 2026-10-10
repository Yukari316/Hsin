using System.Formats.Tar;
using System.IO.Compression;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Hsin.Media.Installation;

internal static class MediaMtxPackageExtractor
{
    private const long MAX_EXTRACTED_BYTES = 256 * 1024 * 1024;
    private const int  COPY_BUFFER_BYTES   = 64 * 1024;
    private const int  MAX_ARCHIVE_ENTRIES = 4;

    internal static async Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken)
    {
        string expectedExecutable = archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? "mediamtx.exe"
            : "mediamtx";
        HashSet<string> expected        = [expectedExecutable, "mediamtx.yml", "LICENSE"];
        HashSet<string> written         = new(StringComparer.Ordinal);
        string?         commonDirectory = null;
        bool            rootFileSeen    = false;
        long            total           = 0;
        int             entryCount      = 0;
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await using ZipArchive archive = await ZipFile.OpenReadAsync(archivePath, cancellationToken);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                CheckEntryCount(++entryCount);
                const int UNIX_TYPE_MASK  = 0xF000;
                const int UNIX_LINK_TYPE  = 0xA000;
                const int UNIX_MODE_SHIFT = 16;
                if (((entry.ExternalAttributes >> UNIX_MODE_SHIFT) & UNIX_TYPE_MASK) == UNIX_LINK_TYPE)
                    throw new InvalidDataException("Links in release archives are not allowed.");

                string? name = NormalizeName(
                    entry.FullName,
                    entry.FullName.EndsWith('/'),
                    ref commonDirectory,
                    ref rootFileSeen);
                if (name is null) continue;

                CheckFileName(name);
                await using Stream     input  = await entry.OpenAsync(cancellationToken);
                await using FileStream output = new(Path.Combine(destination, name), FileMode.CreateNew);
                await CopyBoundedAsync(input, output, cancellationToken);
            }
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
                 || archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            await using FileStream archive = File.OpenRead(archivePath);
            await using GZipStream gzip    = new(archive, CompressionMode.Decompress);
            await using TarReader  reader  = new(gzip);
            while (await reader.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
            {
                CheckEntryCount(++entryCount);
                if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                    throw new InvalidDataException("Links in release archives are not allowed.");

                bool directory = entry.EntryType == TarEntryType.Directory;
                if (!directory && entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    throw new InvalidDataException("Release archive contains an unsupported entry type.");

                string? name = NormalizeName(
                    entry.Name,
                    directory,
                    ref commonDirectory,
                    ref rootFileSeen);
                if (name is null) continue;

                CheckFileName(name);
                if (entry.DataStream is null) throw new InvalidDataException("Release file has no data.");

                await using FileStream output = new(Path.Combine(destination, name), FileMode.CreateNew);
                await CopyBoundedAsync(entry.DataStream, output, cancellationToken);
            }
        }
        else
        {
            throw new InvalidDataException("MediaMTX package must be ZIP or TAR.GZ.");
        }

        if (!written.SetEquals(expected))
            throw new InvalidDataException("MediaMTX package must contain the executable, mediamtx.yml and LICENSE.");

        if (new FileInfo(Path.Combine(destination, "LICENSE")).Length == 0)
            throw new InvalidDataException("MediaMTX package license is empty.");

        string configurationPath = Path.Combine(destination, "mediamtx.yml");
        if (new FileInfo(configurationPath).Length == 0)
            throw new InvalidDataException("MediaMTX package configuration is empty.");

        try
        {
            using StreamReader configuration = File.OpenText(configurationPath);
            YamlStream         yaml          = new();
            yaml.Load(configuration);
            if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode)
                throw new InvalidDataException("MediaMTX package configuration must contain one YAML mapping.");
        }
        catch (YamlException exception)
        {
            throw new InvalidDataException("MediaMTX package configuration is invalid YAML.", exception);
        }

        void CheckFileName(string name)
        {
            if (!expected.Contains(name))
                throw new InvalidDataException($"MediaMTX package contains an unexpected file: {name}.");

            if (!written.Add(name))
                throw new InvalidDataException("Duplicate file in release archive.");
        }

        async Task CopyBoundedAsync(Stream input, Stream output, CancellationToken token)
        {
            byte[] buffer = new byte[COPY_BUFFER_BYTES];
            int    read;
            while ((read = await input.ReadAsync(buffer, token)) != 0)
            {
                total += read;
                CheckTotal(total);
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
        }
    }

    private static string? NormalizeName(
        string      name,
        bool        directory,
        ref string? commonDirectory,
        ref bool    rootFileSeen)
    {
        if (name.StartsWith("./", StringComparison.Ordinal)) name = name[2..];

        if (name.Length == 0 && directory) return null;

        if (Path.IsPathRooted(name) || name.Contains('\\') || name.Contains(':'))
            throw new InvalidDataException("Release archive contains an unsafe path.");

        string[] segments = name.TrimEnd('/').Split('/');
        if (segments.Any(segment => segment is "" or "." or "..") || segments.Length > 2)
            throw new InvalidDataException("Release archive contains an unsafe path.");

        if (directory)
        {
            if (segments.Length != 1 || rootFileSeen || (commonDirectory is not null && commonDirectory != segments[0]))
                throw new InvalidDataException("Release archive contains an unexpected directory.");

            commonDirectory = segments[0];
            return null;
        }

        if (segments.Length == 2)
        {
            if (rootFileSeen || (commonDirectory is not null && commonDirectory != segments[0]))
                throw new InvalidDataException("Release archive contains multiple top-level directories.");

            commonDirectory = segments[0];
            return segments[1];
        }

        if (commonDirectory is not null)
            throw new InvalidDataException("Release archive mixes root files and nested files.");

        rootFileSeen = true;
        return segments[0];
    }

    private static void CheckTotal(long total)
    {
        if (total > MAX_EXTRACTED_BYTES) throw new InvalidDataException("Extracted release exceeds its size limit.");
    }

    private static void CheckEntryCount(int count)
    {
        if (count > MAX_ARCHIVE_ENTRIES)
            throw new InvalidDataException("MediaMTX package contains too many archive entries.");
    }

    internal static void ValidateArchiveType(string packagePath)
    {
        if (OperatingSystem.IsWindows() && packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return;

        if (OperatingSystem.IsLinux() && packagePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return;
        throw new InvalidDataException("MediaMTX package format does not match the current operating system.");
    }
}
