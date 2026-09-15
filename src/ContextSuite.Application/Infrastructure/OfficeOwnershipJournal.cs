using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextSuite.Core.Office;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Application.Infrastructure;

internal enum OfficeOwnershipStep
{
    ProfileIntent, ProfileCreated, GrantIntent, GrantApplied, EngineIntent,
    ProcessesStopped, CleanupIntent, RevokeIntent, GrantRevoked, DeleteIntent, ProfileDeleted
}

internal sealed record OfficeOwnershipIdentity(OfficeExportWork Work, string RuntimeDirectory,
    int OwnerProcessId, long OwnerStartUtcTicks);
internal sealed record OfficeOwnershipChange(OfficeOwnershipStep Step, string? Sid = null,
    string? Path = null, string? DirectoryIdentity = null, bool? Writable = null);

// A write-ahead record, not authority to mutate paths or adopt an existing profile.
// The coordinator must verify live filesystem/profile/process identities on recovery.
internal sealed class OfficeOwnershipJournal : IDisposable
{
    private const int MaximumFrameBytes = 16384;
    private const int MaximumRecords = 32;
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 8,
        Converters = { new JsonStringEnumConverter<OfficeOwnershipStep>(allowIntegerValues: false) }
    };
    private sealed record Entry(int Version, int Sequence, OfficeOwnershipIdentity Owner, OfficeOwnershipChange Change);
    private readonly FileStream _file;
    private readonly IDisposable _directory;
    private readonly int _version;
    private readonly List<OfficeOwnershipChange> _changes = [];
    private byte[] _digest = new byte[32];
    private bool _faulted, _disposed;
    internal OfficeOwnershipIdentity Owner { get; }
    internal int Version => _version;
    internal IReadOnlyList<OfficeOwnershipChange> Changes => _changes.AsReadOnly();

    internal void RequireCreationOwner()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var process = Process.GetCurrentProcess();
        if (_version != 2 || _faulted || _changes.Count != 1 || _changes[0].Step != OfficeOwnershipStep.ProfileIntent ||
            Owner.OwnerProcessId != process.Id || Owner.OwnerStartUtcTicks != process.StartTime.ToUniversalTime().Ticks)
            throw new InvalidDataException("Only the original fresh journal owner may create this Office profile.");
    }

    private OfficeOwnershipJournal(FileStream file, IDisposable directory, OfficeOwnershipIdentity owner, int version = 2)
    { _file = file; _directory = directory; Owner = owner; _version = version; }

    internal static OfficeOwnershipJournal Create(string path, OfficeExportWork work, string runtimeDirectory)
    {
        using var process = Process.GetCurrentProcess();
        var owner = new OfficeOwnershipIdentity(work, runtimeDirectory, process.Id, process.StartTime.ToUniversalTime().Ticks);
        ValidateIdentity(path, owner, Path.GetDirectoryName(work.DirectoryPath)!, runtimeDirectory);
        var (file, directory) = OpenFile(path, FileMode.CreateNew);
        var journal = new OfficeOwnershipJournal(file, directory, owner);
        try { journal.Record(new(OfficeOwnershipStep.ProfileIntent)); return journal; }
        catch { journal.Dispose(); throw; } // Preserve a possibly incomplete file for review.
    }

    internal static OfficeOwnershipJournal Open(string path, string contextRoot, string runtimeDirectory)
    {
        var (file, directory) = OpenFile(path, FileMode.Open);
        OfficeOwnershipJournal? journal = null;
        try
        {
            if (file.Length is <= 0 or > MaximumRecords * (MaximumFrameBytes + 36))
                throw new InvalidDataException("Invalid Office ownership journal size.");
            while (file.Position < file.Length)
            {
                var header = new byte[4]; file.ReadExactly(header);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length is <= 0 or > MaximumFrameBytes) throw new InvalidDataException("Invalid Office ownership frame size.");
                var payload = new byte[length]; file.ReadExactly(payload);
                var recordedHash = new byte[32]; file.ReadExactly(recordedHash);
                var expectedHash = Hash(journal?._digest ?? new byte[32], header, payload);
                if (!CryptographicOperations.FixedTimeEquals(recordedHash, expectedHash))
                    throw new InvalidDataException("Office ownership journal integrity differs.");
                using (var document = JsonDocument.Parse(payload, new() { MaxDepth = 8 })) RejectDuplicateProperties(document.RootElement);
                var entry = JsonSerializer.Deserialize<Entry>(payload, Options) ?? throw new InvalidDataException("Missing Office ownership entry.");
                ValidateIdentity(path, entry.Owner, contextRoot, runtimeDirectory);
                if (entry.Version is not (1 or 2)) throw new InvalidDataException("Unsupported Office ownership journal version.");
                journal ??= new(file, directory, entry.Owner, entry.Version);
                if (entry.Version != journal._version || entry.Sequence != journal._changes.Count || entry.Owner != journal.Owner ||
                    journal._changes.Count >= MaximumRecords)
                    throw new InvalidDataException("Office ownership journal sequence differs.");
                journal.ValidateNext(entry.Change);
                journal._changes.Add(entry.Change); journal._digest = recordedHash;
            }
            return journal ?? throw new InvalidDataException("Missing Office ownership record.");
        }
        catch { if (journal is not null) journal.Dispose(); else { file.Dispose(); directory.Dispose(); } throw; }
    }

    internal void Record(OfficeOwnershipChange change)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_version != 2) throw new InvalidDataException("Legacy Office ownership records are read-only. Retain them for review.");
        if (_faulted || _changes.Count >= MaximumRecords) throw new IOException("Retain the Office ownership journal for recovery.");
        ValidateNext(change);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Entry(_version, _changes.Count, Owner, change), Options);
        if (payload.Length > MaximumFrameBytes) throw new InvalidDataException("Office ownership entry exceeds its limit.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        var digest = Hash(_digest, header, payload);
        try
        {
            _file.Write(header); _file.Write(payload); _file.Write(digest); _file.Flush(flushToDisk: true);
            _changes.Add(change); _digest = digest;
        }
        catch { _faulted = true; throw; }
    }

    private void ValidateNext(OfficeOwnershipChange next)
    {
        var created = false; var engine = false; var stopped = false; var cleanup = false; var deleting = false; var deleted = false;
        var grants = new List<OfficeOwnershipChange>();
        var applied = true; var revoking = false;
        string[] paths = [Owner.RuntimeDirectory, Path.Combine(Owner.Work.DirectoryPath, "input"),
            Path.Combine(Owner.Work.DirectoryPath, "output"), Path.Combine(Owner.Work.DirectoryPath, "profile"), Path.Combine(Owner.Work.DirectoryPath, "temp")];
        var index = 0;
        foreach (var change in _changes.Append(next))
        {
            if (change is null || deleted) throw new InvalidDataException("Invalid Office ownership transition.");
            var valid = change.Step switch
            {
                OfficeOwnershipStep.ProfileIntent => index == 0,
                OfficeOwnershipStep.ProfileCreated => index == 1 && change.Sid == ProfileSid(Owner.Work.ProfileName) &&
                    (_version == 1 || (string.Equals(change.Path, ExpectedProfileDirectory(Owner.Work.ProfileName), StringComparison.OrdinalIgnoreCase) &&
                        change.DirectoryIdentity is { Length: 48 } profileId && profileId.All(char.IsAsciiHexDigit))),
                OfficeOwnershipStep.GrantIntent => created && !engine && !cleanup && applied && grants.Count < paths.Length &&
                    change.Path == paths[grants.Count] && change.Writable == (grants.Count >= 2) &&
                    change.DirectoryIdentity is { Length: 48 } id && id.All(char.IsAsciiHexDigit),
                OfficeOwnershipStep.GrantApplied => created && !cleanup && !applied && change.Path == grants[^1].Path,
                OfficeOwnershipStep.EngineIntent => created && !engine && !cleanup && applied && grants.Count == paths.Length,
                OfficeOwnershipStep.ProcessesStopped => engine && !stopped && !cleanup,
                OfficeOwnershipStep.CleanupIntent => created && !cleanup && (!engine || stopped),
                OfficeOwnershipStep.RevokeIntent => cleanup && !revoking && grants.Count > 0 && change.Path == grants[^1].Path,
                OfficeOwnershipStep.GrantRevoked => revoking && change.Path == grants[^1].Path,
                OfficeOwnershipStep.DeleteIntent => cleanup && grants.Count == 0 && !deleting,
                OfficeOwnershipStep.ProfileDeleted => deleting,
                _ => false
            };
            var grantIntent = change.Step == OfficeOwnershipStep.GrantIntent;
            var profileCreated = _version == 2 && change.Step == OfficeOwnershipStep.ProfileCreated;
            var hasPath = grantIntent || profileCreated || change.Step is OfficeOwnershipStep.GrantApplied or OfficeOwnershipStep.RevokeIntent or OfficeOwnershipStep.GrantRevoked;
            if (!valid || (change.Step != OfficeOwnershipStep.ProfileCreated && change.Sid is not null) ||
                (!hasPath && change.Path is not null) || (!grantIntent && !profileCreated && change.DirectoryIdentity is not null) ||
                (!grantIntent && change.Writable is not null))
                throw new InvalidDataException("Invalid Office ownership transition or fields.");
            switch (change.Step)
            {
                case OfficeOwnershipStep.ProfileCreated: created = true; break;
                case OfficeOwnershipStep.GrantIntent: grants.Add(change); applied = false; break;
                case OfficeOwnershipStep.GrantApplied: applied = true; break;
                case OfficeOwnershipStep.EngineIntent: engine = true; break;
                case OfficeOwnershipStep.ProcessesStopped: stopped = true; break;
                case OfficeOwnershipStep.CleanupIntent: cleanup = true; break;
                case OfficeOwnershipStep.RevokeIntent: revoking = true; break;
                case OfficeOwnershipStep.GrantRevoked: grants.RemoveAt(grants.Count - 1); revoking = false; break;
                case OfficeOwnershipStep.DeleteIntent: deleting = true; break;
                case OfficeOwnershipStep.ProfileDeleted: deleted = true; break;
            }
            index++;
        }
    }

    private static void ValidateIdentity(string path, OfficeOwnershipIdentity owner, string contextRoot, string runtimeDirectory)
    {
        if (owner?.Work is null) throw new InvalidDataException("Missing Office ownership identity.");
        owner.Work.Validate();
        if (owner.OwnerProcessId <= 0 || owner.OwnerStartUtcTicks <= 0 || owner.OwnerStartUtcTicks > DateTime.MaxValue.Ticks ||
            Path.GetFileName(path) != owner.Work.ItemId.ToString("N") + ".ownership" ||
            !string.Equals(Path.GetDirectoryName(owner.Work.DirectoryPath), contextRoot, StringComparison.OrdinalIgnoreCase) ||
            owner.RuntimeDirectory != runtimeDirectory || runtimeDirectory.Length is < 4 or > 240 || runtimeDirectory.EndsWith('\\') ||
            PublicationFiles.Normalize(runtimeDirectory) != runtimeDirectory || runtimeDirectory.StartsWith(@"\\", StringComparison.Ordinal) ||
            Within(path, owner.Work.DirectoryPath) || Within(path, runtimeDirectory) ||
            Within(owner.Work.DirectoryPath, runtimeDirectory) || Within(runtimeDirectory, owner.Work.DirectoryPath))
            throw new InvalidDataException("Office ownership record differs from the expected local context.");
    }

    private static (FileStream File, IDisposable Directory) OpenFile(string path, FileMode mode)
    {
        var full = PublicationFiles.Normalize(path);
        if (full != path || !PublicationFiles.IsLocalNtfs(path)) throw new InvalidDataException("Office ownership requires an ordinary local NTFS journal.");
        var directory = OfficeSandboxOwner.LeaseDirectory(Path.GetDirectoryName(path)!);
        FileStream? file = null;
        try
        {
            // Open the leaf itself, never a substituted link. The leased parent
            // chain prevents ancestor replacement while the journal is held.
            var handle = CreateFile(path, 0xc0000000, 1, IntPtr.Zero, mode == FileMode.CreateNew ? 1U : 3U, 0x80200000, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new System.ComponentModel.Win32Exception(error);
            }
            try { file = new FileStream(handle, FileAccess.ReadWrite); }
            catch { handle.Dispose(); throw; }
            if (!GetFileInformationByHandle(file.SafeFileHandle, out var info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if (info.Links != 1 || (info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new InvalidDataException("Office ownership requires a single-link ordinary journal file.");
            return (file, directory);
        }
        catch { file?.Dispose(); directory.Dispose(); throw; }
    }

    private static bool Within(string path, string directory) => string.Equals(path, directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static byte[] Hash(byte[] previous, byte[] header, byte[] payload)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(previous); hash.AppendData(header); hash.AppendData(payload); return hash.GetHashAndReset();
    }
    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException("Repeated Office ownership field.");
            RejectDuplicateProperties(property.Value);
        }
    }
    internal static string ProfileSid(string name)
    {
        var result = DeriveAppContainerSidFromAppContainerName(name, out var sid);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
        try { return new SecurityIdentifier(sid).Value; }
        finally { FreeSid(sid); }
    }
    internal static string ExpectedProfileDirectory(string name) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", name);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _file.Dispose(); } finally { _directory.Dispose(); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IdHigh, IdLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);
    [DllImport("advapi32.dll")] private static extern IntPtr FreeSid(IntPtr sid);
}
