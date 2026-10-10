using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Formats.Tar;
using Hsin.Media;
using Hsin.Media.Installation;

namespace Hsin.Tests;

public sealed class MediaMtxInstallationTests
{
    [Fact]
    public async Task DownloadRejectsIncorrectOfficialDigest()
    {
        byte[]                payload = "downloaded-content"u8.ToArray();
        using HttpClient      http    = new(new AssetHandler(payload));
        MediaMtxReleaseClient client  = new(http);
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hsin-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ReleaseAsset asset = new()
            {
                Name        = "mediamtx_v1.21.1_windows_amd64.zip",
                Version     = "v1.21.1",
                DownloadUrl = new Uri("https://github.com/bluenviron/mediamtx/releases/download/v1.21.1/test.zip"),
                Sha256      = Convert.ToHexString(SHA256.HashData("different-content"u8)),
                Size        = payload.Length
            };
            await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(
                                                               asset,
                                                               Path.Combine(
                                                                   directory,
                                                                   "candidate.zip"),
                                                               CancellationToken.None));
            Assert.False(
                File.Exists(
                    Path.Combine(
                        directory,
                        "state.json")));
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task ArchiveTraversalIsRejectedBeforeExtraction()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hsin-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string archivePath = Path.Combine(
                directory,
                "candidate.zip");
            using (ZipArchive archive = ZipFile.Open(
                       archivePath,
                       ZipArchiveMode.Create))
            {
                ZipArchiveEntry    entry  = archive.CreateEntry("../escaped.txt");
                await using Stream stream = entry.Open();
                await stream.WriteAsync("unsafe"u8.ToArray());
            }

            string destination = Path.Combine(
                directory,
                "extracted");
            Directory.CreateDirectory(destination);
            await Assert.ThrowsAsync<InvalidDataException>(() => MediaMtxPackageExtractor.ExtractAsync(
                                                               archivePath,
                                                               destination,
                                                               CancellationToken
                                                                   .None));
            Assert.Empty(Directory.EnumerateFiles(destination));
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Theory]
    [InlineData(
        "mediamtx.exe",
        "mediamtx.yml",
        "LICENSE",
        "extra.txt")]
    [InlineData(
        "mediamtx.exe",
        "mediamtx.yml",
        "LICENSE",
        "")]
    public async Task ZipImportRequiresExactlyThreeExpectedFiles(
        string executable,
        string configuration,
        string license,
        string extra)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hsin-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string archivePath = Path.Combine(
                directory,
                "candidate.zip");
            using (ZipArchive archive = ZipFile.Open(
                       archivePath,
                       ZipArchiveMode.Create))
            {
                Add(
                    executable,
                    "binary");
                Add(
                    configuration,
                    "paths: {}");
                Add(
                    license,
                    "MIT");
                if (extra.Length > 0)
                    Add(
                        extra,
                        "unexpected");

                void Add(string name, string value)
                {
                    ZipArchiveEntry    entry  = archive.CreateEntry(name);
                    using StreamWriter writer = new(entry.Open());
                    writer.Write(value);
                }
            }

            string destination = Path.Combine(
                directory,
                "extracted");
            Directory.CreateDirectory(destination);
            if (extra.Length > 0)
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => MediaMtxPackageExtractor.ExtractAsync(
                                                                   archivePath,
                                                                   destination,
                                                                   CancellationToken
                                                                       .None));
            }
            else
            {
                await MediaMtxPackageExtractor.ExtractAsync(
                    archivePath,
                    destination,
                    CancellationToken.None);
                Assert.Equal(
                    3,
                    Directory.EnumerateFiles(destination).Count());
            }
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    [Fact]
    public async Task TarImportRejectsMissingLicense()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hsin-tar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string archivePath = Path.Combine(
                directory,
                "candidate.tar.gz");
            await using (FileStream file = File.Create(archivePath))
            await using (GZipStream gzip = new(
                             file,
                             CompressionMode.Compress))
            using (TarWriter writer = new(gzip))
            {
                await writer.WriteEntryAsync(
                    new PaxTarEntry(
                        TarEntryType.RegularFile,
                        "mediamtx")
                    {
                        DataStream = new MemoryStream("binary"u8.ToArray())
                    });
                await writer.WriteEntryAsync(
                    new PaxTarEntry(
                        TarEntryType.RegularFile,
                        "mediamtx.yml")
                    {
                        DataStream = new MemoryStream("paths: {}"u8.ToArray())
                    });
            }

            string destination = Path.Combine(
                directory,
                "extracted");
            Directory.CreateDirectory(destination);
            await Assert.ThrowsAsync<InvalidDataException>(() => MediaMtxPackageExtractor.ExtractAsync(
                                                               archivePath,
                                                               destination,
                                                               CancellationToken
                                                                   .None));
        }
        finally
        {
            Directory.Delete(
                directory,
                true);
        }
    }

    private sealed class AssetHandler : HttpMessageHandler
    {
        private readonly byte[] payload;

        public AssetHandler(byte[] payload)
        {
            this.payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken  cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content        = new ByteArrayContent(payload)
                });
    }
}
