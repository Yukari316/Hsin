using Hsin.Media.Process.Native;

namespace Hsin.Tests;

public sealed class ProcessLifetimeTests
{
    private const int EXIT_TIMEOUT_SECONDS = 10;
    private const int CHILD_WAIT_SECONDS   = 120;

    [Fact]
    public async Task ClosingOwnedJobTerminatesChildProcess()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        string script = Path.Combine(
            Path.GetTempPath(),
            "hsin-process-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(script, $"Start-Sleep -Seconds {CHILD_WAIT_SECONDS}");
        WindowsNative.StartedProcess owned =
            WindowsNative.StartInJob(powershell, script);
        try
        {
            Assert.False(owned.Process.HasExited);
            owned.TerminateJob();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(EXIT_TIMEOUT_SECONDS));
            await owned.Process.WaitForExitAsync(timeout.Token);
            Assert.True(owned.Process.HasExited);
        }
        finally
        {
            owned.Dispose();
            File.Delete(script);
        }
    }
}
