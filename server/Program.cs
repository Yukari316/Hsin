using Hsin.Application;
using Hsin.Media.Process;
using Serilog;

namespace Hsin;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
        try
        {
            CommandOptions command = CommandLine.Parse(args);
            if (command.Kind == CommandKind.Guardian)
                return await LinuxMtxGuardian.RunAsync(
                    command.MediaExecutable
                    ?? throw new InvalidOperationException("The MediaMTX executable path is absent."),
                    command.MediaConfiguration
                    ?? throw new InvalidOperationException("The MediaMTX configuration path is absent."));

            CommandDispatcher dispatcher = new();
            return await dispatcher.ExecuteAsync(command);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Hsin stopped before completing startup or operation");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
