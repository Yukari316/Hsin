using System.Diagnostics;
using SystemProcess = System.Diagnostics.Process;
using SerilogLogger = Serilog.ILogger;

namespace Hsin.Media.Process;

internal static class ProcessUtils
{
    private const int VERSION_TIMEOUT_SECONDS = 5;

    public static SystemProcess Start(
        string                executable,
        IReadOnlyList<string> arguments,
        string                workingDirectory,
        bool                  redirectStandardInput,
        bool                  redirectStandardOutput,
        bool                  redirectStandardError)
    {
        ProcessStartInfo startInfo = new(executable)
        {
            WorkingDirectory       = workingDirectory,
            UseShellExecute        = false,
            RedirectStandardInput  = redirectStandardInput,
            RedirectStandardOutput = redirectStandardOutput,
            RedirectStandardError  = redirectStandardError,
            CreateNoWindow         = true
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        SystemProcess process = SystemProcess.Start(startInfo)
                                ?? throw new IOException("Process did not start.");
        if (redirectStandardInput)
            process.StandardInput.AutoFlush = true;
        return process;
    }

    public static async Task<string> GetVersionAsync(
        string            executable,
        CancellationToken cancellationToken)
    {
        SystemProcess process = Start(
            executable,
            ["--version"],
            Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            false,
            true,
            true);
        using SystemProcess           ownedProcess = process;
        using CancellationTokenSource timeout      = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(VERSION_TIMEOUT_SECONDS));
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> errorTask  = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(true);
            throw;
        }

        string output = await outputTask;
        string error  = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidDataException($"MediaMTX version probe exited with {process.ExitCode}: {error.Trim()}");
        return output.Trim();
    }

    public static async Task PumpAsync(
        StreamReader  reader,
        SerilogLogger logger,
        bool          warning)
    {
        while (await reader.ReadLineAsync() is string line)
            if (warning)
                logger.Warning("{ProcessMessage}", line);
            else
                logger.Information("{ProcessMessage}", line);
    }

    public static async Task WaitForExitAsync(
        SystemProcess     process,
        TimeSpan          timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        await process.WaitForExitAsync(linked.Token);
    }

    public static async Task StopProcessTreeAsync(
        SystemProcess     process,
        TimeSpan          timeout,
        CancellationToken cancellationToken)
    {
        if (!process.HasExited)
            process.Kill(true);

        try
        {
            await WaitForExitAsync(process, timeout, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(true);
            await process.WaitForExitAsync(cancellationToken);
        }
    }
}
