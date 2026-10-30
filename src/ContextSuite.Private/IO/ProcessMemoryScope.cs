using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Private.IO;

// Native codecs do not all expose allocator callbacks. Bound the sequential
// worker's committed memory during a request, including native allocations. One
// nested job is reused, avoiding a new permanent job nesting level per page.
internal sealed class ProcessMemoryScope : IDisposable
{
    private static readonly object Gate = new();
    private static SafeFileHandle? _job;
    private static bool _active;
    private bool _disposed;

    internal ProcessMemoryScope(ulong maximumBytes)
    {
        if (maximumBytes is 0 or > 4UL * 1024 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        lock (Gate)
        {
            if (_active) throw new InvalidOperationException("Native media work must remain sequential.");
            if (_job is null)
            {
                var job = CreateJobObject(IntPtr.Zero, null);
                if (job.IsInvalid) { var error = Marshal.GetLastWin32Error(); job.Dispose(); throw new Win32Exception(error); }
                if (!AssignProcessToJobObject(job, new IntPtr(-1)))
                { var error = Marshal.GetLastWin32Error(); job.Dispose(); throw new Win32Exception(error); }
                _job = job;
            }
            var limits = new ExtendedLimits { Basic = new() { Flags = 0x100 }, ProcessMemoryLimit = (UIntPtr)maximumBytes };
            if (!SetInformationJobObject(_job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()) ||
                !QueryInformationJobObject(_job, 9, out var applied, (uint)Marshal.SizeOf<ExtendedLimits>(), IntPtr.Zero) ||
                applied.Basic.Flags != 0x100 || applied.ProcessMemoryLimit.ToUInt64() != maximumBytes)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not establish the native media memory budget.");
            _active = true;
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            var limits = new ExtendedLimits();
            if (!SetInformationJobObject(_job!, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not restore the media worker memory policy.");
            _active = false; _disposed = true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, out ExtendedLimits limits, uint length, IntPtr returned);
}
