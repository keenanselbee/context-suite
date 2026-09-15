using System.ComponentModel;
using System.Runtime.InteropServices;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Office;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Application.Infrastructure;

// Prepares local files and durable intent only. The coordinator must create and
// clean up the profile separately, stop its worker before cleanup, and independently
// validate the PDF before publication. No customer directory is granted to Office.
internal sealed class OfficeContextPreparation : IDisposable
{
    private readonly List<IDisposable> _leases = [];
    private FileStream? _original, _snapshot;
    private FileFingerprint? _snapshotIdentity;
    private bool _disposed;
    internal OfficeExportWork Work { get; private set; } = null!;
    internal OfficeOwnershipJournal Journal { get; private set; } = null!;
    internal string OriginalPath { get; private set; } = null!;
    internal FileFingerprint OriginalIdentity { get; private set; } = null!;

    internal static async Task<OfficeContextPreparation> CreateAsync(string contextRoot, string runtimeDirectory,
        string sourcePath, string format, string calculation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var root = PublicationFiles.Normalize(contextRoot);
        var runtime = PublicationFiles.Normalize(runtimeDirectory);
        var source = PublicationFiles.Normalize(sourcePath);
        if (!PublicationFiles.IsLocalNtfs(root) || root != contextRoot || runtime != runtimeDirectory ||
            Within(root, runtime) || Within(runtime, root) || Within(source, root) || Within(source, runtime))
            throw new InvalidDataException("Use separate local Office context, runtime and original locations.");
        var id = Guid.NewGuid();
        var directory = Path.Combine(root, "office-" + id.ToString("N"));
        // Validate fixed policy and bounded paths before creating any files.
        var work = new OfficeExportWork(id, directory, "ContextSuite.Office." + id.ToString("N"),
            format, calculation, 1, new string('0', 64));
        work.Validate();
        var result = new OfficeContextPreparation { OriginalPath = source };
        try
        {
            result._leases.Add(OfficeSandboxOwner.LeaseDirectory(root));
            result._leases.Add(OfficeSandboxOwner.LeaseDirectory(runtime));
            result._leases.Add(OfficeSandboxOwner.LeaseDirectory(Path.GetDirectoryName(source)!));
            result._original = OpenFile(source, create: false);
            if (result._original.Length is <= 0 or > OfficeHostProtocol.MaximumSourceBytes)
                throw new InvalidDataException("Office source size is outside the supported limit.");
            var admission = await OfficeSourcePreflight.InspectOpenXmlAsync(source, result._original, token);
            if (admission.Refusal is not null || admission.FormatId != format)
                throw new InvalidDataException(admission.Refusal ?? "The document contents differ from the selected Office format.");
            result.OriginalIdentity = await PublicationFiles.FingerprintAsync(result._original, token);
            result.Work = work with { SourceBytes = result.OriginalIdentity.Length, SourceSha256 = result.OriginalIdentity.Sha256 };
            result.Work.Validate();
            token.ThrowIfCancellationRequested();
            result.CreateDirectory(directory);
            foreach (var name in new[] { "input", "output", "profile", "temp" })
                result.CreateDirectory(Path.Combine(directory, name));
            using (var output = OpenFile(result.Work.SourcePath, create: true))
            {
                result._original.Position = 0;
                var buffer = new byte[65536]; long copied = 0;
                while (true)
                {
                    var count = await result._original.ReadAsync(buffer, token);
                    if (count == 0) break;
                    copied += count;
                    if (copied > result.Work.SourceBytes) throw new IOException("The original document changed during preparation.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                }
                await output.FlushAsync(token); output.Flush(flushToDisk: true);
                result._snapshotIdentity = await PublicationFiles.FingerprintAsync(output, token);
                if (copied != result.Work.SourceBytes || result._snapshotIdentity.Sha256 != result.Work.SourceSha256)
                    throw new IOException("The Office snapshot differs from its original.");
                var attributes = new BasicInformation { Attributes = (uint)FileAttributes.ReadOnly };
                if (!SetFileInformationByHandle(output.SafeFileHandle, 0, ref attributes, (uint)Marshal.SizeOf<BasicInformation>()))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                output.Flush(flushToDisk: true);
            }
            result._snapshot = OpenFile(result.Work.SourcePath, create: false);
            await result.VerifyAsync(token);
            token.ThrowIfCancellationRequested();
            result.Journal = OfficeOwnershipJournal.Create(Path.Combine(root, id.ToString("N") + ".ownership"), result.Work, runtime);
            return result;
        }
        catch (Exception error)
        {
            // No profile exists at this stage. Preserve partial files as evidence;
            // never recursively delete paths after an uncertain preparation failure.
            error.Data["OfficeContextDirectory"] = directory;
            result.Release();
            throw;
        }
    }

    internal async Task VerifyAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyFile(_original!); VerifyFile(_snapshot!, readOnly: true);
        if (await PublicationFiles.FingerprintAsync(_original!, token) != OriginalIdentity ||
            await PublicationFiles.FingerprintAsync(_snapshot!, token) != _snapshotIdentity)
            throw new IOException("The original or prepared Office document changed.");
        using var original = OpenFile(OriginalPath, create: false);
        using var snapshot = OpenFile(Work.SourcePath, create: false);
        VerifyFile(snapshot, readOnly: true);
        if (await PublicationFiles.FingerprintAsync(original, token) != OriginalIdentity ||
            await PublicationFiles.FingerprintAsync(snapshot, token) != _snapshotIdentity)
            throw new IOException("The original or prepared Office document path changed.");
    }

    private void CreateDirectory(string path)
    {
        if (!CreateDirectoryNative(path, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        _leases.Add(OfficeSandboxOwner.LeaseDirectory(path));
    }

    private static FileStream OpenFile(string path, bool create)
    {
        var native = path.Length < 260 ? path : @"\\?\" + path;
        // Open the leaf itself; parent leases prevent ancestor substitution.
        var handle = CreateFile(native, create ? 0xc0000000U : 0x80000000U, create ? 0U : 1U,
            IntPtr.Zero, create ? 1U : 3U, 0x40200000U | (create ? 0x80000000U : 0U), IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        FileStream? file = null;
        try
        {
            file = new FileStream(handle, create ? FileAccess.ReadWrite : FileAccess.Read, 65536, isAsync: true);
            VerifyFile(file); return file;
        }
        catch { if (file is null) handle.Dispose(); else file.Dispose(); throw; }
    }

    private static void VerifyFile(FileStream file, bool readOnly = false)
    {
        if (!GetFileInformationByHandle(file.SafeFileHandle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (info.Links != 1 || (info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
            readOnly && (info.Attributes & (uint)FileAttributes.ReadOnly) == 0)
            throw new InvalidDataException("Office preparation requires ordinary single-link files and a read-only snapshot.");
    }

    private static bool Within(string path, string parent) => string.Equals(path, parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_disposed) return;
        if (Journal is not null && Journal.Changes.Any(change => change.Step == OfficeOwnershipStep.ProfileCreated) &&
            Journal.Changes[^1].Step != OfficeOwnershipStep.ProfileDeleted)
            throw new IOException("Clean up the Office profile before releasing its prepared source and journal.");
        Release();
    }

    private void Release()
    {
        _disposed = true;
        Journal?.Dispose(); _snapshot?.Dispose(); _original?.Dispose();
        for (var index = _leases.Count - 1; index >= 0; index--) _leases[index].Dispose();
        _leases.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IdHigh, IdLow;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInformation
    {
        public long Created, Accessed, Written, Changed;
        public uint Attributes;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryNative(string path, IntPtr security);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int kind, ref BasicInformation information, uint size);
}
