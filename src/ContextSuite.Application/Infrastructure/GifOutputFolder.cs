using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Application.Infrastructure;

internal sealed record GifFolderIdentity(string Path, uint Volume, ulong Id);

// Atomic directory creation avoids merging unrelated frame sets. A held handle
// prevents directory replacement while frames are being published.
internal sealed class GifOutputFolder : IDisposable
{
    private readonly SafeFileHandle _handle;
    public GifFolderIdentity Identity { get; }
    private GifOutputFolder(string path)
    {
        PublicationFiles.Normalize(path);
        _handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (_handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!GetFileInformationByHandle(_handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if ((info.Attributes & (uint)FileAttributes.Directory) == 0 || (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The GIF output folder changed or is linked.");
            Identity = new(path, info.VolumeSerial, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
        }
        catch { _handle.Dispose(); throw; }
    }

    public static GifOutputFolder Open(GifFolderIdentity expected)
    {
        var folder = new GifOutputFolder(expected.Path);
        if (folder.Identity != expected)
        {
            folder.Dispose();
            throw new InvalidDataException("The GIF output folder changed. Start a new Convert command; completed frames were kept.");
        }
        return folder;
    }

    public static GifOutputFolder Create(string parent, string sourcePath)
    {
        parent = PublicationFiles.Normalize(parent);
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Choose an existing output folder.");
        var basename = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        for (var ordinal = 1; ordinal <= 10000; ordinal++)
        {
            var name = basename + " - Frames" + (ordinal == 1 ? "" : $" ({ordinal})");
            if (name.Length > 255) throw new InvalidDataException("Rename the GIF to a shorter filename before extracting frames.");
            var path = PublicationFiles.Normalize(System.IO.Path.Combine(parent, name));
            if (CreateDirectory(path, IntPtr.Zero)) return new(path);
            var error = Marshal.GetLastWin32Error();
            if (error is not (80 or 183)) throw new Win32Exception(error);
        }
        throw new IOException("Too many GIF folder name collisions. Choose another output folder.");
    }

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectory(string path, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
}
