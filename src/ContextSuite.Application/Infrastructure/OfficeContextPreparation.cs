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
    private Retirement? _retirement;
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
            result.Journal = OfficeOwnershipJournal.Create(Path.Combine(root, id.ToString("N") + ".ownership"), result.Work, runtime,
                Retirement.ReadDirectories(result.Work));
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
        if (_retirement is not null) throw new InvalidOperationException("Office context retirement has started.");
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

    // Live-owner retirement. Durable directory bindings are also available for
    // restart recovery, which must verify owner death and native cleanup first.
    internal void Retire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_retirement is null)
        {
            if (Journal.Changes[^1].Step is not (OfficeOwnershipStep.ProfileDeleted or OfficeOwnershipStep.RetirementIntent) ||
                Journal.Changes.Any(change => change.Step == OfficeOwnershipStep.EngineIntent) &&
                !Journal.Changes.Any(change => change.Step == OfficeOwnershipStep.ProcessesStopped))
                throw new IOException("Complete Office worker and profile cleanup before removing temporary files.");
            var retirement = new Retirement(Work, Journal.Owner.ContextDirectories);
            // Capture identities while the original preparation leases still hold
            // every generated directory and the journal against replacement.
            retirement.Capture();
            if (Journal.Version == 4 && Journal.Changes[^1].Step != OfficeOwnershipStep.RetirementIntent)
                Journal.Record(new(OfficeOwnershipStep.RetirementIntent));
            _retirement = retirement;
            Journal.Dispose(); _snapshot?.Dispose(); _snapshot = null;
            for (var index = _leases.Count - 1; index >= 3; index--) _leases[index].Dispose();
            _leases.RemoveRange(3, _leases.Count - 3);
        }
        _retirement.Run();
        Release();
    }

    internal static async Task RetireRecoveredAsync(OfficeOwnershipJournal journal, string recordPath)
    {
        journal.RequireRetirementOwner();
        var work = journal.Owner.Work;
        var root = Path.GetDirectoryName(work.DirectoryPath)!;
        if (recordPath != Path.Combine(root, work.ItemId.ToString("N") + ".ownership"))
            throw new InvalidDataException("Office retirement record is outside its owned context root.");
        using var lease = OfficeSandboxOwner.LeaseDirectory(root);
        OfficeSandboxOwner.RequireProfileAbsent(work.ProfileName);
        var engine = journal.Changes.SingleOrDefault(change => change.Step == OfficeOwnershipStep.EngineIntent);
        if (engine is not null) await WorkerProcessJob.StopRecordedAsync(engine.Lifetime!);
        var retirement = new Retirement(work, journal.Owner.ContextDirectories);
        retirement.Capture(recovering: true);
        journal.Dispose();
        retirement.Run();
    }

    private sealed class Retirement(OfficeExportWork work, OfficeContextDirectories? expectedDirectories)
    {
        private const int MaximumEntries = 4096;
        private readonly Dictionary<string, string> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _record = Path.Combine(Path.GetDirectoryName(work.DirectoryPath)!, work.ItemId.ToString("N") + ".ownership");
        private string _recordIdentity = "";
        private bool _recordRemoved;

        internal void Capture(bool recovering = false)
        {
            var actual = recovering ? expectedDirectories ?? throw new InvalidDataException("Missing durable Office directory bindings.") : ReadDirectories(work);
            actual.Validate();
            if (expectedDirectories is not null && actual != expectedDirectories)
                throw new IOException("Office temporary directories differ from their durable identities.");
            string[] identities = [actual.Context, actual.Input, actual.Output, actual.Profile, actual.Temp];
            var paths = new[] { work.DirectoryPath }.Concat(new[] { "input", "output", "profile", "temp" }
                .Select(name => Path.Combine(work.DirectoryPath, name))).ToArray();
            for (var index = 0; index < paths.Length; index++)
            {
                _directories.Add(paths[index], identities[index]);
                // A crash can occur after any child deletion. Missing paths confer
                // no authority over replacements: Run verifies every present ID.
                if (recovering && IsAbsent(paths[index])) _removed.Add(paths[index]);
            }
            using var record = Open(_record, delete: false);
            if (Information(record).Attributes.HasFlag(FileAttributes.Directory)) throw new IOException("Office ownership record changed.");
            _recordIdentity = Identity(record);
        }

        internal static OfficeContextDirectories ReadDirectories(OfficeExportWork work)
        {
            return new(Read(work.DirectoryPath), Read(Path.Combine(work.DirectoryPath, "input")),
                Read(Path.Combine(work.DirectoryPath, "output")), Read(Path.Combine(work.DirectoryPath, "profile")),
                Read(Path.Combine(work.DirectoryPath, "temp")));

            static string Read(string path)
            {
                using var handle = Open(path, delete: false);
                if (!Information(handle).Attributes.HasFlag(FileAttributes.Directory)) throw new IOException("Office context directory changed.");
                return Identity(handle);
            }
        }

        internal void Run()
        {
            var entries = new List<(string Path, SafeFileHandle Handle, bool Directory)>();
            try
            {
                using var record = _recordRemoved ? null : Open(_record, delete: true);
                if (record is not null && (Identity(record) != _recordIdentity || Information(record).Attributes.HasFlag(FileAttributes.Directory)))
                    throw new IOException("Office ownership record identity changed.");
                var pending = new Stack<(string Path, int Depth)>();
                if (!_removed.Contains(work.DirectoryPath)) pending.Push((work.DirectoryPath, 0));
                long pathCharacters = 0;
                while (pending.TryPop(out var next))
                {
                    if (entries.Count == MaximumEntries || next.Depth > 32 || (pathCharacters += next.Path.Length) > 1_048_576)
                        throw new IOException("Office temporary cleanup exceeds its inspection limit.");
                    var handle = Open(next.Path, delete: true);
                    entries.Add((next.Path, handle, false));
                    var directory = Information(handle).Attributes.HasFlag(FileAttributes.Directory);
                    entries[^1] = (next.Path, handle, directory);
                    if (_directories.TryGetValue(next.Path, out var expected) && (!directory || Identity(handle) != expected) || _removed.Contains(next.Path))
                        throw new IOException("Office temporary directory identity changed.");
                    if (!directory) continue;
                    var children = Directory.EnumerateFileSystemEntries(next.Path).Take(MaximumEntries + 1).ToArray();
                    if (children.Length > MaximumEntries - entries.Count - pending.Count)
                        throw new IOException("Office temporary cleanup exceeds its inspection limit.");
                    if (next.Depth == 0 && children.Any(path => !_directories.ContainsKey(path)))
                        throw new IOException("Unexpected file beside the owned Office context directories.");
                    foreach (var child in children) pending.Push((child, next.Depth + 1));
                }
                foreach (var path in _directories.Keys)
                {
                    if (_removed.Contains(path)) RequireAbsent(path);
                    else if (!entries.Any(entry => entry.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("An owned Office context directory is missing.");
                }
                // Inspect the entire bounded tree before deleting anything. Handles
                // deny writes/renames; parents remain open until their children go.
                for (var index = entries.Count - 1; index >= 0; index--)
                {
                    var entry = entries[index];
                    Delete(entry.Handle); entry.Handle.Dispose();
                    if (entry.Directory && _directories.ContainsKey(entry.Path)) _removed.Add(entry.Path);
                    RequireAbsent(entry.Path);
                }
                RequireAbsent(work.DirectoryPath);
                if (record is not null) { Delete(record); record.Dispose(); _recordRemoved = true; }
                RequireAbsent(_record);
            }
            finally { foreach (var entry in entries) entry.Handle.Dispose(); }
        }

        private static SafeFileHandle Open(string path, bool delete)
        {
            var native = path.Length < 260 ? path : @"\\?\" + path;
            var handle = CreateFile(native, delete ? 0x10081U : 0x81U, delete ? 1U : 3U, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
            try { _ = Information(handle); return handle; }
            catch { handle.Dispose(); throw; }
        }

        private static (FileAttributes Attributes, FileInformation Native) Information(SafeFileHandle handle)
        {
            if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var attributes = (FileAttributes)info.Attributes;
            if (attributes.HasFlag(FileAttributes.ReparsePoint) || !attributes.HasFlag(FileAttributes.Directory) && info.Links != 1)
                throw new IOException("Office temporary cleanup requires ordinary directories and single-link files.");
            return (attributes, info);
        }

        private static string Identity(SafeFileHandle handle)
        {
            var info = Information(handle).Native;
            return FormattableString.Invariant($"{info.Volume:X8}{info.IdHigh:X8}{info.IdLow:X8}");
        }

        private static void Delete(SafeFileHandle handle)
        {
            _ = Information(handle);
            // FileDispositionInfoEx: DELETE | FORCE_IMAGE_SECTION_CHECK |
            // IGNORE_READONLY_ATTRIBUTE. Never clear attributes through a path.
            uint flags = 0x15;
            if (!SetDisposition(handle, 21, ref flags, sizeof(uint))) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        private static void RequireAbsent(string path)
        {
            if (IsAbsent(path)) return;
            throw new IOException("Office temporary cleanup did not remove the owned path.");
        }

        private static bool IsAbsent(string path)
        {
            try { _ = File.GetAttributes(path); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return true; }
            return false;
        }

        [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDisposition(SafeFileHandle file, int kind, ref uint flags, uint size);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (Journal is not null && Journal.Changes.Any(change => change.Step == OfficeOwnershipStep.ProfileCreated) &&
            Journal.Changes[^1].Step is not (OfficeOwnershipStep.ProfileDeleted or OfficeOwnershipStep.RetirementIntent))
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
