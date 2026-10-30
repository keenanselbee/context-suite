using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Private.IO;

internal static class MediaFiles
{
    private const uint ExcludedAttributes = 0x10 | 0x40 | 0x400 | 0x1000 | 0x40000 | 0x400000;

    internal static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Length > 32700 ||
            path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new InvalidDataException("Use an ordinary fully qualified media path.");
        var full = Path.GetFullPath(path);
        var relative = full[Path.GetPathRoot(full)!.Length..];
        if (relative.IndexOfAny([':', '\0', '\r', '\n']) >= 0 || relative.Split(Path.DirectorySeparatorChar).Any(part => part.EndsWith(' ') || part.EndsWith('.')))
            throw new InvalidDataException("Alternate streams and ambiguous media paths are not supported.");
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked media paths are not supported.");
        if (((uint)File.GetAttributes(full) & ExcludedAttributes) != 0)
            throw new InvalidDataException("An available regular media file is required.");
        return full;
    }

    internal static FileStream OpenRead(string path)
    {
        var full = ValidatePath(path);
        // Managed file APIs already support these lengths. The explicit Win32
        // open needs extended syntax after ordinary-path/link validation.
        var native = full.Length < 260 ? full : full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
        var handle = CreateFileW(native, 0x80000000, 1, IntPtr.Zero, 3, 0x08000000 | 0x00200000 | 0x00100000, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        FileStream stream;
        try { stream = new FileStream(handle, FileAccess.Read); }
        catch { handle.Dispose(); throw; }
        try { CheckHandle(stream, full); return stream; }
        catch { stream.Dispose(); throw; }
    }

    internal static void CheckHandle(FileStream stream, string path)
    {
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var final = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(stream.SafeFileHandle, final, (uint)final.Capacity, 0);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (length >= final.Capacity) throw new InvalidDataException("Media path exceeds the supported length.");
        var actual = final.ToString();
        if (actual.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) actual = @"\\" + actual[8..];
        else if (actual.StartsWith(@"\\?\", StringComparison.Ordinal)) actual = actual[4..];
        if (info.NumberOfLinks != 1 || (info.Attributes & ExcludedAttributes) != 0 ||
            !string.Equals(actual, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Media handle is linked, unavailable or redirected.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
