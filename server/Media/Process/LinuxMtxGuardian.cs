using System.ComponentModel;
using System.Diagnostics;
using Hsin.Media.Process.Native;
using Serilog;
using Serilog.Core;
using SystemProcess = System.Diagnostics.Process;
using SerilogLogger = Serilog.ILogger;

namespace Hsin.Media.Process;

internal static class LinuxMtxGuardian
{
    private const int MEDIA_STOP_TIMEOUT_SECONDS = 5;

    public static async Task<int> RunAsync(string executable, string configurationPath)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("LinuxMtxGuardian is available only on Linux.");

        await using Logger logger = new LoggerConfiguration()
                                    .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}{Exception}")
                                    .CreateLogger();
        SystemProcess media = ProcessUtils.Start(
            executable,
            [configurationPath],
            Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            false,
            true,
            true);
        Task output = ProcessUtils.PumpAsync(
            media.StandardOutput,
            logger.ForContext("Component", "MediaMTX"),
            false);
        Task error = ProcessUtils.PumpAsync(
            media.StandardError,
            logger.ForContext("Component", "MediaMTX"),
            true);

        try
        {
            Task<string?> parentInput = Task.Run(Console.In.ReadLine);
            Task          mediaExit   = media.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAny(parentInput, mediaExit);
            if (media.HasExited)
                return media.ExitCode;

            string? command = await parentInput;
            if (command is not (null or "STOP"))
                throw new InvalidDataException("Unexpected LinuxMtxGuardian control message.");

            logger.Information(
                "Guardian stopping owned MediaMTX process {MediaProcessId}",
                media.Id);
            try
            {
                LinuxNative.Terminate(media.Id);
            }
            catch (Exception exception) when (exception is Win32Exception)
            {
                logger.Warning(exception, "Unable to signal owned MediaMTX process {MediaProcessId}", media.Id);
            }

            try
            {
                await ProcessUtils.WaitForExitAsync(
                    media,
                    TimeSpan.FromSeconds(MEDIA_STOP_TIMEOUT_SECONDS),
                    CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                logger.Warning("MediaMTX process {MediaProcessId} did not stop in time", media.Id);
                await ProcessUtils.StopProcessTreeAsync(
                    media,
                    TimeSpan.FromSeconds(MEDIA_STOP_TIMEOUT_SECONDS),
                    CancellationToken.None);
            }

            return media.ExitCode;
        }
        finally
        {
            if (!media.HasExited)
                await ProcessUtils.StopProcessTreeAsync(
                    media,
                    TimeSpan.FromSeconds(MEDIA_STOP_TIMEOUT_SECONDS),
                    CancellationToken.None);
            await Task.WhenAll(output, error);
            media.Dispose();
        }
    }
}
