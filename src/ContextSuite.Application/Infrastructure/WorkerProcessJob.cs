using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Application.Infrastructure;

// The application retains this job even when its worker crashes. Engine jobs
// remain nested inside it with their own resource and security restrictions.
internal sealed class WorkerProcessJob : IDisposable
{
    private readonly SafeFileHandle _handle;

    internal WorkerProcessJob()
    {
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("The media worker requires Windows x64.");
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error(); _handle.Dispose(); throw new Win32Exception(error);
        }
        var limits = new Limits { Flags = 0x2000 }; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, no breakaway.
        if (!SetInformationJobObject(_handle, 9, ref limits, (uint)Marshal.SizeOf<Limits>()))
        {
            var error = Marshal.GetLastWin32Error(); _handle.Dispose(); throw new Win32Exception(error);
        }
        if (!ReadLimits(_handle, 9, out var applied, (uint)Marshal.SizeOf<Limits>(), IntPtr.Zero) || applied.Flags != limits.Flags)
        {
            _handle.Dispose(); throw new IOException("The media worker lifetime job was not configured.");
        }
    }

    internal void Assign(Process process)
    {
        if (!AssignProcessToJobObject(_handle, process.SafeHandle)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal async Task StopAsync()
    {
        if (!TerminateJobObject(_handle, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (!ReadAccounting(_handle, 1, out var accounting, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (accounting.ActiveProcesses == 0) return;
            if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(5))
                throw new IOException("The media worker and its child processes did not finish stopping.");
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    public void Dispose() { _handle.Dispose(); }

    // Windows x64 JOBOBJECT_EXTENDED_LIMIT_INFORMATION. Unspecified limits stay
    // zero; the application sets only the lifetime flag, not engine budgets.
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct Limits
    {
        [FieldOffset(16)] public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting
    {
        public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref Limits information, uint size);
    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadLimits(SafeFileHandle job, int kind, out Limits information, uint size, IntPtr returned);
    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadAccounting(SafeFileHandle job, int kind, out Accounting information, uint size, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
}
