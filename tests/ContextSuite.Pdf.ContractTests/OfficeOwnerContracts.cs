using System.Diagnostics;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;
using ContextSuite.Private.IO;
using ContextSuite.Private.Office;
using Microsoft.Win32;

[SupportedOSPlatform("windows")]
internal static class OfficeOwnerContracts
{
    private static readonly string[] Names = ["APPDATA", "LOCALAPPDATA", "PATH", "SYSTEMROOT", "TEMP", "TMP", "USERPROFILE", "WINDIR",
        "CS_OFFICE_PROFILE_NAME", "CS_OFFICE_REQUEST_ID", "CS_OFFICE_FORMAT", "CS_OFFICE_CALCULATION", "CS_OFFICE_SOURCE", "CS_OFFICE_OUTPUT",
        "CS_OFFICE_PROFILE", "CS_OFFICE_PROGRAM", "CS_OFFICE_SOURCE_IDENTITY", "CS_OFFICE_OUTPUT_IDENTITY", "CS_OFFICE_PROFILE_IDENTITY", "CS_OFFICE_PROGRAM_IDENTITY"];

    internal static async Task WorkerAsync(string executable, string directory, string expectedHash)
    {
        RequireOwned(directory);
        using var host = MediaFiles.OpenRead(executable);
        if (Convert.ToHexString(SHA256.HashData(host)) != expectedHash) throw new IOException("Ownership probe hash mismatch.");
        var environment = Names.ToDictionary(name => name, name => Environment.GetEnvironmentVariable(name) ?? throw new IOException("Missing owner test environment."));
        var reply = await OfficeSandboxProcess.RunAsync(executable, directory, environment, CancellationToken.None);
        Console.Write(System.Text.Encoding.UTF8.GetString(reply));
    }

    internal static async Task RunAsync(string probe, string expectedHash, string stage)
    {
        RequireOwned(stage);
        if (!Guid.TryParseExact(Path.GetFileName(stage), "N", out _) || Directory.Exists(stage)) throw new IOException("Use a fresh owned stage GUID.");
        using (var native = MediaFiles.OpenRead(probe))
            if (Convert.ToHexString(SHA256.HashData(native)) != expectedHash) throw new IOException("Ownership probe differs from its build receipt.");
        var checks = new List<string>();
        void Check(bool result, string name) { if (!result) throw new IOException(name); checks.Add(name); }
        var binary = Path.Combine(stage, "binary"); var readable = Path.Combine(stage, "readable");
        var writable = Path.Combine(stage, "writable"); var denied = Path.Combine(stage, "withheld");
        foreach (var directory in new[] { binary, readable, writable, denied }) Directory.CreateDirectory(directory);
        var hostPath = Path.Combine(binary, "ContextSuite.Office.ProcessProbe.exe"); File.Copy(probe, hostPath);
        var input = Path.Combine(readable, "input.txt"); var secret = Path.Combine(denied, "input.txt");
        File.WriteAllText(input, "authored readable input"); File.WriteAllText(secret, "authored withheld input");
        var preserved = Path.Combine(writable, "existing.txt"); File.WriteAllText(preserved, "existing writable content");
        var longDirectory = Path.Combine(writable, new string('d', 80), new string('e', 80), new string('f', 80));
        Directory.CreateDirectory(longDirectory);
        var longFile = Path.Combine(longDirectory, "owned-cache.txt"); File.WriteAllText(longFile, "authored long-path cache");
        Check(longFile.Length > 260, "authored Office cache path exceeds legacy MAX_PATH");
        var hashes = new[] { input, secret, preserved, hostPath, longFile }.ToDictionary(path => path, Hash);
        var profileName = "ContextSuite.Office.Evaluation." + Path.GetFileName(stage);
        var user = Path.Combine(writable, "user"); var local = Path.Combine(user, "AppData", "Local");
        var roaming = Path.Combine(user, "AppData", "Roaming"); var temp = Path.Combine(writable, "temp");
        foreach (var directory in new[] { local, roaming, temp, Path.Combine(local, "Packages", profileName.ToLowerInvariant(), "AC", "Temp") }) Directory.CreateDirectory(directory);
        var files = new[] { binary, hostPath, readable, input, writable, preserved, denied, secret, longDirectory, longFile };
        var before = files.ToDictionary(path => path, path => Aces(path, null));
        var namePath = Path.Combine(stage, "profile-name.txt"); File.WriteAllText(namePath, profileName);
        var owner = OfficeSandboxOwner.Create(profileName);
        File.WriteAllText(Path.Combine(stage, "profile-sid.txt"), owner.Sid);
        var profileFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", profileName);
        var mapping = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + owner.Sid;
        try
        {
            Check(Directory.Exists(profileFolder) && MappingExists(mapping), "application owner creates the Windows profile");
            var profileIdentity = owner.ReadProfileIdentity();
            Check(profileIdentity.Path == profileFolder && profileIdentity.Identity.Length == 48,
                "Windows profile storage resolves to an ordinary owned directory identity");
            try { using var collision = OfficeSandboxOwner.Create(profileName); throw new IOException("Existing profile was adopted."); }
            catch (Exception error) when ((uint)error.HResult == 0x800700b7) { checks.Add("profile collision refuses without adopting ownership"); }
            Check(Directory.Exists(profileFolder) && MappingExists(mapping), "collision leaves the real owner intact");
            var aliased = Path.Combine(stage, "aliased"); Directory.CreateDirectory(aliased);
            var linked = Path.Combine(aliased, "copy.txt");
            Check(CreateHardLink(linked, secret, IntPtr.Zero), "create owned hard-link refusal fixture");
            var aliasBefore = Aces(secret, null);
            try { owner.GrantDirectory(aliased, false); throw new IOException("Hard-linked grant was accepted."); }
            catch (IOException error) when (error.Message == "Office grant files must be single-link regular files.") { checks.Add("hard-linked grant refuses before ACL mutation"); }
            Check(Aces(secret, null).SequenceEqual(aliasBefore) && !HasSid(linked, owner.Sid), "hard-link refusal preserves the other path ACL");
            File.Delete(linked); // Remove only the freshly created alias, retaining its source.
            var junction = Path.Combine(aliased, "linked-directory");
            CreateJunction(junction, denied);
            try
            {
                try { owner.GrantDirectory(aliased, false); throw new IOException("Linked child grant was accepted."); }
                catch (IOException error) when (error.Message == "Linked entries are not permitted in Office grant trees.") { checks.Add("linked child refuses before ACL propagation"); }
                try { owner.GrantDirectory(junction, false); throw new IOException("Linked root grant was accepted."); }
                catch (IOException error) when (error.Message == "Office grant paths must contain only ordinary directories.") { checks.Add("linked grant root refuses"); }
                Check(Aces(secret, null).SequenceEqual(aliasBefore) && !HasSid(denied, owner.Sid), "link refusal preserves withheld tree permissions");
            }
            finally { Directory.Delete(junction); }
            owner.GrantDirectory(binary, false); owner.GrantDirectory(readable, false); owner.GrantDirectory(writable, true);
            Check(Aces(input, owner.Sid).SequenceEqual(before[input]) && Aces(hostPath, owner.Sid).SequenceEqual(before[hostPath]), "grant preserves unrelated child entries");
            Check(HasSid(input, owner.Sid) && HasSid(preserved, owner.Sid), "grants propagate to existing files");
            Check(HasSid(longFile, owner.Sid), "grant reaches the long-path cache file");
            var createdAfter = Path.Combine(readable, "later.txt"); File.WriteAllText(createdAfter, "new inherited file");
            Check(HasSid(createdAfter, owner.Sid), "new files inherit the owned read grant");
            try { owner.GrantDirectory(readable, false); throw new IOException("Duplicate grant accepted."); }
            catch (IOException error) when (error.Message == "Office grant directories must not overlap.") { checks.Add("duplicate grants refuse"); }
            try { owner.GrantDirectory(stage, false); throw new IOException("Overlapping ancestor accepted."); }
            catch (IOException error) when (error.Message == "Office grant directories must not overlap.") { checks.Add("overlapping ancestor grants refuse"); }
            var renamed = Path.GetFullPath(Path.Combine(stage, "readable-renamed"));
            Check(renamed.StartsWith(Path.GetFullPath(stage) + Path.DirectorySeparatorChar, StringComparison.Ordinal), "rename target stays in owned stage");
            try { Directory.Move(readable, renamed); throw new IOException("Grant target moved while leased."); }
            catch (IOException) when (Directory.Exists(readable) && !Directory.Exists(renamed)) { checks.Add("active grant leases prevent directory replacement"); }
            var environment = MakeEnvironment("access-test", "first.txt");
            var reply = await RunWorkerAsync(environment, "access-success");
            Check(reply.GetProperty("sourceRead").GetInt32() == 0 && reply.GetProperty("sourceWrite").GetInt32() == 5 &&
                reply.GetProperty("withheldRead").GetInt32() == 5 && reply.GetProperty("outputWrite").GetInt32() == 0,
                "native child verifies read/write/withheld grants");
            var security = new DirectoryInfo(readable).GetAccessControl(AccessControlSections.Access);
            using var identity = WindowsIdentity.GetCurrent();
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ReadAttributes, AccessControlType.Allow));
            new DirectoryInfo(readable).SetAccessControl(security);
            var concurrentEntries = Aces(readable, owner.Sid);
            Check(!concurrentEntries.SequenceEqual(before[readable]), "test adds an unrelated ACL change during ownership");

            var crashEnvironment = MakeEnvironment("access-hold", "crash.txt");
            using (var worker = StartWorker(crashEnvironment))
            {
                var stdout = worker.StandardOutput.ReadToEndAsync(); var stderr = worker.StandardError.ReadToEndAsync();
                var marker = crashEnvironment["CS_OFFICE_PROFILE"];
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try
                {
                    while (!File.Exists(marker) && !worker.HasExited) await Task.Delay(10, timeout.Token);
                    Check(!worker.HasExited && File.Exists(marker), "worker crash waits for a running native child");
                    string[] child;
                    do { child = File.ReadAllText(marker).Split(' ', StringSplitOptions.RemoveEmptyEntries); if (child.Length != 2) await Task.Delay(10, timeout.Token); }
                    while (child.Length != 2);
                    using var host = Process.GetProcessById(int.Parse(child[0])); _ = host.Handle;
                    Check(host.StartTime.ToUniversalTime().ToFileTimeUtc() == long.Parse(child[1]) && !host.HasExited, "retain the exact live native child handle");
                    worker.Kill(); // Kill the worker alone; its job must stop the child.
                    await worker.WaitForExitAsync(timeout.Token); await host.WaitForExitAsync(timeout.Token);
                    Check(host.HasExited, "worker loss closes its job and stops the retained native child");
                    await Task.WhenAll(stdout, stderr);
                    Check(Directory.Exists(profileFolder) && MappingExists(mapping), "Windows profile remains owned by the surviving application");
                }
                finally
                {
                    if (!worker.HasExited) worker.Kill(true);
                    await worker.WaitForExitAsync();
                    await Task.WhenAll(stdout, stderr);
                }
            }
            var lockedCache = Path.Combine(writable, "locked-cache.txt");
            File.WriteAllText(lockedCache, "authored transient cleanup lock");
            Task cleanup;
            using (var locked = new FileStream(lockedCache, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { owner.Dispose(); throw new IOException("Locked cleanup unexpectedly succeeded."); }
                catch (AggregateException error)
                {
                    Check(error.InnerExceptions.Any(item => item.InnerException is System.ComponentModel.Win32Exception native &&
                        native.NativeErrorCode == 32 && native.Message.Contains(lockedCache, StringComparison.Ordinal)),
                        "cleanup identifies the exact locked entry and preserves its native sharing error");
                }
                Check(Directory.Exists(profileFolder) && MappingExists(mapping) && HasSid(writable, owner.Sid),
                    "failed cleanup retains the profile and unresolved grant for retry");
                try
                {
                    await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                    throw new IOException("Persistent cleanup lock unexpectedly succeeded.");
                }
                catch (AggregateException error)
                {
                    Check(error.InnerExceptions.Any(item => item.InnerException is System.ComponentModel.Win32Exception native &&
                        native.NativeErrorCode == 32), "persistent sharing conflict exhausts bounded retries with its original error");
                }
                Check(Directory.Exists(profileFolder) && MappingExists(mapping) && HasSid(writable, owner.Sid),
                    "retry exhaustion preserves ownership and unresolved access for later recovery");
                cleanup = owner.DisposeAsync().AsTask();
                Check(!cleanup.IsCompleted && Directory.Exists(profileFolder) && MappingExists(mapping),
                    "asynchronous cleanup keeps ownership while the sharing conflict remains");
            }
            await cleanup.WaitAsync(TimeSpan.FromSeconds(10));
            owner.Dispose();
            Check(!HasSid(lockedCache, owner.Sid) && File.ReadAllText(lockedCache) == "authored transient cleanup lock",
                "same owner retries cleanup after the lock is released without changing file bytes");
            Check(!Directory.Exists(profileFolder) && !MappingExists(mapping), "application cleans profile folder and mapping after worker exit");
            foreach (var path in files)
                Check(!HasSid(path, owner.Sid) && Aces(path, null).SequenceEqual(path == readable ? concurrentEntries : before[path]), "restore only owned grant: " + Path.GetRelativePath(stage, path));
            Check(!HasSid(createdAfter, owner.Sid), "cleanup revokes the inherited grant on a later file");
            foreach (var path in new[] { Path.Combine(writable, "first.txt"), Path.Combine(writable, "crash.txt") })
                Check(!HasSid(path, owner.Sid), "cleanup revokes the grant on native-created output");
            Directory.Move(readable, renamed); Directory.Move(renamed, readable); checks.Add("cleanup releases directory leases");
            using (var reused = OfficeSandboxOwner.Create(profileName))
            {
                Check(reused.Sid == owner.Sid, "recreated profile has the same deterministic SID");
                owner.Dispose();
                Check(Directory.Exists(profileFolder) && MappingExists(mapping), "old owner disposal cannot delete a later profile");
                reused.GrantDirectory(binary, false);
                var revoked = await RunWorkerAsync(MakeEnvironment("access-revoked", "revoked.txt"), "access-revoked");
                Check(revoked.EnumerateObject().All(property => property.Value.GetInt32() == 5), "recreated SID has no revoked source/output access");
            }
            Check(!Directory.Exists(profileFolder) && !MappingExists(mapping), "second profile is also removed");
            await CheckRetainedChildAccessAsync(false);
            await CheckRetainedChildAccessAsync(true);
            await CheckJournalOwnerAsync();
            await OfficeRecoveryContracts.RunAsync(stage, Check);
            foreach (var pair in hashes) Check(Hash(pair.Key) == pair.Value, "preserve original bytes: " + Path.GetRelativePath(stage, pair.Key));
            File.WriteAllText(Path.Combine(stage, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks, Profile = profileName, Sid = owner.Sid }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Passed " + checks.Count + " Office profile/grant ownership checks: " + stage);
        }
        finally { owner.Dispose(); }

        async Task CheckJournalOwnerAsync()
        {
            var id = Guid.ParseExact(Path.GetFileName(stage), "N");
            var contextRoot = Path.Combine(stage, "contexts");
            var work = new OfficeExportWork(id, Path.Combine(contextRoot, "office-" + id.ToString("N")), profileName,
                "docx", "none", new FileInfo(input).Length, Hash(input));
            string[] paths = [binary, Path.Combine(work.DirectoryPath, "input"), Path.Combine(work.DirectoryPath, "output"),
                Path.Combine(work.DirectoryPath, "profile"), Path.Combine(work.DirectoryPath, "temp")];
            foreach (var path in paths.Skip(1)) Directory.CreateDirectory(path);
            File.Copy(input, work.SourcePath);
            var journalPath = Path.Combine(stage, id.ToString("N") + ".ownership");
            using (var journal = OfficeOwnershipJournal.Create(journalPath, work, binary))
            {
                using (var collisionOwner = OfficeSandboxOwner.Create(profileName))
                {
                    try { using var duplicate = OfficeSandboxOwner.Create(journal); throw new IOException("Journal adopted an existing profile."); }
                    catch (Exception error) when ((uint)error.HResult == 0x800700b7)
                    { checks.Add("journaled creation refuses an existing profile without adopting it"); }
                    Check(journal.Changes.Count == 1 && Directory.Exists(profileFolder) && MappingExists(mapping),
                        "creation collision retains intent and leaves the existing owner intact");
                }
                var journalOwner = OfficeSandboxOwner.Create(journal);
                try
                {
                    using (var profileLeases = new OfficeDirectoryLeases())
                    {
                        var profileId = OfficeDirectoryLeases.Identity(profileLeases.Open(profileFolder));
                        Check(journal.Version == 3 && journal.Changes[^1] == new OfficeOwnershipChange(OfficeOwnershipStep.ProfileCreated,
                            journalOwner.Sid, profileFolder, profileId) && Directory.Exists(profileFolder) && MappingExists(mapping),
                            "journal confirms the actual Windows profile SID and directory identity");
                    }
                    var deniedAcl = Aces(denied, null);
                    try { journalOwner.GrantDirectory(denied, true); throw new IOException("Unplanned grant was accepted."); }
                    catch (InvalidDataException) { checks.Add("journaled owner refuses an unplanned grant before mutation"); }
                    Check(journal.Changes.Count == 2 && Aces(denied, null).SequenceEqual(deniedAcl),
                        "unplanned grant refusal preserves the journal and unrelated directory permissions");
                    for (var index = 0; index < paths.Length; index++)
                    {
                        journalOwner.GrantDirectory(paths[index], index >= 2);
                        using var leases = new OfficeDirectoryLeases();
                        var actual = OfficeDirectoryLeases.Identity(leases.Open(paths[index]));
                        Check(journal.Changes[^2] == new OfficeOwnershipChange(OfficeOwnershipStep.GrantIntent,
                            Path: paths[index], DirectoryIdentity: actual, Writable: index >= 2) &&
                            journal.Changes[^1] == new OfficeOwnershipChange(OfficeOwnershipStep.GrantApplied, Path: paths[index]) &&
                            HasSid(paths[index], journalOwner.Sid), "journal binds applied grant to its actual Windows directory identity: " + index);
                    }
                    journal.RecordEngineIntent(WorkerLifetimeIdentity.Create(Guid.NewGuid())); // Exercise the gate; this case launches no engine.
                    try { journalOwner.Dispose(); throw new IOException("Journal allowed cleanup before recorded shutdown."); }
                    catch (InvalidDataException) { checks.Add("journaled owner refuses cleanup before recorded process shutdown"); }
                    Check(journal.Changes.Count == 13 && paths.All(path => HasSid(path, journalOwner.Sid)) && Directory.Exists(profileFolder),
                        "missing shutdown leaves every native grant and the profile unchanged");
                    journal.Record(new(OfficeOwnershipStep.ProcessesStopped));
                    var retainedProfile = Path.GetFullPath(profileFolder + ".retained");
                    var packages = Path.GetDirectoryName(Path.GetFullPath(profileFolder));
                    Check(Path.GetDirectoryName(retainedProfile) == packages &&
                        Path.GetFileName(profileFolder) == profileName && !Directory.Exists(retainedProfile),
                        "profile replacement fixture uses only the exact disposable profile and its fresh sibling");
                    Directory.Move(profileFolder, retainedProfile);
                    try
                    {
                        Directory.CreateDirectory(profileFolder);
                        try { journalOwner.Dispose(); throw new IOException("A replacement profile folder was accepted."); }
                        catch (InvalidDataException error) when (error.Message.StartsWith("The Office profile directory differs", StringComparison.Ordinal))
                        { checks.Add("journaled cleanup refuses an actually replaced profile folder before revoking grants"); }
                        Check(journal.Changes.Count == 15 && paths.All(path => HasSid(path, journalOwner.Sid)) &&
                            Directory.Exists(retainedProfile) && MappingExists(mapping),
                            "replacement refusal preserves the original profile, mapping and all runtime/context grants");
                    }
                    finally
                    {
                        // The replacement is an empty test-created directory. Refuse
                        // unexpected contents rather than recursively deleting them.
                        Directory.Delete(profileFolder);
                        Directory.Move(retainedProfile, profileFolder);
                    }
                    var lockedPath = Path.Combine(paths[^1], "locked.txt"); File.WriteAllText(lockedPath, "authored journal cleanup lock");
                    using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        for (var attempt = 0; attempt < 2; attempt++)
                        {
                            try { journalOwner.Dispose(); throw new IOException("Journal cleanup ignored a sharing lock."); }
                            catch (AggregateException error)
                            {
                                Check(error.InnerExceptions.Any(item => item.InnerException is System.ComponentModel.Win32Exception native && native.NativeErrorCode == 32),
                                    "journaled cleanup preserves the native sharing failure: " + attempt);
                            }
                            Check(journal.Changes.Count == 16 && journal.Changes[^1] == new OfficeOwnershipChange(OfficeOwnershipStep.RevokeIntent, Path: paths[^1]) &&
                                paths.All(path => HasSid(path, journalOwner.Sid)) && Directory.Exists(profileFolder) && MappingExists(mapping),
                                "journaled cleanup retains reverse order without duplicate intent or earlier revocation: " + attempt);
                        }
                    }
                    await journalOwner.DisposeAsync();
                    Check(journal.Changes.Count == 27 && journal.Changes[^1].Step == OfficeOwnershipStep.ProfileDeleted &&
                        !Directory.Exists(profileFolder) && !MappingExists(mapping) && paths.All(path => !HasSid(path, journalOwner.Sid)),
                        "journaled cleanup completes all recorded revocations and actual profile deletion after lock release");
                    Check(!HasSid(lockedPath, journalOwner.Sid) && Hash(work.SourcePath) == work.SourceSha256,
                        "journaled cleanup removes child access and preserves the source snapshot");
                }
                finally
                {
                    if (journal.Changes[^1].Step == OfficeOwnershipStep.EngineIntent) journal.Record(new(OfficeOwnershipStep.ProcessesStopped));
                    await journalOwner.DisposeAsync();
                }
            }
            using var replay = OfficeOwnershipJournal.Open(journalPath, contextRoot, binary);
            Check(replay.Changes.Count == 27 && replay.Changes[^1].Step == OfficeOwnershipStep.ProfileDeleted,
                "reopened native ownership journal retains the complete checked lifecycle");
            try { using var reusedJournal = OfficeSandboxOwner.Create(replay); throw new IOException("Completed journal created another profile."); }
            catch (InvalidDataException) { checks.Add("completed ownership journal cannot create a replacement profile"); }
            Check(!Directory.Exists(profileFolder) && !MappingExists(mapping), "refused journal reuse leaves the profile absent");
        }

        async Task CheckRetainedChildAccessAsync(bool protectedDirectory)
        {
            var label = protectedDirectory ? "protected-directory" : "explicit-file";
            var root = Path.Combine(stage, label);
            var child = Path.Combine(root, "child"); Directory.CreateDirectory(child);
            var file = Path.Combine(child, "content.txt"); File.WriteAllText(file, "authored child-access recovery input");
            var hash = Hash(file);
            var target = protectedDirectory ? child : file;
            var childOwner = OfficeSandboxOwner.Create(profileName);
            var sid = new SecurityIdentifier(childOwner.Sid);
            try
            {
                FileSystemSecurity access = protectedDirectory ? new DirectoryInfo(target).GetAccessControl(AccessControlSections.Access)
                    : new FileInfo(target).GetAccessControl(AccessControlSections.Access);
                access.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.Read, AccessControlType.Allow));
                SetFixtureAccess(access);
                var beforeRoot = Aces(root, null); var beforeChild = Aces(target, null);
                try { childOwner.GrantDirectory(root, true); throw new IOException("Unowned child access was accepted."); }
                catch (IOException error) when (error.Message == "Unresolved Office profile access on child: " + target)
                { checks.Add(label + ": existing child SID refuses before inheritance changes"); }
                Check(Aces(root, null).SequenceEqual(beforeRoot) && Aces(target, null).SequenceEqual(beforeChild),
                    label + ": refused grant preserves root and child ACLs");
                RemoveFixtureAccess();
                childOwner.GrantDirectory(root, true);
                access = protectedDirectory ? new DirectoryInfo(target).GetAccessControl(AccessControlSections.Access)
                    : new FileInfo(target).GetAccessControl(AccessControlSections.Access);
                if (protectedDirectory) access.SetAccessRuleProtection(true, true);
                else access.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.Read, AccessControlType.Allow));
                SetFixtureAccess(access);
                var unrelated = Aces(target, childOwner.Sid);
                Check(HasSid(target, childOwner.Sid), label + ": child retains a separate profile access entry");
                try { await childOwner.DisposeAsync(); throw new IOException("Residual child access was accepted as cleanup."); }
                catch (AggregateException error)
                {
                    Check(error.InnerExceptions.Any(item => item.InnerException is IOException detail &&
                        detail.Message.StartsWith("Unresolved Office profile access on child: ", StringComparison.Ordinal)),
                        label + ": cleanup reports retained child access");
                }
                Check(!HasSid(root, childOwner.Sid) && HasSid(target, childOwner.Sid) &&
                    Directory.Exists(profileFolder) && MappingExists(mapping),
                    label + ": root revocation alone does not release profile ownership");
                Check(Aces(target, childOwner.Sid).SequenceEqual(unrelated) && Hash(file) == hash,
                    label + ": failed cleanup preserves unrelated child entries and bytes");
                RemoveFixtureAccess();
                await childOwner.DisposeAsync();
                Check(!HasSid(file, childOwner.Sid) && !HasSid(child, childOwner.Sid) &&
                    !Directory.Exists(profileFolder) && !MappingExists(mapping),
                    label + ": same owner completes only after child access is resolved");
                Check(Aces(target, null).SequenceEqual(unrelated) && Hash(file) == hash,
                    label + ": successful recovery preserves unrelated entries and bytes");
            }
            finally { RemoveFixtureAccess(); await childOwner.DisposeAsync(); }

            void SetFixtureAccess(FileSystemSecurity access)
            {
                if (protectedDirectory) new DirectoryInfo(target).SetAccessControl((DirectorySecurity)access);
                else new FileInfo(target).SetAccessControl((FileSecurity)access);
            }
            void RemoveFixtureAccess()
            {
                FileSystemSecurity access = protectedDirectory ? new DirectoryInfo(target).GetAccessControl(AccessControlSections.Access)
                    : new FileInfo(target).GetAccessControl(AccessControlSections.Access);
                access.PurgeAccessRules(sid); SetFixtureAccess(access);
            }
        }

        Dictionary<string, string> MakeEnvironment(string policy, string output)
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return new() { ["APPDATA"] = roaming, ["LOCALAPPDATA"] = local, ["PATH"] = Path.Combine(windows, "System32"), ["SYSTEMROOT"] = windows,
                ["TEMP"] = temp, ["TMP"] = temp, ["USERPROFILE"] = user, ["WINDIR"] = windows, ["CS_OFFICE_PROFILE_NAME"] = profileName,
                ["CS_OFFICE_REQUEST_ID"] = Guid.NewGuid().ToString("N"), ["CS_OFFICE_FORMAT"] = "docx", ["CS_OFFICE_CALCULATION"] = policy,
                ["CS_OFFICE_SOURCE"] = input, ["CS_OFFICE_OUTPUT"] = Path.Combine(writable, output), ["CS_OFFICE_PROGRAM"] = secret,
                ["CS_OFFICE_PROFILE"] = Path.Combine(writable, output + ".child"), ["CS_OFFICE_SOURCE_IDENTITY"] = "unused",
                ["CS_OFFICE_OUTPUT_IDENTITY"] = "unused", ["CS_OFFICE_PROFILE_IDENTITY"] = "unused", ["CS_OFFICE_PROGRAM_IDENTITY"] = "unused" };
        }
        Process StartWorker(Dictionary<string,string> environment)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { WorkingDirectory = stage, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "--office-profile-worker", hostPath, stage, expectedHash }) start.ArgumentList.Add(argument);
            start.Environment.Clear(); foreach (var pair in environment) start.Environment.Add(pair.Key, pair.Value);
            return Process.Start(start) ?? throw new IOException("Ownership worker did not start.");
        }
        async Task<JsonElement> RunWorkerAsync(Dictionary<string,string> environment, string evidence)
        {
            using var worker = StartWorker(environment);
            var stdout = worker.StandardOutput.ReadToEndAsync(); var stderr = worker.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await worker.WaitForExitAsync(timeout.Token); }
            finally { if (!worker.HasExited) worker.Kill(true); await worker.WaitForExitAsync(); }
            var output = await stdout; var errors = await stderr;
            File.WriteAllText(Path.Combine(stage, evidence + ".json"), output); File.WriteAllText(Path.Combine(stage, evidence + ".err"), errors);
            Check(worker.ExitCode == 0, "bounded ownership worker completes: " + evidence);
            using var document = JsonDocument.Parse(output); return document.RootElement.Clone();
        }
    }

    private static string[] Aces(string path, string? excluded)
    {
        var bytes = Directory.Exists(path) ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm()
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
        var acl = new RawSecurityDescriptor(bytes, 0).DiscretionaryAcl ?? throw new IOException("Missing test ACL.");
        return acl.Cast<GenericAce>().Where(ace => excluded is null || ace is not KnownAce known || known.SecurityIdentifier.Value != excluded)
            .Select(ace => { var data = new byte[ace.BinaryLength]; ace.GetBinaryForm(data, 0); return Convert.ToHexString(data); }).ToArray();
    }
    private static bool HasSid(string path, string sid) => Aces(path, null).Length != Aces(path, sid).Length;
    private static bool MappingExists(string mapping) { using var key = Registry.CurrentUser.OpenSubKey(mapping); return key is not null; }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static void RequireOwned(string stage)
    {
        var full = Path.GetFullPath(stage);
        if (full != stage || !full.Contains("\\.codex-temp\\office-isolation\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use an owned absolute Office isolation directory.");
    }
    private static void CreateJunction(string path, string target)
    {
        RequireOwned(path); RequireOwned(target);
        if (Directory.Exists(path)) throw new IOException("Junction fixture must be fresh.");
        Directory.CreateDirectory(path);
        var substitute = System.Text.Encoding.Unicode.GetBytes(@"\??\" + target);
        var display = System.Text.Encoding.Unicode.GetBytes(target);
        var buffer = new byte[16 + substitute.Length + 2 + display.Length + 2];
        BitConverter.GetBytes(0xa0000003U).CopyTo(buffer, 0);
        BitConverter.GetBytes(checked((ushort)(buffer.Length - 8))).CopyTo(buffer, 4);
        BitConverter.GetBytes(checked((ushort)substitute.Length)).CopyTo(buffer, 10);
        BitConverter.GetBytes(checked((ushort)(substitute.Length + 2))).CopyTo(buffer, 12);
        BitConverter.GetBytes(checked((ushort)display.Length)).CopyTo(buffer, 14);
        substitute.CopyTo(buffer, 16); display.CopyTo(buffer, 18 + substitute.Length);
        using var handle = OpenDirectory(path, 0x40000000, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900a4, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string name, string existing, IntPtr security);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle OpenDirectory(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle handle, uint code, byte[] input, int length, IntPtr output, int maximum, out uint returned, IntPtr overlapped);
}
