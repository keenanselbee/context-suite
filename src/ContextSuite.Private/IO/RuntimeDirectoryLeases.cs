using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Private.IO;

internal class RuntimeDirectoryLeases : IDisposable
{
    private readonly Dictionary<string, SafeFileHandle> _handles = new(StringComparer.OrdinalIgnoreCase);

    internal FileStream OpenFileRead(string path)
    {
        path = LocalPath(path);
        Open(Path.GetDirectoryName(path)!);
        // All ancestors already have verified, retained directory handles.
        // Still open the leaf without following a reparse point and verify its
        // actual resolved path, attributes and link count on every file open.
        var handle = CreateFile(path, 0x80000000, 1, IntPtr.Zero, 3, 0x08300000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error(); handle.Dispose();
            if (error == 2) throw new FileNotFoundException("Runtime file is missing.", path);
            throw new Win32Exception(error);
        }
        FileStream stream;
        try { stream = new FileStream(handle, FileAccess.Read); }
        catch { handle.Dispose(); throw; }
        try { MediaFiles.CheckHandle(stream, path); return stream; }
        catch { stream.Dispose(); throw; }
    }

    internal static string LocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length is < 4 or > 240 || !char.IsAsciiLetter(path[0]) ||
            path[1] != ':' || path[2] != '\\' || !string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase) ||
            path[3..].IndexOfAny([':', '\0', '\r', '\n', '/', '"']) >= 0 ||
            path[3..].Split('\\').Any(part => part.Length == 0 || part.EndsWith(' ') || part.EndsWith('.')))
            throw new InvalidDataException("Use an ordinary local runtime path within the candidate path limit.");
        return path;
    }

    internal SafeFileHandle Open(string path)
    {
        var pending = new Stack<string>();
        for (var current = path; current is not null && !_handles.ContainsKey(current); current = Path.GetDirectoryName(current))
            pending.Push(current);
        while (pending.TryPop(out var current))
        {
            // Directory-list access participates in delete-sharing checks;
            // metadata-only handles do not prevent renaming.
            var native = current.Length < 260 ? current : @"\\?\" + current;
            var handle = CreateFile(native, 0x81, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
            _handles.Add(current, handle);
            if (!GetFileInformationByHandleEx(handle, 9, out AttributeTag attributes, (uint)Marshal.SizeOf<AttributeTag>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if ((attributes.Attributes & 0x400) != 0 || (attributes.Attributes & 0x10) == 0)
                throw new InvalidDataException("Runtime files require ordinary directories.");
            var final = new StringBuilder(32768);
            var count = GetFinalPathNameByHandle(handle, final, (uint)final.Capacity, 0);
            if (count <= 4 || count >= final.Capacity || !final.ToString().StartsWith(@"\\?\", StringComparison.Ordinal) ||
                !string.Equals(final.ToString()[4..], current, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Runtime directory identity changed.");
        }
        return _handles[path];
    }

    internal static string Identity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 18, out FileIdentity identity, (uint)Marshal.SizeOf<FileIdentity>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return identity.Volume.ToString("X16", System.Globalization.CultureInfo.InvariantCulture) + Convert.ToHexString(identity.Identifier);
    }

    public void Dispose()
    {
        foreach (var handle in _handles.Values.Reverse()) handle.Dispose();
        _handles.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTag { internal uint Attributes, Tag; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdentity
    {
        internal ulong Volume;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] internal byte[] Identifier;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int information, out AttributeTag attributes, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int information, out FileIdentity identity, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
}
