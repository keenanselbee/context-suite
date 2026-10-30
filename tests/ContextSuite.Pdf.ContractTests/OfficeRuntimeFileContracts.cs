using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using ContextSuite.Private.Office;
using Microsoft.Win32.SafeHandles;

internal static class OfficeRuntimeFileContracts
{
    internal static void Run(string root, List<string> checks)
    {
        var folder = Path.Combine(root, "authored-file-opens"); Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "target"); Directory.CreateDirectory(target);
        var file = Path.Combine(target, "fixture.txt"); File.WriteAllText(file, "Owned runtime path fixture.");
        using (var directories = new OfficeDirectoryLeases())
        {
            using var input = directories.OpenFileRead(file);
            using var reader = new StreamReader(input, leaveOpen: true);
            if (reader.ReadToEnd() != "Owned runtime path fixture.") throw new IOException("Wrong file was opened.");
            checks.Add("leased-parent open reads the authored regular file");
            try
            {
                using var writer = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                throw new InvalidOperationException("Leased file allowed a writer.");
            }
            catch (IOException error) when ((error.HResult & 0xffff) == 32) { checks.Add("leased-parent file denies write sharing"); }
            foreach (var path in new[] { file + ":stream", file + ".", file + " ", Path.Combine(target, "..", "target", "fixture.txt"),
                "relative.txt", @"\\unused.invalid\share\fixture.txt", file.Replace('\\', '/') })
                Refuse(directories, path, "noncanonical or alternate path refused");
        }
        using (var exclusive = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
            checks.Add("leased-parent file releases its handle");
        var hardLink = Path.Combine(folder, "hard-link.txt");
        if (!CreateHardLink(hardLink, file, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            using var directories = new OfficeDirectoryLeases();
            Refuse(directories, hardLink, "hard-linked leaf refused");
        }
        finally { File.Delete(hardLink); }
        foreach (var cached in new[] { false, true })
        {
            var redirected = Path.Combine(folder, cached ? "cached-directory" : "new-directory");
            Directory.CreateDirectory(redirected);
            using (var directories = new OfficeDirectoryLeases())
            {
                if (cached) directories.Open(redirected);
                using (var junction = Junction(redirected, target))
                    Refuse(directories, Path.Combine(redirected, "fixture.txt"),
                        cached ? "redirected cached parent refused by final file identity" : "pre-existing redirected parent refused");
            }
            if (File.GetAttributes(redirected).HasFlag(FileAttributes.ReparsePoint) ||
                File.ReadAllText(file) != "Owned runtime path fixture.")
                throw new IOException("Owned junction fixture was not retired or changed its target.");
            checks.Add("owned junction removed and target bytes preserved");
        }
        void Refuse(OfficeDirectoryLeases directories, string path, string label)
        {
            try { using var unexpected = directories.OpenFileRead(path); throw new IOException("Unexpected runtime file accepted."); }
            catch (InvalidDataException) { checks.Add(label); }
        }
    }

    private static IDisposable Junction(string path, string target)
    {
        var handle = CreateFile(path, 0x40000000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        try
        {
            var substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
            var print = Encoding.Unicode.GetBytes(target);
            var buffer = new byte[16 + substitute.Length + 2 + print.Length + 2];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, 0xa0000003);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), checked((ushort)(buffer.Length - 8)));
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), checked((ushort)substitute.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12), checked((ushort)(substitute.Length + 2)));
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(14), checked((ushort)print.Length));
            substitute.CopyTo(buffer, 16); print.CopyTo(buffer, 18 + substitute.Length);
            if (!DeviceIoControl(handle, 0x900a4, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new OwnedJunction(handle);
        }
        catch { handle.Dispose(); throw; }
    }

    private sealed class OwnedJunction(SafeFileHandle handle) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                var buffer = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(buffer, 0xa0000003);
                if (!DeviceIoControl(handle, 0x900ac, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { handle.Dispose(); }
        }
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string name, string existing, IntPtr security);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input,
        int inputBytes, IntPtr output, int outputBytes, out int returned, IntPtr overlapped);
}
