using System.Security.Cryptography;
using Hsin.Media.Process;
using SerilogLogger = Serilog.ILogger;

namespace Hsin.Media.Installation;

public sealed class MediaMtxInstallationService
{
    private const long MAX_ARCHIVE_BYTES             = 128 * 1024 * 1024;
    private const int  COPY_BUFFER_BYTES             = 64 * 1024;
    private const int  MOVE_RETRY_COUNT              = 10;
    private const int  MOVE_RETRY_DELAY_MILLISECONDS = 250;

    private readonly string                root;
    private readonly MediaMtxStateStore    stateStore;
    private readonly MediaMtxReleaseClient releases;
    private readonly SerilogLogger         logger;

    public MediaMtxInstallationService(
        string                dataDirectory,
        MediaMtxReleaseClient releases,
        SerilogLogger         logger)
    {
        root          = Path.GetFullPath(dataDirectory);
        stateStore    = new MediaMtxStateStore(root);
        this.releases = releases;
        this.logger   = logger.ForContext("Component", "MediaMTX.Installation");
    }

    public string GetExecutablePath(string installationId)
        => stateStore.ExecutablePath(installationId);

    public async Task<InstallationState> PrepareAsync(string? version, CancellationToken cancellationToken)
    {
        InstallationState state = await stateStore.ReadAsync(cancellationToken);
        ReleaseAsset      asset = await releases.ResolveAsync(version, cancellationToken);
        if (state.Active?.Id == asset.Version)
        {
            logger.Information("MediaMTX {Version} is already installed", asset.Version);
            return state;
        }

        logger.Information("Preparing MediaMTX {Version} from the official release", asset.Version);

        InstalledMediaVersion candidate = new()
        {
            Id              = asset.Version,
            ReportedVersion = asset.Version,
            Source          = MediaPackageSource.Official,
            ArchiveSha256   = asset.Sha256.ToLowerInvariant(),
            BinarySha256    = string.Empty
        };
        string destination = Path.GetDirectoryName(stateStore.ExecutablePath(candidate.Id))
                             ?? throw new InvalidOperationException("Version directory is absent.");
        string staging = Path.Combine(root, "staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            string archivePath = Path.Combine(staging, asset.Name);
            await releases.DownloadAsync(asset, archivePath, cancellationToken);
            InstallationState prepared = await PrepareArchiveAsync(
                state,
                candidate,
                archivePath,
                destination,
                staging,
                cancellationToken);
            logger.Information("Prepared MediaMTX {Version} for the next start", prepared.Pending?.ReportedVersion);
            return prepared;
        }
        finally
        {
            CleanupStaging(staging);
        }
    }

    public async Task<InstallationState> ImportAsync(string packagePath, CancellationToken cancellationToken)
    {
        string sourcePath = Path.GetFullPath(packagePath);
        MediaMtxPackageExtractor.ValidateArchiveType(sourcePath);
        FileInfo source = new(sourcePath);
        if (!source.Exists || source.Length is <= 0 or > MAX_ARCHIVE_BYTES)
            throw new InvalidDataException("MediaMTX package does not exist or exceeds the archive size limit.");

        logger.Information("Importing MediaMTX package {PackageName}", source.Name);

        InstallationState state   = await stateStore.ReadAsync(cancellationToken);
        string            staging = Path.Combine(root, "staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            string archivePath = Path.Combine(staging, Path.GetFileName(sourcePath));
            await using (FileStream input = File.OpenRead(sourcePath))
            await using (FileStream output = new(
                             archivePath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             COPY_BUFFER_BYTES,
                             FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            if (new FileInfo(archivePath).Length > MAX_ARCHIVE_BYTES)
                throw new InvalidDataException("MediaMTX package grew beyond the archive size limit while copying.");

            await using FileStream archive = File.OpenRead(archivePath);
            string checksum = Convert.ToHexString(await SHA256.HashDataAsync(archive, cancellationToken))
                                     .ToLowerInvariant();
            InstalledMediaVersion candidate = new()
            {
                Id              = "custom-" + checksum,
                ReportedVersion = "v0.0.0",
                Source          = MediaPackageSource.UserPackage,
                ArchiveSha256   = checksum,
                BinarySha256    = string.Empty
            };
            string destination = Path.GetDirectoryName(stateStore.ExecutablePath(candidate.Id))
                                 ?? throw new InvalidOperationException("Version directory is absent.");
            InstallationState prepared = await PrepareArchiveAsync(
                state,
                candidate,
                archivePath,
                destination,
                staging,
                cancellationToken);
            logger.Information(
                "Prepared imported MediaMTX {Version} for the next start",
                prepared.Pending?.ReportedVersion);
            return prepared;
        }
        catch (Exception importError)
        {
            try
            {
                InstallationState failed = await stateStore.ReadAsync(cancellationToken);
                if (failed.Pending?.Source == MediaPackageSource.UserPackage)
                {
                    await stateStore.WriteAsync(failed with { Pending = null }, cancellationToken);
                    RemoveFailedImport(failed);
                }
            }
            catch (Exception cleanupError)
            {
                throw new IOException(
                    "MediaMTX import failed and its pending installation could not be cleaned up.",
                    new AggregateException(importError, cleanupError));
            }

            throw;
        }
        finally
        {
            CleanupStaging(staging);
        }
    }

    private async Task<InstallationState> PrepareArchiveAsync(
        InstallationState     state,
        InstalledMediaVersion candidate,
        string                archivePath,
        string                destination,
        string                staging,
        CancellationToken     cancellationToken)
    {
        string extracted = Path.Combine(staging, "extracted");
        Directory.CreateDirectory(extracted);
        await MediaMtxPackageExtractor.ExtractAsync(archivePath, extracted, cancellationToken);
        string executable = Path.Combine(extracted, OperatingSystem.IsWindows() ? "mediamtx.exe" : "mediamtx");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                executable,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute);

        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? root);
        bool moved = false;
        if (!Directory.Exists(destination))
        {
            await MoveDirectoryAsync(extracted, destination, cancellationToken);
            moved = true;
        }
        else if (state.Active?.Id != candidate.Id
                 && state.Previous?.Id != candidate.Id
                 && state.Pending?.Id != candidate.Id)
        {
            throw new IOException(
                $"MediaMTX candidate directory already exists: {destination}. Inspect it before retrying.");
        }

        try
        {
            string installedExecutable = stateStore.ExecutablePath(candidate.Id);
            string actualVersion = (await ProcessUtils.GetVersionAsync(
                installedExecutable,
                cancellationToken)).Trim();
            MediaMtxReleaseClient.ValidateVersion(actualVersion);
            if (candidate.Source == MediaPackageSource.Official && actualVersion != candidate.ReportedVersion)
                throw new InvalidDataException("Downloaded binary reports a different version.");

            string binarySha256;
            await using (FileStream binary = File.OpenRead(installedExecutable))
            {
                binarySha256 = Convert.ToHexString(await SHA256.HashDataAsync(binary, cancellationToken))
                                      .ToLowerInvariant();
            }

            InstalledMediaVersion verified = candidate with
            {
                ReportedVersion = actualVersion,
                BinarySha256 = binarySha256
            };
            if (state.Active?.Id == verified.Id) return state;

            InstallationState prepared = state with { Pending = verified };
            await stateStore.WriteAsync(prepared, cancellationToken);
            return prepared;
        }
        catch
        {
            if (!moved || !Directory.Exists(destination)) throw;
            try
            {
                Directory.Delete(destination, true);
            }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
            {
                logger.Warning(
                    cleanupError,
                    "Unable to remove rejected MediaMTX installation {InstallationId}",
                    candidate.Id);
            }

            throw;
        }
    }

    public async Task<InstallationState> EnsureAsync(CancellationToken cancellationToken)
    {
        InstallationState state = await stateStore.ReadAsync(cancellationToken);
        if (state.Active is null && state.Pending is null)
        {
            logger.Information("No MediaMTX installation found; preparing the default release");
            return await PrepareAsync(MediaMtxReleaseClient.DEFAULT_VERSION, cancellationToken);
        }

        logger.Information(
            "Using installed MediaMTX {Version}",
            state.Pending?.ReportedVersion ?? state.Active?.ReportedVersion);
        return state;
    }

    public async Task VerifyInstalledBinaryAsync(InstalledMediaVersion version, CancellationToken cancellationToken)
    {
        string executable = stateStore.ExecutablePath(version.Id);
        if (!File.Exists(executable))
            throw new FileNotFoundException("MediaMTX executable is absent.", executable);

        byte[] actualHash;
        await using (FileStream binary = File.OpenRead(executable))
        {
            actualHash = await SHA256.HashDataAsync(binary, cancellationToken);
        }

        byte[] expectedHash = Convert.FromHexString(version.BinarySha256);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            throw new InvalidDataException(
                $"MediaMTX binary {version.Id} does not match its recorded SHA-256 checksum.");

        string actualVersion = (await ProcessUtils.GetVersionAsync(
            executable,
            cancellationToken)).Trim();
        if (!string.Equals(actualVersion, version.ReportedVersion, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"MediaMTX binary {version.Id} reports {actualVersion} instead of {version.ReportedVersion}.");

        logger.Information("Verified MediaMTX {Version} binary", version.ReportedVersion);
    }

    public async Task ClearPendingAsync(CancellationToken cancellationToken)
    {
        InstallationState state = await stateStore.ReadAsync(cancellationToken);
        await stateStore.WriteAsync(state with { Pending = null }, cancellationToken);
    }

    public async Task CommitReadyAsync(InstalledMediaVersion selected, CancellationToken cancellationToken)
    {
        InstallationState state = await stateStore.ReadAsync(cancellationToken);
        if (state.Pending is null) return;
        if (state.Pending.Id != selected.Id)
            throw new InvalidDataException("The prepared MediaMTX version changed during startup.");

        InstallationState committed = state with
        {
            Active = selected,
            Previous = state.Active,
            Pending = null
        };
        await stateStore.WriteAsync(committed, cancellationToken);
        logger.Information("Activated MediaMTX {Version}", selected.ReportedVersion);
    }

    public async Task ClearFailedImportAsync(InstallationState state, CancellationToken cancellationToken)
    {
        await stateStore.WriteAsync(state with { Pending = null }, cancellationToken);
        try
        {
            RemoveFailedImport(state);
        }
        catch (Exception exception)
        {
            throw new IOException(
                $"Failed to remove rejected MediaMTX package {state.Pending?.Id}.",
                exception);
        }
    }

    public async Task PrepareRollbackAsync(CancellationToken cancellationToken)
    {
        InstallationState state = await stateStore.ReadAsync(cancellationToken);
        if (state.Previous is null)
            throw new InvalidOperationException("There is no previous MediaMTX version to restore.");

        await stateStore.WriteAsync(state with { Pending = state.Previous }, cancellationToken);
        logger.Information("Prepared rollback to MediaMTX {Version}", state.Previous.ReportedVersion);
    }

    public void RemoveFailedImport(InstallationState state)
    {
        InstalledMediaVersion? pending = state.Pending;
        if (pending?.Source != MediaPackageSource.UserPackage
            || state.Active?.Id == pending.Id
            || state.Previous?.Id == pending.Id)
            return;

        string directory = Path.GetDirectoryName(stateStore.ExecutablePath(pending.Id))
                           ?? throw new InvalidOperationException("MediaMTX version directory is absent.");
        string versionsRoot = Path.GetFullPath(Path.Combine(root, "versions")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(directory).StartsWith(versionsRoot, StringComparison.Ordinal))
            throw new InvalidOperationException("MediaMTX version directory escapes its data root.");

        if (Directory.Exists(directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("MediaMTX version directory is a link.");
            Directory.Delete(directory, true);
        }
    }

    private void CleanupStaging(string staging)
    {
        // 只清理本次创建且仍位于数据目录 staging 下的目录。
        string stagingRoot = Path.GetFullPath(Path.Combine(root, "staging")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(staging).StartsWith(stagingRoot, StringComparison.Ordinal) && Directory.Exists(staging))
            Directory.Delete(staging, true);
    }

    private async Task MoveDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        for (int attempt = 1;; attempt++)
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception exception) when (
                exception is (IOException or UnauthorizedAccessException) and not DirectoryNotFoundException
                && attempt < MOVE_RETRY_COUNT)
            {
                if (attempt == 1)
                    logger.Warning("Waiting for the MediaMTX installation directory to become available");
                await Task.Delay(TimeSpan.FromMilliseconds(MOVE_RETRY_DELAY_MILLISECONDS), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"Unable to move the MediaMTX installation directory from '{source}' to '{destination}'. Check that the data directory is writable and that no process is using the extracted files.",
                    exception);
            }
    }
}
