using System.Text;

namespace Hsin.Infrastructure;

internal static class AtomicFile
{
    private const int BUFFER_BYTES = 4096;

    public static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))
                           ?? throw new InvalidDataException("Output path has no directory.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (FileStream stream = new(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             BUFFER_BYTES,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

                byte[] bytes = Encoding.UTF8.GetBytes(content);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
