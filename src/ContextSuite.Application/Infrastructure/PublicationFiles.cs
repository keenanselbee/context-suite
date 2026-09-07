using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Application.Infrastructure;

internal sealed record FileFingerprint(long Length, string Sha256, uint Volume, ulong Id);

internal static class PublicationFiles
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Length > 32700 ||
            path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new InvalidDataException("Use an ordinary fully qualified file path.");
        var full = Path.GetFullPath(path);
        var relative = full[Path.GetPathRoot(full)!.Length..];
        if (relative.IndexOfAny([':', '\0', '\r', '\n']) >= 0 ||
            relative.Split(Path.DirectorySeparatorChar).Any(p => p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidDataException("Alternate streams and ambiguous path components are not supported.");
        RejectLinks(full);
        return full;
    }

    public static void RejectLinks(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked paths and cloud placeholders are not supported for publication.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public static FileStream OpenRead(string path, bool allowRename = false)
    {
        Normalize(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            allowRename ? FileShare.Read | FileShare.Delete : FileShare.Read, 65536, FileOptions.Asynchronous);
        try
        {
            if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (info.NumberOfLinks != 1 || (info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new InvalidDataException("Publication requires an ordinary file with one filesystem link.");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    public static async Task<FileFingerprint> FingerprintAsync(FileStream stream, CancellationToken cancellationToken)
    {
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        stream.Position = 0;
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new(stream.Length, Convert.ToHexString(hash), info.VolumeSerial, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }

    public static FileFingerprint Fingerprint(FileStream stream)
    {
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        stream.Position = 0;
        return new(stream.Length, Convert.ToHexString(SHA256.HashData(stream)), info.VolumeSerial,
            ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }

    public static async Task<bool> MatchesAsync(string path, FileFingerprint fingerprint)
    {
        try
        {
            await using var stream = OpenRead(path, allowRename: true);
            return await FingerprintAsync(stream, CancellationToken.None) == fingerprint;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception)
        { return false; }
    }

    public static bool IsLocalNtfs(string path)
    {
        var root = Path.GetPathRoot(path);
        if (root is null || root.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var drive = new DriveInfo(root);
        return drive.IsReady && drive.DriveType == DriveType.Fixed && drive.DriveFormat == "NTFS";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
}
