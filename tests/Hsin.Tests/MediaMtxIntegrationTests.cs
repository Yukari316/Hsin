using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Formats.Tar;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Net.Http.Headers;
using Hsin.Configuration;
using Hsin.Media;
using Hsin.Media.Control;
using Hsin.Media.Configuration;
using Hsin.Media.Installation;
using Serilog;
using Serilog.Core;
using Xunit.Sdk;

namespace Hsin.Tests;

public sealed class MediaMtxIntegrationTests
{
    [MediaIntegrationFact]
    [Trait(
        "Category",
        "MediaIntegration")]
    public async Task BinaryVersionMismatchFailsBeforeStartingMedia()
    {
        string executable = Environment.GetEnvironmentVariable("HSIN_TEST_MEDIAMTX")
                            ?? throw new InvalidOperationException("MediaMTX test executable is missing.");
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hsin-version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string versionDirectory = Path.Combine(
                directory,
                "data",
                "mediamtx",
                "versions",
                "v1.21.1");
            Directory.CreateDirectory(versionDirectory);
            string mediaExecutable = Path.Combine(
                versionDirectory,
                OperatingSystem.IsWindows() ? "mediamtx.exe" : "mediamtx");
            File.Copy(
                executable,
                mediaExecutable);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(
                    mediaExecutable,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            InstallationState state = new()
            {
                Active = new InstalledMediaVersion
                {
                    Id              = "v1.21.1",
                    ReportedVersion = "v9.9.9",
                    Source          = MediaPackageSource.Official,
                    ArchiveSha256 = new string(
                        '0',
                        64),
                    BinarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable)))
                                          .ToLowerInvariant()
                }
            };
            await File.WriteAllTextAsync(
                Path.Combine(
                    directory,
                    "data",
                    "mediamtx",
                    "state.json"),
                JsonSerializer.Serialize(state));
            string configPath = Path.Combine(
                directory,
                "hsin.json");
            await File.WriteAllTextAsync(
                configPath,
                """
                { "Logging": { "FileEnabled": false } }
                """);

            ProcessStartInfo start = new("dotnet")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add(configPath);
            using Process hsin   = Process.Start(start) ?? throw new IOException("Hsin test process did not start.");
            Task<string>  output = hsin.StandardOutput.ReadToEndAsync();
            Task<string>  errors = hsin.StandardError.ReadToEndAsync();
            try
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
                await hsin.WaitForExitAsync(timeout.Token);
            }
            finally
            {
                if (!hsin.HasExited)
                {
                    hsin.Kill(true);
                    await hsin.WaitForExitAsync();
                }
            }

            string log = await output + await errors;
            Assert.Equal(
                1,
                hsin.ExitCode);
            Assert.Contains(
                "reports v1.21.1 instead of v9.9.9",
                log,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Started owned MediaMTX process",
                log,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Started MediaMTX guardian process",
                log,
                StringComparison.Ordinal);
        }
        finally
        {
            ClearAttributes(directory);
            Directory.Delete(directory, true);
        }
    }

    [MediaIntegrationFact]
    [Trait(
        "Category",
        "MediaIntegration")]
    public async Task LinuxGuardianStartsMediaBeforeStopOrEof()
    {
        if (!OperatingSystem.IsLinux()) return;

        string executable = Environment.GetEnvironmentVariable("HSIN_TEST_MEDIAMTX")
                            ?? throw new InvalidOperationException("MediaMTX test executable is missing.");
        foreach (bool sendStop in new[] { true, false })
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "hsin-guardian-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                int mediaPort   = AvailablePort();
                int controlPort = AvailablePort();
                string configPath = Path.Combine(
                    directory,
                    "hsin.json");
                HsinOptions options = new()
                {
                    Publish = new PublishOptions { BearerToken = "publisher:test-secret" },
                    Logging = new LoggingOptions { FileEnabled = false },
                    MediaMtx = new MediaMtxOptions
                    {
                        ListenAddress = "127.0.0.1",
                        Port          = mediaPort,
                        PublicOrigin  = $"http://127.0.0.1:{mediaPort}",
                        ControlApi    = new ControlApiOptions { Port = controlPort }
                    }
                };
                await File.WriteAllTextAsync(
                    configPath,
                    JsonSerializer.Serialize(options));
                using ResolvedConfiguration configuration = await ConfigurationLoader.LoadAsync(configPath);
                string generated = Path.Combine(
                    directory,
                    "mediamtx.yml");
                await MediaMtxConfiguration.WriteAsync(
                    configuration,
                    generated);

                ProcessStartInfo start = new("dotnet")
                {
                    UseShellExecute        = false,
                    RedirectStandardInput  = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };
                start.ArgumentList.Add(typeof(Program).Assembly.Location);
                start.ArgumentList.Add("--media-guardian");
                start.ArgumentList.Add(executable);
                start.ArgumentList.Add(generated);
                using Process guardian = Process.Start(start)
                                         ?? throw new IOException("MediaMTX guardian test process did not start.");
                Task<string> output = guardian.StandardOutput.ReadToEndAsync();
                Task<string> errors = guardian.StandardError.ReadToEndAsync();
                try
                {
                    using MediaMtxApiClient       api     = new(configuration);
                    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
                    while (true)
                    {
                        Assert.False(guardian.HasExited);
                        try
                        {
                            if (await api.HasLivePathConfigurationAsync(timeout.Token)) break;
                        }
                        catch (HttpRequestException)
                        {
                        }

                        await Task.Delay(
                            TimeSpan.FromMilliseconds(100),
                            timeout.Token);
                    }

                    if (sendStop) await guardian.StandardInput.WriteLineAsync("STOP");
                    guardian.StandardInput.Close();
                    await guardian.WaitForExitAsync(timeout.Token);
                    Assert.Equal(
                        0,
                        guardian.ExitCode);
                    using TcpClient socket = new();
                    await Assert.ThrowsAnyAsync<SocketException>(() => socket.ConnectAsync(
                                                                     IPAddress.Loopback,
                                                                     controlPort));
                }
                finally
                {
                    if (!guardian.HasExited)
                    {
                        guardian.Kill(true);
                        await guardian.WaitForExitAsync();
                    }

                    await Task.WhenAll(
                        output,
                        errors);
                }
            }
            finally
            {
                Directory.Delete(
                    directory,
                    true);
            }
        }
    }

    [MediaIntegrationFact]
    [Trait(
        "Category",
        "MediaIntegration")]
    public async Task LocalOfficialArchiveCanBeImportedAsCurrentInstallation()
    {
        string executable = Environment.GetEnvironmentVariable("HSIN_TEST_MEDIAMTX")
                            ?? throw new InvalidOperationException("MediaMTX test executable is missing.");
        string sourceDirectory = Path.GetDirectoryName(executable)
                                 ?? throw new InvalidOperationException("MediaMTX test directory is missing.");
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hsin-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string package = Path.Combine(
                directory,
                OperatingSystem.IsWindows() ? "media.zip" : "media.tar.gz");
            string   executableName = OperatingSystem.IsWindows() ? "mediamtx.exe" : "mediamtx";
            string[] names          = [executableName, "mediamtx.yml", "LICENSE"];
            if (OperatingSystem.IsWindows())
            {
                using ZipArchive zip = ZipFile.Open(
                    package,
                    ZipArchiveMode.Create);
                foreach (string name in names)
                    zip.CreateEntryFromFile(
                        Path.Combine(
                            sourceDirectory,
                            name),
                        name);
            }
            else
            {
                await using FileStream file = File.Create(package);
                await using GZipStream gzip = new(
                    file,
                    CompressionMode.Compress);
                using TarWriter tar = new(gzip);
                foreach (string name in names)
                {
                    await using FileStream input = File.OpenRead(
                        Path.Combine(
                            sourceDirectory,
                            name));
                    await tar.WriteEntryAsync(
                        new PaxTarEntry(
                            TarEntryType.RegularFile,
                            name)
                        {
                            DataStream = input
                        });
                }
            }

            using HttpClient http = new();
            MediaMtxInstallationService installation = new(
                Path.Combine(
                    directory,
                    "data"),
                new MediaMtxReleaseClient(http),
                new LoggerConfiguration().CreateLogger());
            InstallationState pending = await installation.ImportAsync(
                package,
                CancellationToken.None);
            InstalledMediaVersion imported = pending.Pending
                                             ?? throw new XunitException("The archive was not prepared.");
            Assert.StartsWith(
                "custom-",
                imported.Id);
            Assert.Equal(
                "v1.21.1",
                imported.ReportedVersion);
            Assert.Equal(
                MediaPackageSource.UserPackage,
                imported.Source);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))).ToLowerInvariant(),
                imported.BinarySha256);
            Assert.True(File.Exists(installation.GetExecutablePath(imported.Id)));
            await installation.CommitReadyAsync(imported, CancellationToken.None);
            InstallationState repeated = await installation.ImportAsync(
                package,
                CancellationToken.None);
            Assert.Equal(
                imported.Id,
                repeated.Active?.Id);
            Assert.Null(repeated.Pending);
        }
        finally
        {
            ClearAttributes(directory);
            Directory.Delete(directory, true);
        }
    }

    [MediaIntegrationFact]
    [Trait("Category", "MediaIntegration")]
    public async Task OfficialDownloadMovesArchiveBeforeVersionProbe()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string executable = Environment.GetEnvironmentVariable("HSIN_TEST_MEDIAMTX")
                            ?? throw new InvalidOperationException("MediaMTX test executable is missing.");
        string sourceDirectory = Path.GetDirectoryName(executable)
                                 ?? throw new InvalidOperationException("MediaMTX test directory is missing.");
        string directory = Path.Combine(Path.GetTempPath(), "hsin-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string package = Path.Combine(directory, "mediamtx_v1.21.1_windows_amd64.zip");
            using (ZipArchive zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                foreach (string name in new[] { "mediamtx.exe", "mediamtx.yml", "LICENSE" })
                    zip.CreateEntryFromFile(Path.Combine(sourceDirectory, name), name);
            }

            byte[]           archive  = await File.ReadAllBytesAsync(package);
            string           checksum = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
            using HttpClient http     = new(new OfficialDownloadHandler(archive, checksum));
            using Logger     logger   = new LoggerConfiguration().CreateLogger();
            MediaMtxInstallationService installation = new(
                Path.Combine(directory, "data"),
                new MediaMtxReleaseClient(http),
                logger);

            InstallationState     state   = await installation.PrepareAsync("v1.21.1", CancellationToken.None);
            InstalledMediaVersion pending = Assert.IsType<InstalledMediaVersion>(state.Pending);
            Assert.Equal("v1.21.1", pending.ReportedVersion);
            Assert.Equal(checksum, pending.ArchiveSha256);
            Assert.True(File.Exists(installation.GetExecutablePath(pending.Id)));
            await installation.VerifyInstalledBinaryAsync(pending, CancellationToken.None);
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(directory, "data", "staging")));

            using HttpClient wrongVersionHttp = new(new OfficialDownloadHandler(archive, checksum, "v9.9.9"));
            MediaMtxInstallationService wrongVersionInstallation = new(
                Path.Combine(directory, "wrong-version"),
                new MediaMtxReleaseClient(wrongVersionHttp),
                logger);
            await Assert.ThrowsAsync<InvalidDataException>(() => wrongVersionInstallation.PrepareAsync(
                                                               "v9.9.9",
                                                               CancellationToken.None));
            Assert.False(File.Exists(wrongVersionInstallation.GetExecutablePath("v9.9.9")));
            Assert.False(File.Exists(Path.Combine(directory, "wrong-version", "state.json")));
        }
        finally
        {
            ClearAttributes(directory);
            await DeleteWithRetryAsync(directory);
        }
    }

    [MediaIntegrationFact]
    [Trait(
        "Category",
        "MediaIntegration")]
    public async Task RealMediaProcessLoadsUnicodeCredentialAndCustomControlPort()
    {
        string executable = Environment.GetEnvironmentVariable("HSIN_TEST_MEDIAMTX")
                            ?? throw new InvalidOperationException("MediaMTX test executable is missing.");
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hsin-real-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Process? process = null;
        try
        {
            int mediaPort   = AvailablePort();
            int controlPort = AvailablePort();
            string configPath = Path.Combine(
                directory,
                "hsin.json");
            HsinOptions options = new()
            {
                WebUi = new WebUiOptions
                {
                    Token = "中文观看密码"
                },
                Logging = new LoggingOptions
                {
                    FileEnabled = false
                },
                MediaMtx = new MediaMtxOptions
                {
                    ListenAddress = "127.0.0.1",
                    Port          = mediaPort,
                    PublicOrigin  = $"http://localhost:{mediaPort}",
                    ControlApi = new ControlApiOptions
                    {
                        Port     = controlPort,
                        Password = "separate-control-secret"
                    }
                }
            };
            await File.WriteAllTextAsync(
                configPath,
                JsonSerializer.Serialize(options));
            string assembly = typeof(Program).Assembly.Location;
            ProcessStartInfo start = new("dotnet")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };
            start.ArgumentList.Add(assembly);
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add(configPath);
            start.ArgumentList.Add("--mediamtx-package");
            start.ArgumentList.Add(
                Environment.GetEnvironmentVariable("HSIN_TEST_MEDIAMTX_PACKAGE")
                ?? Path.Combine(
                    Path.GetDirectoryName(executable)
                    ?? throw new InvalidOperationException("MediaMTX test directory is missing."),
                    "..",
                    "..",
                    "mediamtx_v1.21.1_windows_amd64.zip"));
            process = Process.Start(start)
                      ?? throw new IOException("Hsin test process did not start.");
            Task<string>     output = process.StandardOutput.ReadToEndAsync();
            Task<string>     errors = process.StandardError.ReadToEndAsync();
            using HttpClient api    = new();
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hsin-control:separate-control-secret")));
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
            while (true)
            {
                Assert.False(process.HasExited);
                try
                {
                    using HttpResponseMessage response = await api.GetAsync(
                        $"http://127.0.0.1:{controlPort}/v3/config/paths/get/live",
                        timeout.Token);
                    if (response.IsSuccessStatusCode)
                        break;
                    await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
                }
            }

            using HttpResponseMessage pathResponse = await api.GetAsync(
                $"http://127.0.0.1:{controlPort}/v3/paths/get/live",
                timeout.Token);
            string pathJson = await pathResponse.Content.ReadAsStringAsync(timeout.Token);
            Assert.Contains("\"online\":false", pathJson, StringComparison.OrdinalIgnoreCase);
            using HttpClient viewer = new();
            viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("viewer:中文观看密码")));
            using HttpResponseMessage forbiddenApi = await viewer.GetAsync(
                $"http://127.0.0.1:{controlPort}/v3/info",
                timeout.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, forbiddenApi.StatusCode);
            process.Kill(true);
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, errors);
            await WaitForPortClosedAsync(controlPort);
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync();
                }

                process.Dispose();
            }

            ClearAttributes(directory);
            await DeleteWithRetryAsync(directory);
        }
    }

    private static int AvailablePort()
    {
        TcpListener listener = new(
            IPAddress.Loopback,
            0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void ClearAttributes(string directory)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
            File.SetAttributes(path, FileAttributes.Normal);
        File.SetAttributes(directory, FileAttributes.Normal);
    }

    private static async Task DeleteWithRetryAsync(string directory)
    {
        for (int attempt = 0;; attempt++)
            try
            {
                Directory.Delete(directory, true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < 20)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
    }

    private sealed class OfficialDownloadHandler : HttpMessageHandler
    {
        private readonly byte[] archive;
        private readonly string checksum;
        private readonly string releaseVersion;

        public OfficialDownloadHandler(
            byte[] archive,
            string checksum,
            string releaseVersion = "v1.21.1")
        {
            this.archive        = archive;
            this.checksum       = checksum;
            this.releaseVersion = releaseVersion;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken  cancellationToken)
        {
            HttpContent content;
            if (request.RequestUri?.Host == "api.github.com")
            {
                string body = JsonSerializer.Serialize(
                    new
                    {
                        draft      = false,
                        prerelease = false,
                        tag_name   = releaseVersion,
                        assets = new[]
                        {
                            new
                            {
                                name = $"mediamtx_{releaseVersion}_windows_amd64.zip",
                                browser_download_url =
                                    $"https://github.com/bluenviron/mediamtx/releases/download/{releaseVersion}/mediamtx_{releaseVersion}_windows_amd64.zip",
                                size   = archive.Length,
                                digest = "sha256:" + checksum
                            }
                        }
                    });
                content = new StringContent(body);
            }
            else
            {
                content = new ByteArrayContent(archive);
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content        = content
                });
        }
    }

    private static async Task WaitForPortClosedAsync(int port)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            using TcpClient socket = new();
            try
            {
                await socket.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            }
            catch (SocketException)
            {
                return;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }

        throw new XunitException($"Port {port} remained open after Hsin stopped.");
    }
}
