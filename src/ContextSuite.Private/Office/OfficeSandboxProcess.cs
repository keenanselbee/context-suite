using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using ContextSuite.Core.Office;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Private.Office;

// The application owns the Windows profile independently of this worker-owned
// job. The caller retains verified payload/path leases and explicit grants.
// No profile creation, ACL mutation, media publication or ambient environment.
internal static class OfficeSandboxProcess
{
    private static readonly HashSet<string> EnvironmentNames = new(StringComparer.Ordinal)
    {
        "APPDATA", "LOCALAPPDATA", "PATH", "SYSTEMROOT", "TEMP", "TMP", "USERPROFILE", "WINDIR",
        "CS_OFFICE_PROFILE_NAME", "CS_OFFICE_REQUEST_ID", "CS_OFFICE_FORMAT", "CS_OFFICE_CALCULATION",
        "CS_OFFICE_SOURCE", "CS_OFFICE_OUTPUT", "CS_OFFICE_PROFILE", "CS_OFFICE_PROGRAM",
        "CS_OFFICE_SOURCE_IDENTITY", "CS_OFFICE_OUTPUT_IDENTITY", "CS_OFFICE_PROFILE_IDENTITY", "CS_OFFICE_PROGRAM_IDENTITY"
    };

    internal static async Task<byte[]> RunAsync(string executable, string directory, IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken, TimeSpan? deadline = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timeout = deadline ?? TimeSpan.FromMinutes(2);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(deadline));
        if (!EnvironmentNames.SetEquals(environment.Keys) || environment.Any(pair => pair.Value.Length == 0 || pair.Value.Contains('\0')))
            throw new InvalidDataException("Office requires a complete explicit launch environment.");
        var profile = environment["CS_OFFICE_PROFILE_NAME"];
        if (!profile.StartsWith("ContextSuite.Office.", StringComparison.Ordinal) || profile.Length > 128 ||
            profile.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '.'))
            throw new InvalidDataException("Office requires an owned profile identity.");
        var environmentText = string.Join('\0', environment.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value)) + "\0\0";
        if (environmentText.Length > 32767) throw new InvalidDataException("Office launch environment exceeds its bound.");
        if (!Path.IsPathFullyQualified(executable) || executable.IndexOfAny(['\0', '"', '\r', '\n']) >= 0 || executable.Length > 32700)
            throw new InvalidDataException("Office requires a verified absolute host path.");

        var result = DeriveAppContainerSidFromAppContainerName(profile, out var sid);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
        try { return await RunContainedAsync(executable, directory, environmentText, sid, timeout, cancellationToken); }
        finally { FreeSid(sid); }
    }

    private static async Task<byte[]> RunContainedAsync(string executable, string directory, string environmentText, IntPtr sid,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var stdout = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var stderr = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var security = new SecurityAttributes { Size = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
        using var input = CreateFile("NUL", 0x80000000, 3, ref security, 3, 0, IntPtr.Zero);
        if (input.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var job = new OfficeJob();
        var handles = new[] { input.DangerousGetHandle(), stdout.ClientSafePipeHandle.DangerousGetHandle(), stderr.ClientSafePipeHandle.DangerousGetHandle() };
        nuint attributeBytes = 0;
        InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref attributeBytes);
        if (attributeBytes == 0 || attributeBytes > 65536) throw new Win32Exception(Marshal.GetLastWin32Error());
        var attributes = Marshal.AllocHGlobal(checked((int)attributeBytes));
        var handleBuffer = Marshal.AllocHGlobal(handles.Length * IntPtr.Size);
        var capabilitiesBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<SecurityCapabilities>());
        var environment = Marshal.StringToHGlobalUni(environmentText);
        var initialized = false;
        ProcessInformation information;
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 2, 0, ref attributeBytes)) throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;
            Marshal.Copy(handles, 0, handleBuffer, handles.Length);
            Marshal.StructureToPtr(new SecurityCapabilities { Sid = sid }, capabilitiesBuffer, false);
            if (!UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x20002, handleBuffer, (nuint)(handles.Length * IntPtr.Size), IntPtr.Zero, IntPtr.Zero) ||
                !UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x20009, capabilitiesBuffer, (nuint)Marshal.SizeOf<SecurityCapabilities>(), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var startup = new StartupInfoEx { AttributeList = attributes,
                Startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100,
                    Input = handles[0], Output = handles[1], Error = handles[2] } };
            // Suspended, hidden, Unicode environment; only the three listed handles.
            if (!CreateProcess(executable, new StringBuilder('"' + executable + '"'), IntPtr.Zero, IntPtr.Zero, true, 0x08080404,
                environment, directory, ref startup, out information)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(attributes); Marshal.FreeHGlobal(handleBuffer); Marshal.FreeHGlobal(capabilitiesBuffer); Marshal.FreeHGlobal(environment);
        }
        using var nativeProcess = new SafeFileHandle(information.Process, true);
        using var nativeThread = new SafeFileHandle(information.Thread, true);
        stdout.DisposeLocalCopyOfClientHandle(); stderr.DisposeLocalCopyOfClientHandle();
        using var timer = new CancellationTokenSource(timeout);
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);
        Task[] readers = [];
        try
        {
            job.Assign(nativeProcess);
            VerifyToken(nativeProcess, sid);
            using var process = Process.GetProcessById(checked((int)information.ProcessId));
            _ = process.Handle;
            using var registration = stopped.Token.Register(() =>
            {
                // Closing the job is the final fallback; the retained root handle
                // also covers a stop racing job assignment/process startup.
                try { job.Stop(); } catch (Win32Exception) { }
                TerminateProcess(nativeProcess, 1);
            });
            async Task<byte[]> ReadChannelAsync(Stream stream, int limit)
            {
                try { return await ReadAsync(stream, limit, stopped.Token); }
                catch { stopped.Cancel(); throw; }
            }
            var output = ReadChannelAsync(stdout, OfficeHostProtocol.MaximumReplyBytes);
            var diagnostics = ReadChannelAsync(stderr, 65536);
            readers = [output, diagnostics];
            try
            {
                stopped.Token.ThrowIfCancellationRequested();
                if (ResumeThread(nativeThread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
                await Task.WhenAll(output, diagnostics, process.WaitForExitAsync(stopped.Token));
            }
            catch (OperationCanceledException) when (timer.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            { throw new TimeoutException("Office export exceeded its time limit."); }
            cancellationToken.ThrowIfCancellationRequested();
            if (timer.IsCancellationRequested) throw new TimeoutException("Office export exceeded its time limit.");
            if (process.ExitCode != 0) throw new InvalidDataException("Office export did not complete successfully.",
                new IOException($"Office host exit {process.ExitCode}: {Encoding.UTF8.GetString(await diagnostics)}"));
            return await output;
        }
        finally
        {
            try { job.Stop(); }
            finally
            {
                TerminateProcess(nativeProcess, 1);
                await job.WaitForCleanupAsync();
                if (WaitForSingleObject(nativeProcess, 5000) != 0) throw new IOException("Office host did not stop within the cleanup deadline.");
                // Descendants retaining stdout/stderr are stopped before consumers drain.
                try { await Task.WhenAll(readers); } catch { }
            }
        }
    }

    private static async Task<byte[]> ReadAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Office output or diagnostics exceeded its bound.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static void VerifyToken(SafeFileHandle process, IntPtr sid)
    {
        if (!OpenProcessToken(process, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            var isolated = ReadToken(token, 29);
            if (isolated.Length != 4 || BitConverter.ToUInt32(isolated) != 1) throw new InvalidDataException("Office child is not isolated.");
            var capabilities = ReadToken(token, 30);
            if (capabilities.Length < 4 || BitConverter.ToUInt32(capabilities) != 0) throw new InvalidDataException("Office child has unexpected capabilities.");
            uint bytes = 0;
            // TOKEN_APPCONTAINER_INFORMATION points into this native buffer.
            GetTokenInformation(token, 31, IntPtr.Zero, 0, out bytes);
            if (bytes < IntPtr.Size || bytes > 65536) throw new InvalidDataException("Invalid Office sandbox token size.");
            var buffer = Marshal.AllocHGlobal(checked((int)bytes));
            try
            {
                if (!GetTokenInformation(token, 31, buffer, bytes, out _) || !EqualSid(Marshal.ReadIntPtr(buffer), sid))
                    throw new InvalidDataException("Office child sandbox identity differs from its owner.");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    private static byte[] ReadToken(SafeFileHandle token, int kind)
    {
        GetTokenInformation(token, kind, IntPtr.Zero, 0, out var bytes);
        if (bytes == 0 || bytes > 65536) throw new InvalidDataException("Invalid Office sandbox token size.");
        var buffer = Marshal.AllocHGlobal(checked((int)bytes));
        try
        {
            if (!GetTokenInformation(token, kind, buffer, bytes, out var written) || written > bytes)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[written]; Marshal.Copy(buffer, result, 0, result.Length); return result;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Size; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities { public IntPtr Sid, Capabilities; public uint Count, Reserved; }
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
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);
    [DllImport("advapi32.dll")] private static extern IntPtr FreeSid(IntPtr sid);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, ref SecurityAttributes attributes, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, uint count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation information);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeFileHandle process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeFileHandle process, uint access, out SafeFileHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeFileHandle token, int kind, IntPtr buffer, uint bytes, out uint returned);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EqualSid(IntPtr first, IntPtr second);
}
