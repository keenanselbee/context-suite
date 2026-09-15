using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Application.Infrastructure;

internal sealed record WorkerLifetimeIdentity(string Name, int OwnerProcessId, long OwnerStartUtcTicks, int SessionId)
{
    private const string Prefix = @"Local\ContextSuite-WorkerLifetime-";

    internal static WorkerLifetimeIdentity Create(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A fresh worker lifetime identity is required.", nameof(id));
        using var process = Process.GetCurrentProcess();
        return new(Prefix + id.ToString("N"), process.Id, process.StartTime.ToUniversalTime().Ticks, process.SessionId);
    }

    internal void Validate()
    {
        if (Name is null || !Name.StartsWith(Prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(Name[Prefix.Length..], "N", out var id) || id == Guid.Empty || Name != Prefix + id.ToString("N") ||
            OwnerProcessId <= 0 || OwnerStartUtcTicks <= 0 || OwnerStartUtcTicks > DateTime.MaxValue.Ticks || SessionId < 0)
            throw new InvalidDataException("Invalid worker lifetime identity. Retain it for review.");
    }

    internal void RequireExitedOwner()
    {
        Validate();
        using var current = Process.GetCurrentProcess();
        if (current.SessionId != SessionId)
            throw new InvalidDataException("Worker lifetime recovery requires the original Windows session.");
        Process owner;
        try { owner = Process.GetProcessById(OwnerProcessId); }
        catch (ArgumentException) { return; } // The original PID no longer exists.
        using (owner)
        {
            // Query failures remain failures; they do not establish owner death.
            if (!owner.HasExited && owner.StartTime.ToUniversalTime().Ticks == OwnerStartUtcTicks)
                throw new IOException("The worker lifetime owner is still running.");
        }
    }
}

// The application retains this job even when its worker crashes. Engine jobs
// remain nested inside it with their own resource and security restrictions.
internal sealed class WorkerProcessJob : IDisposable
{
    private readonly SafeFileHandle _handle;
    internal WorkerLifetimeIdentity? RecoveryIdentity { get; }

    internal WorkerProcessJob() : this(null) { }

    internal static WorkerProcessJob CreateRecoverable(Guid id) => new(WorkerLifetimeIdentity.Create(id));

    private WorkerProcessJob(WorkerLifetimeIdentity? identity)
    {
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("The media worker requires Windows x64.");
        RecoveryIdentity = identity;
        int error;
        if (identity is null)
        {
            _handle = CreateJobObject(IntPtr.Zero, null);
            error = Marshal.GetLastWin32Error();
        }
        else
        {
            identity.Validate();
            using var user = WindowsIdentity.GetCurrent();
            var sid = user.User?.Value ?? throw new IOException("The worker lifetime owner has no Windows user SID.");
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor($"D:P(A;;GA;;;SY)(A;;GA;;;{sid})", 1, out var descriptor, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
                _handle = CreateNamedJobObject(ref attributes, identity.Name);
                error = Marshal.GetLastWin32Error();
            }
            finally { LocalFree(descriptor); }
        }
        if (_handle.IsInvalid || identity is not null && error == 183)
        {
            _handle.Dispose(); // Never reconfigure or adopt a colliding job.
            throw new Win32Exception(error);
        }
        var limits = new Limits { Flags = 0x2000 }; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, no breakaway.
        if (!SetInformationJobObject(_handle, 9, ref limits, (uint)Marshal.SizeOf<Limits>()))
        {
            error = Marshal.GetLastWin32Error(); _handle.Dispose(); throw new Win32Exception(error);
        }
        if (!ReadLimits(_handle, 9, out var applied, (uint)Marshal.SizeOf<Limits>(), IntPtr.Zero) || applied.Flags != limits.Flags)
        {
            _handle.Dispose(); throw new IOException("The media worker lifetime job was not configured.");
        }
    }

    // The caller must first validate the durable record that supplied this identity.
    // A missing named object is meaningful only after confirmed creation/assignment.
    internal static async Task<bool> StopRecordedAsync(WorkerLifetimeIdentity identity)
    {
        identity.RequireExitedOwner();
        using var handle = OpenJobObject(0x0004 | 0x0008, false, identity.Name); // Query and terminate only.
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 2) return false;
            throw new Win32Exception(error);
        }
        if (!ReadLimits(handle, 9, out var limits, (uint)Marshal.SizeOf<Limits>(), IntPtr.Zero) || limits.Flags != 0x2000)
            throw new IOException("The recorded worker lifetime job has unexpected limits. Retain it for review.");
        await StopAsync(handle).ConfigureAwait(false);
        return true;
    }

    internal void Assign(Process process)
    {
        if (!AssignProcessToJobObject(_handle, process.SafeHandle)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal Task StopAsync() => StopAsync(_handle);

    private static async Task StopAsync(SafeFileHandle handle)
    {
        if (!TerminateJobObject(handle, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (!ReadAccounting(handle, 1, out var accounting, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero))
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
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateNamedJobObject(ref SecurityAttributes attributes, string name);
    [DllImport("kernel32.dll", EntryPoint = "OpenJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle OpenJobObject(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
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
