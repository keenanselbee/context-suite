using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Evaluation lifetime ownership only. This does not restrict filesystem/network
// access and must only execute the harness's authored passive fixtures.
internal static class OfficeEvaluationProcess
{
    internal sealed record Result(string Output, string Error, int ProcessId, uint TotalProcesses, uint ActiveProcessesAfterCleanup, long Milliseconds);

    internal static async Task<Result> RunAsync(string executable, IEnumerable<string> arguments, string directory,
        IReadOnlyDictionary<string, string>? overrides = null, CancellationToken token = default,
        TimeSpan? timeout = null)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        using var job = CreateJobObject(IntPtr.Zero, null);
        Require(!job.IsInvalid);
        var limits = new ExtendedLimits
        {
            Basic = new BasicLimits { Flags = 0x2000 | 0x100 | 0x200 | 8, ActiveProcesses = 8 },
            ProcessMemory = 512 * 1024 * 1024, JobMemory = 1024 * 1024 * 1024
        };
        Require(SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()));
        using var stdout = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var stderr = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var stdin = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var handles = new[] { stdin.ClientSafePipeHandle.DangerousGetHandle(), stdout.ClientSafePipeHandle.DangerousGetHandle(), stderr.ClientSafePipeHandle.DangerousGetHandle() };
        var timer = Stopwatch.StartNew();
        var information = Start(executable, arguments, directory, overrides, job, handles);
        using var child = new SafeFileHandle(information.Process, true);
        using var thread = new SafeFileHandle(information.Thread, true);
        stdout.DisposeLocalCopyOfClientHandle(); stderr.DisposeLocalCopyOfClientHandle();
        stdin.DisposeLocalCopyOfClientHandle(); stdin.Dispose(); // Empty input, never the caller's console.
        Task<string>[] readers = [];
        void Stop() => TerminateJobObject(job, 71);
        try
        {
            using var process = Process.GetProcessById(checked((int)information.ProcessId));
            _ = process.Handle;
            using var cancelled = deadline.Token.Register(Stop);
            var output = Read(stdout, deadline.Token); var errors = Read(stderr, deadline.Token);
            readers = [output, errors];
            // A failed reader must stop writers immediately; WhenAll alone can
            // wait for the whole deadline while a descendant retains a pipe.
            foreach (var reader in readers)
                _ = reader.ContinueWith(_ => Stop(), CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            deadline.Token.ThrowIfCancellationRequested();
            if (ResumeThread(thread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
            var exited = process.WaitForExitAsync(deadline.Token);
            await exited;
            // The root can exit while a descendant is alive and holds stdout.
            Stop();
            await Task.WhenAll(readers);
            deadline.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new IOException($"Evaluation child exited {process.ExitCode}: {await errors}");
            var accounting = await Empty(job);
            return new(await output, await errors, process.Id, accounting.TotalProcesses, accounting.ActiveProcesses, timer.ElapsedMilliseconds);
        }
        finally
        {
            Stop();
            await Empty(job);
            try { await Task.WhenAll(readers); }
            catch { /* Preserve the operation's failure after owned writers exit. */ }
        }
    }

    private static async Task<string> Read(Stream stream, CancellationToken token)
    {
        using var bytes = new MemoryStream(); var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(), token);
            if (count == 0) break;
            if (bytes.Length + count > 65536) throw new InvalidDataException("Evaluation diagnostics exceed the byte budget.");
            bytes.Write(buffer, 0, count);
        }
        bytes.Position = 0;
        using var reader = new StreamReader(bytes, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static async Task<Accounting> Empty(SafeFileHandle job)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            Require(QueryInformationJobObject(job, 1, out var result, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero));
            if (result.ActiveProcesses == 0) return result;
            await Task.Delay(20);
        } while (timer.Elapsed < TimeSpan.FromSeconds(5));
        throw new IOException("Evaluation job retained processes after cleanup.");
    }

    private static ProcessInformation Start(string executable, IEnumerable<string> arguments, string directory,
        IReadOnlyDictionary<string, string>? overrides, SafeFileHandle job, IntPtr[] handles)
    {
        var command = new StringBuilder(string.Join(' ', new[] { executable }.Concat(arguments).Select(Quote)));
        if (command.Length > 32766) throw new InvalidDataException("Evaluation command exceeds the Windows limit.");
        var environment = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) environment[(string)entry.Key] = (string)entry.Value!;
        if (overrides is not null)
            foreach (var (name, value) in overrides)
            {
                if (name.Length == 0 || name.Contains('=') || name.Contains('\0') || value.Contains('\0')) throw new ArgumentException("Invalid child environment.");
                environment[name] = value;
            }
        nuint bytes = 0;
        InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref bytes);
        if (bytes == 0 || bytes > 65536) throw new Win32Exception(Marshal.GetLastWin32Error());
        var attributes = Marshal.AllocHGlobal(checked((int)bytes));
        var inherited = Marshal.AllocHGlobal(handles.Length * IntPtr.Size);
        var jobPointer = Marshal.AllocHGlobal(IntPtr.Size);
        var environmentPointer = Marshal.StringToHGlobalUni(string.Join('\0', environment.Select(pair => pair.Key + "=" + pair.Value)) + "\0\0");
        var initialized = false;
        try
        {
            Require(InitializeProcThreadAttributeList(attributes, 2, 0, ref bytes)); initialized = true;
            Marshal.Copy(handles, 0, inherited, handles.Length);
            Marshal.WriteIntPtr(jobPointer, job.DangerousGetHandle());
            Require(UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x20002, inherited, (nuint)(handles.Length * IntPtr.Size), IntPtr.Zero, IntPtr.Zero));
            // JOB_LIST assigns ownership inside process creation, avoiding a
            // running or suspended child left between CreateProcess and Assign.
            Require(UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x2000d, jobPointer, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero));
            var startup = new StartupEx { Attributes = attributes, Startup = new Startup
            {
                Size = Marshal.SizeOf<StartupEx>(), Flags = 0x100, Input = handles[0], Output = handles[1], Error = handles[2]
            } };
            Require(CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, true, 0x08080404,
                environmentPointer, directory, ref startup, out var information));
            return information;
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(attributes); Marshal.FreeHGlobal(inherited);
            Marshal.FreeHGlobal(jobPointer); Marshal.FreeHGlobal(environmentPointer);
        }
    }

    private static string Quote(string value)
    {
        if (value.Contains('\0')) throw new ArgumentException("NUL is not a command-line character.");
        var result = new StringBuilder("\""); var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? 2 * slashes + 1 : slashes).Append(c); slashes = 0;
        }
        return result.Append('\\', 2 * slashes).Append('"').ToString();
    }

    private static void Require(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses;
        public nuint Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Accounting
    {
        public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Startup
    {
        public int Size;
        public IntPtr Reserved, Desktop, Title;
        public uint X, Y, Width, Height, XCharacters, YCharacters, Fill, Flags;
        public ushort ShowWindow, ReservedBytes;
        public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupEx { public Startup Startup; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits limits, uint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, out Accounting accounting, uint bytes, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, nuint bytes, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string executable, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string directory, ref StartupEx startup, out ProcessInformation information);
}
