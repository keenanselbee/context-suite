using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Runtime;

internal static class LocalPipe
{
    public static string SessionName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return $"ContextSuite-{identity.User!.Value}-{Process.GetCurrentProcess().SessionId}";
        }
    }

    public static NamedPipeServerStream CreateServer(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        // Deny network logons, allow only this user, and reject remote clients at the pipe itself.
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
            $"D:P(D;;GA;;;NU)(A;;GA;;;{identity.User!.Value})", 1, out var descriptor, out _))
            throw new Win32Exception();
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor
            };
            var handle = CreateNamedPipe($@"\\.\pipe\{name}", 0x40080003, 8, 1, 65536, 65536, 0, ref attributes);
            if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(); }
            return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
        }
        finally { LocalFree(descriptor); }
    }

    public static void VerifyPeer(PipeStream pipe, bool isServer, int? expectedPid, string expectedExecutable)
    {
        uint pid;
        var found = isServer ? GetNamedPipeClientProcessId(pipe.SafePipeHandle, out pid)
            : GetNamedPipeServerProcessId(pipe.SafePipeHandle, out pid);
        if (!found || pid == 0 || (expectedPid.HasValue && pid != expectedPid.Value))
            throw new IOException("The local connection peer could not be verified.");
        using var peer = Process.GetProcessById(checked((int)pid));
        if (peer.SessionId != Process.GetCurrentProcess().SessionId ||
            !string.Equals(peer.MainModule?.FileName, expectedExecutable, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The local connection belongs to another session or application.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW")]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateNamedPipeW")]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint instances, uint output, uint input, uint timeout, ref SecurityAttributes attributes);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
}
