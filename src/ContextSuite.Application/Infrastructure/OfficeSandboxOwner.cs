using System.ComponentModel;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Application.Infrastructure;

// Application-owned lifetime, separate from the worker's kill-on-close job.
// Stop and await all using workers before cleanup. No customer paths are granted:
// callers supply only owned snapshot/output folders and the verified runtime.
[SupportedOSPlatform("windows")]
internal sealed class OfficeSandboxOwner : IDisposable, IAsyncDisposable
{
    private const int ReadAccess = 0x1200a9;
    private const int WriteAccess = 0x1301ff;
    private const AceFlags Inherit = AceFlags.ContainerInherit | AceFlags.ObjectInherit;
    private readonly object _sync = new();
    private readonly SecurityIdentifier _sid;
    private readonly List<Grant> _grants = [];
    private bool _created;
    private bool _cleaning, _deleteRecorded;
    private OfficeOwnershipJournal? _journal;

    private OfficeSandboxOwner(string name, SecurityIdentifier sid) { Name = name; _sid = sid; _created = true; }
    internal string Name { get; }
    internal string Sid => _sid.Value;

    internal (string Path, string Identity) ReadProfileIdentity()
    {
        var path = ProfileDirectory(Name, Sid);
        using var profile = new Grant(path, ReadAccess);
        profile.Open();
        return (path, profile.Identity());
    }

    // Also used to keep a journal's ordinary parent chain stable without granting access.
    internal static IDisposable LeaseDirectory(string path)
    {
        var directory = new Grant(ValidatePath(path), ReadAccess);
        try { directory.Open(requireAclWrite: false); return directory; }
        catch { directory.Dispose(); throw; }
    }

    internal static OfficeSandboxOwner Create(string name)
    {
        var suffix = name.StartsWith("ContextSuite.Office.Evaluation.", StringComparison.Ordinal)
            ? name["ContextSuite.Office.Evaluation.".Length..]
            : name.StartsWith("ContextSuite.Office.", StringComparison.Ordinal) ? name["ContextSuite.Office.".Length..] : "";
        if (!Guid.TryParseExact(suffix, "N", out var identity) || identity == Guid.Empty || suffix != identity.ToString("N"))
            throw new ArgumentException("Use a unique owned Office profile name.", nameof(name));
        var result = CreateAppContainerProfile(name, name, "Context Suite isolated Office operation", IntPtr.Zero, 0, out var nativeSid);
        if (result < 0) Marshal.ThrowExceptionForHR(result); // Never adopt/delete an existing profile on collision.
        try { return new(name, new SecurityIdentifier(nativeSid)); }
        catch
        {
            var cleanup = DeleteAppContainerProfile(name);
            if (cleanup < 0) throw new IOException("Office profile creation cleanup failed: " + name, Marshal.GetExceptionForHR(cleanup));
            throw;
        }
        finally { FreeSid(nativeSid); }
    }

    internal static OfficeSandboxOwner Create(OfficeOwnershipJournal journal)
    {
        journal.RequireCreationOwner(); // No native creation on stale, closed or uncertain records.
        var owner = Create(journal.Owner.Work.ProfileName);
        owner._journal = journal;
        try
        {
            var profile = owner.ReadProfileIdentity();
            journal.Record(new(OfficeOwnershipStep.ProfileCreated, owner.Sid, profile.Path, profile.Identity));
            return owner;
        }
        catch (Exception error)
        {
            // Creation succeeded, but its confirmation may not be durable. Keep
            // the real owner reachable; do not perform an unjournaled deletion.
            throw new OfficeOwnershipCreationException(owner, error);
        }
    }

    internal void GrantDirectory(string path, bool writable)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(!_created, this);
            if (_cleaning) throw new IOException("An Office profile being cleaned up cannot receive new grants.");
            var full = ValidatePath(path);
            if (_grants.Any(grant => Within(full, grant.Path) || Within(grant.Path, full)))
                throw new IOException("Office grant directories must not overlap.");
            var grant = new Grant(full, writable ? WriteAccess : ReadAccess);
            try
            {
                grant.Open();
                grant.CheckChildren();
                var acl = ReadAcl(grant.Handle);
                if (acl.Cast<GenericAce>().OfType<KnownAce>().Any(ace => ace.SecurityIdentifier == _sid))
                    throw new IOException("The Office profile already has unowned access on this directory.");
                grant.VerifyNoChildAccess(_sid);
                var position = 0;
                while (position < acl.Count && (acl[position].AceFlags & AceFlags.Inherited) == 0) position++;
                acl.InsertAce(position, new CommonAce(Inherit, AceQualifier.AccessAllowed, grant.Access, _sid, false, null));
                _journal?.Record(new(OfficeOwnershipStep.GrantIntent, Path: full, DirectoryIdentity: grant.Identity(), Writable: writable));
                // Track before applying: an uncertain ACL failure must remain recoverable.
                _grants.Add(grant);
                WriteAcl(grant.Handle, acl);
                if (CountGrant(ReadAcl(grant.Handle), grant.Access) != 1)
                    throw new IOException("Office directory access was not applied as requested.");
                _journal?.Record(new(OfficeOwnershipStep.GrantApplied, Path: full));
            }
            catch
            {
                if (!_grants.Contains(grant)) grant.Dispose();
                throw;
            }
        }
    }

    internal static async Task<OfficeSandboxOwner> RecoverAsync(OfficeOwnershipJournal journal)
    {
        journal.RequireRecoveryOwner();
        using var profile = VerifyProfile(journal);
        var engine = journal.Changes.SingleOrDefault(change => change.Step == OfficeOwnershipStep.EngineIntent);
        if (engine is not null)
        {
            await WorkerProcessJob.StopRecordedAsync(engine.Lifetime!).ConfigureAwait(false);
            if (!journal.Changes.Any(change => change.Step == OfficeOwnershipStep.ProcessesStopped))
                journal.Record(new(OfficeOwnershipStep.ProcessesStopped));
        }
        var created = journal.Changes.Single(change => change.Step == OfficeOwnershipStep.ProfileCreated);
        var owner = new OfficeSandboxOwner(journal.Owner.Work.ProfileName, new SecurityIdentifier(created.Sid!))
        {
            _journal = journal,
            _cleaning = journal.Changes.Any(change => change.Step == OfficeOwnershipStep.CleanupIntent),
            _deleteRecorded = journal.Changes.Any(change => change.Step == OfficeOwnershipStep.DeleteIntent)
        };
        try
        {
            // Reopen and verify every recorded object before any ACL mutation.
            // Even completed revocations must still be free of this profile SID.
            foreach (var intent in journal.Changes.Where(change => change.Step == OfficeOwnershipStep.GrantIntent))
            {
                var grant = new Grant(ValidatePath(intent.Path!), intent.Writable == true ? WriteAccess : ReadAccess);
                try
                {
                    grant.Open();
                    if (grant.Identity() != intent.DirectoryIdentity)
                        throw new IOException("An Office grant directory differs from its recorded identity. Retain it for review.");
                    grant.CheckChildren();
                    var entries = ReadAcl(grant.Handle).Cast<GenericAce>().OfType<KnownAce>().Where(ace => ace.SecurityIdentifier == owner._sid).ToArray();
                    var revoked = journal.Changes.Any(change => change.Step == OfficeOwnershipStep.GrantRevoked && change.Path == intent.Path);
                    if (entries.Length > 1 || entries.Any(ace => !owner.IsGrant(ace, grant.Access)) || revoked && entries.Length != 0)
                        throw new IOException("Office profile permissions differ from the recorded grant. Retain them for review.");
                    if (revoked) grant.VerifyNoChildAccess(owner._sid);
                    else
                    {
                        grant.RevokeRecorded = journal.Changes.Any(change => change.Step == OfficeOwnershipStep.RevokeIntent && change.Path == intent.Path);
                        owner._grants.Add(grant);
                    }
                }
                finally { if (!owner._grants.Contains(grant)) grant.Dispose(); }
            }
            if (!owner._cleaning)
            {
                journal.Record(new(OfficeOwnershipStep.CleanupIntent));
                owner._cleaning = true;
            }
            return owner;
        }
        catch
        {
            // Failed reconstruction must release leases without invoking cleanup.
            foreach (var grant in owner._grants) grant.Dispose();
            owner._grants.Clear(); owner._created = false;
            throw;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (!_created) return;
            if (!_cleaning)
            {
                _journal?.Record(new(OfficeOwnershipStep.CleanupIntent));
                _cleaning = true;
            }
            using var profile = _journal is null ? null : VerifyProfile(_journal);
            var errors = new List<Exception>();
            for (var index = _grants.Count - 1; index >= 0; index--)
            {
                var grant = _grants[index];
                try
                {
                    if (!grant.RevokeRecorded)
                    {
                        _journal?.Record(new(OfficeOwnershipStep.RevokeIntent, Path: grant.Path));
                        grant.RevokeRecorded = true;
                    }
                    // Inheritance must never follow a linked/aliased child into
                    // another tree. Retain ownership if cleanup cannot verify it.
                    grant.CheckChildren();
                    var acl = ReadAcl(grant.Handle);
                    for (var position = acl.Count - 1; position >= 0; position--)
                        if (IsGrant(acl[position], grant.Access)) acl.RemoveAce(position);
                    WriteAcl(grant.Handle, acl);
                    if (ReadAcl(grant.Handle).Cast<GenericAce>().OfType<KnownAce>().Any(ace => ace.SecurityIdentifier == _sid))
                        throw new IOException("Office directory access could not be removed.");
                    grant.VerifyNoChildAccess(_sid);
                    _journal?.Record(new(OfficeOwnershipStep.GrantRevoked, Path: grant.Path));
                    grant.Dispose(); _grants.RemoveAt(index);
                }
                catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException)
                {
                    errors.Add(new IOException("Office access cleanup failed for " + grant.Path, error));
                    if (_journal is not null) break; // Preserve strict reverse order for a retry.
                }
            }
            if (errors.Count != 0)
                throw new AggregateException("Retain and retry cleanup of Office profile " + Name + ".", errors);
            if (!_deleteRecorded)
            {
                _journal?.Record(new(OfficeOwnershipStep.DeleteIntent));
                _deleteRecorded = true;
            }
            // Windows profile deletion must run without open storage handles.
            profile?.Dispose();
            var result = DeleteAppContainerProfile(Name);
            if (result < 0) throw new IOException("Retain and retry cleanup of Office profile " + Name + ".", Marshal.GetExceptionForHR(result));
            _created = false;
            _journal?.Record(new(OfficeOwnershipStep.ProfileDeleted));
        }
    }

    public async ValueTask DisposeAsync()
    {
        for (var attempt = 0; ; attempt++)
        {
            try { Dispose(); return; }
            catch (AggregateException error) when (attempt < 49 && error.InnerExceptions.Count > 0 &&
                error.InnerExceptions.All(item => item is IOException && item.InnerException is Win32Exception native &&
                    native.NativeErrorCode is 32 or 33))
            {
                // A stopped process can leave a transient file-sharing conflict.
                // Retain ownership and rerun every safety check; never widen sharing.
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }

    private int CountGrant(RawAcl acl, int access) => acl.Cast<GenericAce>().Count(ace => IsGrant(ace, access));
    internal static IDisposable VerifyProfile(OfficeOwnershipJournal journal)
    {
        if (journal.Version < 2) throw new InvalidDataException("Profile recovery requires a recorded directory identity.");
        var created = journal.Changes.SingleOrDefault(change => change.Step == OfficeOwnershipStep.ProfileCreated)
            ?? throw new InvalidDataException("The Office profile has no confirmed creation record.");
        var path = ProfileDirectory(journal.Owner.Work.ProfileName, created.Sid!);
        var profile = new Grant(path, ReadAccess);
        try
        {
            profile.Open();
            if (!string.Equals(path, created.Path, StringComparison.OrdinalIgnoreCase) || profile.Identity() != created.DirectoryIdentity)
                throw new InvalidDataException("The Office profile directory differs from its recorded identity. Retain it for review.");
            return profile;
        }
        catch { profile.Dispose(); throw; }
    }
    private static string ProfileDirectory(string name, string sid)
    {
        var result = GetAppContainerFolderPath(sid, out var native);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
        try
        {
            var path = Marshal.PtrToStringUni(native) ?? throw new IOException("Windows returned no Office profile directory.");
            var expected = OfficeOwnershipJournal.ExpectedProfileDirectory(name);
            if (!Within(path.TrimEnd(Path.DirectorySeparatorChar), expected))
                throw new IOException("Windows returned an unexpected Office profile storage location.");
            return ValidatePath(expected);
        }
        finally { Marshal.FreeCoTaskMem(native); }
    }
    private bool IsGrant(GenericAce ace, int access) => ace is CommonAce common && common.AceFlags == Inherit &&
        common.AceQualifier == AceQualifier.AccessAllowed && common.AccessMask == access && common.SecurityIdentifier == _sid && !common.IsCallback;
    private static bool Within(string path, string parent) => string.Equals(path, parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Length > 32700 || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.Length < 4 || path[1] != ':' || path[2] != '\\') throw new IOException("Use an ordinary local grant directory.");
        var full = Path.GetFullPath(path);
        if (!string.Equals(full, path, StringComparison.OrdinalIgnoreCase) || full[3..].IndexOfAny([':', '\0', '\r', '\n', '/']) >= 0 ||
            full[3..].Split('\\').Any(part => part.Length == 0 || part.EndsWith(' ') || part.EndsWith('.')))
            throw new IOException("Ambiguous Office grant paths are not supported.");
        return full;
    }

    private static RawAcl ReadAcl(SafeFileHandle handle)
    {
        var result = GetSecurityInfo(handle, 1, 4, IntPtr.Zero, IntPtr.Zero, out _, IntPtr.Zero, out var descriptor);
        if (result != 0) throw new Win32Exception((int)result);
        try
        {
            var length = GetSecurityDescriptorLength(descriptor);
            if (length == 0 || length > 1024 * 1024) throw new IOException("Invalid directory security descriptor.");
            var bytes = new byte[length]; Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            return new RawSecurityDescriptor(bytes, 0).DiscretionaryAcl ?? throw new IOException("Unrestricted directory ACL is not supported.");
        }
        finally { LocalFree(descriptor); }
    }

    private static void WriteAcl(SafeFileHandle handle, RawAcl acl)
    {
        var bytes = new byte[acl.BinaryLength]; acl.GetBinaryForm(bytes, 0);
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var result = SetSecurityInfo(handle, 1, 4, IntPtr.Zero, IntPtr.Zero, buffer, IntPtr.Zero);
            if (result != 0) throw new Win32Exception((int)result);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private sealed class Grant(string path, int access) : IDisposable
    {
        private readonly List<SafeFileHandle> _handles = [];
        private readonly List<(string Path, SafeFileHandle Handle)> _children = [];
        private SafeFileHandle? _root;
        internal string Path { get; } = path;
        internal int Access { get; } = access;
        internal bool RevokeRecorded { get; set; }
        internal SafeFileHandle Handle => _root ?? throw new InvalidOperationException("Grant directory is not open.");
        internal string Identity()
        {
            if (!ReadDirectoryIdentity(Handle, 18, out var value, (uint)Marshal.SizeOf<DirectoryIdentity>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Span<byte> identifier = stackalloc byte[16];
            BinaryPrimitives.WriteUInt64LittleEndian(identifier, value.Low);
            BinaryPrimitives.WriteUInt64LittleEndian(identifier[8..], value.High);
            return FormattableString.Invariant($"{value.Volume:X16}") + Convert.ToHexString(identifier);
        }
        internal void Open(bool requireAclWrite = true)
        {
            var chain = new Stack<string>();
            for (var current = Path; current is not null; current = System.IO.Path.GetDirectoryName(current)) chain.Push(current);
            while (chain.TryPop(out var current))
            {
                // FILE_LIST_DIRECTORY participates in sharing checks; an
                // attributes-only handle does not prevent directory renaming.
                var native = current.Length < 260 ? current : @"\\?\" + current;
                var handle = CreateFile(native, current == Path && requireAclWrite ? 0x60081U : 0x81U, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
                _handles.Add(handle);
                if (!GetFileInformationByHandleEx(handle, 9, out var attributes, (uint)Marshal.SizeOf<AttributeTag>()) ||
                    (attributes.Attributes & 0x400) != 0 || (attributes.Attributes & 0x10) == 0)
                    throw new IOException("Office grant paths must contain only ordinary directories.");
                var final = new StringBuilder(32768);
                var count = GetFinalPathNameByHandle(handle, final, (uint)final.Capacity, 0);
                if (count <= 4 || count >= final.Capacity || !final.ToString().StartsWith(@"\\?\", StringComparison.Ordinal) ||
                    !string.Equals(final.ToString()[4..], current, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Office grant directory identity changed.");
            }
            _root = _handles[^1];
        }
        internal void CheckChildren()
        {
            var pending = new Stack<string>(); pending.Push(Path);
            var checkedHandles = new List<(string Path, SafeFileHandle Handle)>();
            try
            {
                while (pending.TryPop(out var directory))
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        if (checkedHandles.Count >= 65536) throw new IOException("Office grant tree exceeds its entry budget.");
                        var native = entry.Length < 260 ? entry : @"\\?\" + entry;
                        var handle = CreateFile(native, 0x80000000, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                        if (handle.IsInvalid)
                        {
                            var error = Marshal.GetLastWin32Error(); handle.Dispose();
                            throw new Win32Exception(error, "Could not verify Office grant entry: " + entry);
                        }
                        checkedHandles.Add((entry, handle));
                        if (!GetFileInformationByHandleEx(handle, 9, out var attributes, (uint)Marshal.SizeOf<AttributeTag>()) ||
                            (attributes.Attributes & 0x400) != 0) throw new IOException("Linked entries are not permitted in Office grant trees.");
                        if ((attributes.Attributes & 0x10) != 0) pending.Push(entry);
                        else if (!GetStandardInformation(handle, 1, out var file, (uint)Marshal.SizeOf<StandardInformation>()) ||
                            file.Links != 1 || file.DeletePending || file.Directory)
                            throw new IOException("Office grant files must be single-link regular files.");
                    }
                foreach (var child in _children) child.Handle.Dispose();
                _children.Clear(); _children.AddRange(checkedHandles); checkedHandles.Clear();
            }
            finally { foreach (var child in checkedHandles) child.Handle.Dispose(); }
        }
        internal void VerifyNoChildAccess(SecurityIdentifier sid)
        {
            foreach (var child in _children)
                if (ReadAcl(child.Handle).Cast<GenericAce>().OfType<KnownAce>().Any(ace => ace.SecurityIdentifier == sid))
                    throw new IOException("Unresolved Office profile access on child: " + child.Path);
        }
        public void Dispose()
        {
            foreach (var child in _children) child.Handle.Dispose(); _children.Clear();
            foreach (var handle in _handles) handle.Dispose(); _handles.Clear(); _root = null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTag { public uint Attributes, Tag; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DirectoryIdentity { public ulong Volume, Low, High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct StandardInformation
    {
        public long Allocation, Bytes;
        public uint Links;
        [MarshalAs(UnmanagedType.U1)] public bool DeletePending;
        [MarshalAs(UnmanagedType.U1)] public bool Directory;
    }
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int CreateAppContainerProfile(string name, string display, string description, IntPtr capabilities, uint count, out IntPtr sid);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)] private static extern int DeleteAppContainerProfile(string name);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)] private static extern int GetAppContainerFolderPath(string sid, out IntPtr path);
    [DllImport("advapi32.dll")] private static extern IntPtr FreeSid(IntPtr sid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint GetSecurityInfo(SafeFileHandle handle, int type, uint information, IntPtr owner, IntPtr group, out IntPtr dacl, IntPtr sacl, out IntPtr descriptor);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint SetSecurityInfo(SafeFileHandle handle, int type, uint information, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);
    [DllImport("advapi32.dll")] private static extern uint GetSecurityDescriptorLength(IntPtr descriptor);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out AttributeTag information, uint bytes);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetStandardInformation(SafeFileHandle handle, int kind, out StandardInformation information, uint bytes);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadDirectoryIdentity(SafeFileHandle handle, int kind, out DirectoryIdentity information, uint bytes);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}

internal sealed class OfficeOwnershipCreationException(OfficeSandboxOwner owner, Exception inner)
    : IOException("Office profile creation completed but its ownership record failed. Retain profile " + owner.Name + ".", inner)
{
    internal OfficeSandboxOwner Owner { get; } = owner;
}
