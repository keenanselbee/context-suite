using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using ContextSuite.Private.Images;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Private.IO;

// Inherit only a read-only snapshot and the two diagnostic/output pipes. Create
// suspended, assign the existing kill-on-close job, then resume native parsing.
// This controls lifetime/handles; it is not a filesystem or network sandbox.
internal static class SeekableMediaProcess
{
    internal static Task<byte[]> RunAsync(string executable, string directory, FileStream input,
        IEnumerable<string> arguments, int maximumOutput, CancellationToken cancellationToken, Func<int, Exception>? exitFailure = null,
        Func<int, ReadOnlyMemory<byte>, Exception>? diagnosticFailure = null) =>
        RunAsync(executable, directory, input, arguments, (stream, token) => ReadAsync(stream, maximumOutput, token),
            cancellationToken, exitFailure, diagnosticFailure);

    internal static async Task<T> RunAsync<T>(string executable, string directory, FileStream input,
        IEnumerable<string> arguments, Func<Stream, CancellationToken, Task<T>> consumeOutput, CancellationToken cancellationToken,
        Func<int, Exception>? exitFailure = null, Func<int, ReadOnlyMemory<byte>, Exception>? diagnosticFailure = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!input.CanRead || input.CanWrite || !input.CanSeek || input.IsAsync)
            throw new ArgumentException("Native media input requires a synchronous, read-only, seekable file handle.");
        using var stdout = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var stderr = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var job = new EncoderJob();
        var current = GetCurrentProcess();
        if (!DuplicateHandle(current, input.SafeFileHandle, current, out var inheritedInput, 0, true, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        using var inputHandle = inheritedInput;
        var handles = new[] { inputHandle.DangerousGetHandle(), stdout.ClientSafePipeHandle.DangerousGetHandle(), stderr.ClientSafePipeHandle.DangerousGetHandle() };
        nuint attributeBytes = 0;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeBytes);
        if (attributeBytes == 0 || attributeBytes > 65536) throw new Win32Exception(Marshal.GetLastWin32Error());
        var attributes = Marshal.AllocHGlobal(checked((int)attributeBytes));
        var handleBuffer = Marshal.AllocHGlobal(handles.Length * IntPtr.Size);
        var environment = IntPtr.Zero;
        var initialized = false;
        ProcessInformation information = default;
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref attributeBytes)) throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;
            Marshal.Copy(handles, 0, handleBuffer, handles.Length);
            if (!UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x20002, handleBuffer, (nuint)(handles.Length * IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var entries = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
                .Where(pair => !string.Equals((string)pair.Key, "FFREPORT", StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => (string)pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key + "=" + pair.Value);
            environment = Marshal.StringToHGlobalUni(string.Join('\0', entries) + "\0\0");
            var startup = new StartupInfoEx { AttributeList = attributes,
                Startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100,
                    Input = handles[0], Output = handles[1], Error = handles[2] } };
            var command = new StringBuilder(string.Join(' ', new[] { executable }.Concat(arguments).Select(Quote)));
            if (command.Length > 32766) throw new InvalidDataException("Native media arguments exceed the Windows command-line limit.");
            // CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT |
            // EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW.
            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, true, 0x08080404,
                environment, directory, ref startup, out information)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(attributes); Marshal.FreeHGlobal(handleBuffer);
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
        }
        using var nativeProcess = new SafeFileHandle(information.Process, true);
        using var nativeThread = new SafeFileHandle(information.Thread, true);
        stdout.DisposeLocalCopyOfClientHandle(); stderr.DisposeLocalCopyOfClientHandle();
        // Keep the native handle alive even if opening the managed Process fails.
        try
        {
            using var process = Process.GetProcessById(checked((int)information.ProcessId));
            _ = process.Handle;
            void Stop() { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
            Task[] readers = [];
            try
            {
                job.Assign(process);
                using var cancelled = cancellationToken.Register(Stop);
                cancellationToken.ThrowIfCancellationRequested();
                var output = consumeOutput(stdout, cancellationToken);
                var errors = ReadAsync(stderr, 65536, cancellationToken);
                readers = [output, errors];
                foreach (var task in readers)
                    _ = task.ContinueWith(_ => Stop(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                if (ResumeThread(nativeThread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
                await Task.WhenAll(output, errors, process.WaitForExitAsync(cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw diagnosticFailure?.Invoke(process.ExitCode, await errors) ??
                    exitFailure?.Invoke(process.ExitCode) ??
                    new InvalidDataException("Media processing failed; no output was admitted.");
                return await output;
            }
            catch (Win32Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            finally
            {
                Stop(); await process.WaitForExitAsync();
                // A cancellation racing ResumeThread can bypass WhenAll above.
                // Drain both consumers before their pipes/reference are disposed.
                try { await Task.WhenAll(readers); }
                catch { /* The operation's original failure remains authoritative. */ }
            }
        }
        finally
        {
            // Also covers failures before the process was assigned to the job.
            TerminateProcess(nativeProcess, 1);
        }
    }

    private static async Task<byte[]> ReadAsync(Stream stream, int limit, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (bytes.Length + count > limit) throw new InvalidDataException("Decoded media or diagnostics exceed the current budget.");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }

    private static string Quote(string value)
    {
        // CreateProcess receives quoted arguments directly, without a shell.
        // CR/LF are literal metadata, while NUL would truncate an argument.
        if (value.Contains('\0')) throw new ArgumentException("Invalid native argument.");
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved, Desktop, Title;
        public uint X, Y, Width, Height, XCharacters, YCharacters, Fill, Flags;
        public ushort ShowWindow, ReservedBytes;
        public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx { public StartupInfo Startup; public IntPtr AttributeList; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, SafeFileHandle source, IntPtr targetProcess,
        out SafeFileHandle target, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, uint count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment,
        string directory, ref StartupInfoEx startup, out ProcessInformation information);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(SafeFileHandle process, uint exitCode);
}
