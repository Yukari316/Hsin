using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SystemProcess = System.Diagnostics.Process;

namespace Hsin.Media.Process.Native;

internal static class WindowsNative
{
    private const uint CREATE_SUSPENDED                      = 0x00000004;
    private const uint CREATE_NO_WINDOW                      = 0x08000000;
    private const uint EXTENDED_STARTUPINFO_PRESENT          = 0x00080000;
    private const uint STARTF_USESTDHANDLES                  = 0x00000100;
    private const uint HANDLE_FLAG_INHERIT                   = 0x00000001;
    private const int  PROC_THREAD_ATTRIBUTE_HANDLE_LIST     = 0x00020002;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE    = 0x00002000;
    private const int  JOB_OBJECT_EXTENDED_LIMIT_INFORMATION = 9;
    private const uint WAIT_TIMEOUT_MILLISECONDS             = 5000;
    private const int  PIPE_BUFFER_BYTES                     = 4096;

    public static StartedProcess StartInJob(string executable, string configurationPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows process ownership requires Windows.");

        JobHandle       job                      = CreateOwnedJob();
        SafeFileHandle? stdinRead                = null;
        SafeFileHandle? stdinWrite               = null;
        SafeFileHandle? stdoutRead               = null;
        SafeFileHandle? stdoutWrite              = null;
        SafeFileHandle? stderrRead               = null;
        SafeFileHandle? stderrWrite              = null;
        SafeFileHandle? processHandle            = null;
        SafeFileHandle? threadHandle             = null;
        IntPtr          attributeList            = IntPtr.Zero;
        IntPtr          handleList               = IntPtr.Zero;
        bool            attributeListInitialized = false;
        SystemProcess?  process                  = null;
        StreamReader?   stdout                   = null;
        StreamReader?   stderr                   = null;
        try
        {
            SecurityAttributes pipeAttributes = new()
            {
                Length        = checked((uint)Marshal.SizeOf<SecurityAttributes>()),
                InheritHandle = true
            };
            CreateOwnedPipe(ref pipeAttributes, out stdinRead, out stdinWrite);
            CreateOwnedPipe(ref pipeAttributes, out stdoutRead, out stdoutWrite);
            CreateOwnedPipe(ref pipeAttributes, out stderrRead, out stderrWrite);
            MakeNonInheritable(stdinWrite);
            MakeNonInheritable(stdoutRead);
            MakeNonInheritable(stderrRead);

            nuint attributeBytes = 0;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeBytes);
            if (attributeBytes == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to size the process attribute list.");
            attributeList = Marshal.AllocHGlobal(checked((nint)attributeBytes));
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeBytes))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create the process attribute list.");
            attributeListInitialized = true;

            handleList = Marshal.AllocHGlobal(3 * IntPtr.Size);
            Marshal.WriteIntPtr(handleList, 0, stdinRead.DangerousGetHandle());
            Marshal.WriteIntPtr(handleList, IntPtr.Size, stdoutWrite.DangerousGetHandle());
            Marshal.WriteIntPtr(handleList, 2 * IntPtr.Size, stderrWrite.DangerousGetHandle());
            if (!UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                    handleList,
                    (nuint)(3 * IntPtr.Size),
                    IntPtr.Zero,
                    IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to restrict child process handles.");

            StartupInfoEx startup = new()
            {
                StartupInfo = new StartupInfo
                {
                    Size           = checked((uint)Marshal.SizeOf<StartupInfoEx>()),
                    Flags          = STARTF_USESTDHANDLES,
                    StandardInput  = stdinRead.DangerousGetHandle(),
                    StandardOutput = stdoutWrite.DangerousGetHandle(),
                    StandardError  = stderrWrite.DangerousGetHandle()
                },
                AttributeList = attributeList
            };
            StringBuilder commandLine = new($"\"{executable}\" \"{configurationPath}\"");
            if (!CreateProcessW(
                    executable,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    CREATE_SUSPENDED | CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT,
                    IntPtr.Zero,
                    Path.GetDirectoryName(executable),
                    ref startup,
                    out ProcessInformation created))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create suspended MediaMTX process.");

            processHandle = new SafeFileHandle(created.ProcessHandle, true);
            threadHandle  = new SafeFileHandle(created.ThreadHandle, true);
            if (!AssignProcessToJobObject(job, processHandle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to assign MediaMTX to its Job.");

            process = SystemProcess.GetProcessById(checked((int)created.ProcessId));
            stdoutWrite.Dispose();
            stdoutWrite = null;
            stderrWrite.Dispose();
            stderrWrite = null;
            stdinRead.Dispose();
            stdinRead = null;
            stdinWrite.Dispose();
            stdinWrite = null;
            stdout     = new StreamReader(new FileStream(stdoutRead, FileAccess.Read, PIPE_BUFFER_BYTES, false));
            stdoutRead = null;
            stderr     = new StreamReader(new FileStream(stderrRead, FileAccess.Read, PIPE_BUFFER_BYTES, false));
            stderrRead = null;

            if (ResumeThread(threadHandle) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to resume MediaMTX in its Job.");

            StartedProcess result = new(process, stdout, stderr, job);
            process = null;
            stdout  = null;
            stderr  = null;
            return result;
        }
        catch
        {
            if (processHandle is { IsInvalid: false })
            {
                TerminateProcess(processHandle, 1);
                WaitForSingleObject(processHandle, WAIT_TIMEOUT_MILLISECONDS);
            }

            process?.Dispose();
            stdout?.Dispose();
            stderr?.Dispose();
            job.Dispose();
            throw;
        }
        finally
        {
            threadHandle?.Dispose();
            processHandle?.Dispose();
            stdinRead?.Dispose();
            stdinWrite?.Dispose();
            stdoutRead?.Dispose();
            stdoutWrite?.Dispose();
            stderrRead?.Dispose();
            if (handleList != IntPtr.Zero)
                Marshal.FreeHGlobal(handleList);
            if (attributeListInitialized)
                DeleteProcThreadAttributeList(attributeList);
            if (attributeList != IntPtr.Zero)
                Marshal.FreeHGlobal(attributeList);
        }
    }

    private static JobHandle CreateOwnedJob()
    {
        JobHandle job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error, "Unable to create the MediaMTX Job.");
        }

        ExtendedLimitInformation limits = new()
        {
            BasicLimitInformation = new BasicLimitInformation
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };
        if (!SetInformationJobObject(
                job,
                JOB_OBJECT_EXTENDED_LIMIT_INFORMATION,
                ref limits,
                checked((uint)Marshal.SizeOf<ExtendedLimitInformation>())))
        {
            int error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error, "Unable to configure MediaMTX Job ownership.");
        }

        return job;
    }

    private static void CreateOwnedPipe(
        ref SecurityAttributes attributes,
        out SafeFileHandle     read,
        out SafeFileHandle     write)
    {
        if (!CreatePipe(out read, out write, ref attributes, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create a MediaMTX standard stream pipe.");
    }

    private static void MakeNonInheritable(SafeFileHandle handle)
    {
        if (!SetHandleInformation(handle, HANDLE_FLAG_INHERIT, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to restrict a MediaMTX pipe handle.");
    }

    internal sealed class StartedProcess : IDisposable
    {
        private readonly JobHandle job;

        public StartedProcess(
            SystemProcess process,
            StreamReader  standardOutput,
            StreamReader  standardError,
            JobHandle     job)
        {
            Process        = process;
            StandardOutput = standardOutput;
            StandardError  = standardError;
            this.job       = job;
        }

        public SystemProcess Process        { get; }
        public StreamReader  StandardOutput { get; }
        public StreamReader  StandardError  { get; }

        public void TerminateJob()
        {
            job.Dispose();
        }

        public void Dispose()
        {
            job.Dispose();
            StandardOutput.Dispose();
            StandardError.Dispose();
            Process.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public uint   Length;
        public IntPtr SecurityDescriptor;

        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint   Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public uint   X;
        public uint   Y;
        public uint   XSize;
        public uint   YSize;
        public uint   XCountChars;
        public uint   YCountChars;
        public uint   FillAttribute;
        public uint   Flags;
        public ushort ShowWindow;
        public ushort Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr      AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public uint   ProcessId;
        public uint   ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long  PerProcessUserTimeLimit;
        public long  PerJobUserTimeLimit;
        public uint  LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint  ActiveProcessLimit;
        public nuint Affinity;
        public uint  PriorityClass;
        public uint  SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters            IoInfo;
        public nuint                 ProcessMemoryLimit;
        public nuint                 JobMemoryLimit;
        public nuint                 PeakProcessMemoryUsed;
        public nuint                 PeakJobMemoryUsed;
    }

    internal sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public JobHandle()
            : base(true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern JobHandle CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        JobHandle                    job,
        int                          informationClass,
        ref ExtendedLimitInformation information,
        uint                         informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(JobHandle job, SafeFileHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out SafeFileHandle     read,
        out SafeFileHandle     write,
        ref SecurityAttributes attributes,
        uint                   size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr    attributeList,
        int       attributeCount,
        uint      flags,
        ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint   flags,
        IntPtr attribute,
        IntPtr value,
        nuint  size,
        IntPtr previousValue,
        IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string                               executable,
        StringBuilder                        commandLine,
        IntPtr                               processAttributes,
        IntPtr                               threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint                                 creationFlags,
        IntPtr                               environment,
        string?                              currentDirectory,
        ref StartupInfoEx                    startupInfo,
        out ProcessInformation               processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(SafeFileHandle thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeFileHandle process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
