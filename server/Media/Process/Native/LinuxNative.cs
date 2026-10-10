using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Hsin.Media.Process.Native;

internal static class LinuxNative
{
    private const int SIGTERM = 15;

    public static void Terminate(int processId)
    {
        if (Kill(processId, SIGTERM) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to signal the owned MediaMTX process.");
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int processId, int signal);
}
