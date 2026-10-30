using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Private.Office;

internal sealed class OfficeJob : IDisposable
{
    private readonly SafeFileHandle _handle;

    internal OfficeJob()
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new ExtendedLimits
        {
            Basic = new BasicLimits { Flags = 0x2000 | 0x8 | 0x100 | 0x200, ActiveProcessLimit = 8 },
            // XLSX copy validation can exceed 512 MiB after PDF rendering.
            // Let the main host use the existing aggregate allowance; all
            // descendants still share the same bounded 1 GiB job.
            ProcessMemory = 1024 * 1024 * 1024, JobMemory = 1024 * 1024 * 1024
        };
        if (!SetInformationJobObject(_handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            var error = Marshal.GetLastWin32Error(); _handle.Dispose(); throw new Win32Exception(error);
        }
        if (!QueryExtendedInformation(_handle, 9, out var applied, (uint)Marshal.SizeOf<ExtendedLimits>(), IntPtr.Zero) ||
            applied.Basic.Flags != limits.Basic.Flags || applied.Basic.ActiveProcessLimit != 8 ||
            applied.ProcessMemory != limits.ProcessMemory || applied.JobMemory != limits.JobMemory)
        {
            _handle.Dispose(); throw new IOException("Office job resource limits were not applied.");
        }
    }

    internal void Assign(SafeFileHandle process)
    {
        if (!AssignProcessToJobObject(_handle, process)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal void Stop()
    {
        if (!TerminateJobObject(_handle, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal async Task WaitForCleanupAsync()
    {
        var started = Stopwatch.GetTimestamp();
        for (;;)
        {
            if (!QueryInformationJobObject(_handle, 1, out var accounting, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (accounting.ActiveProcesses == 0) return;
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(5))
                throw new IOException("Office processes did not stop within the cleanup deadline.");
            await Task.Delay(10);
        }
    }

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
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
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimits information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int informationClass, out Accounting information, uint length, IntPtr returned);
    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryExtendedInformation(SafeFileHandle job, int informationClass, out ExtendedLimits information, uint length, IntPtr returned);
}
