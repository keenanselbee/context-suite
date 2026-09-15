using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;

namespace ContextSuite.Core.ContractTests;

internal static class OfficeJournalContracts
{
    internal static async Task HoldAsync(string stage, Guid id, string mode)
    {
        if (!stage.Contains("\\.codex-temp\\", StringComparison.OrdinalIgnoreCase) || mode is not ("complete" or "torn"))
            throw new ArgumentException("Use an owned journal crash fixture.");
        var work = Work(stage, id);
        using var journal = OfficeOwnershipJournal.Create(Path.Combine(stage, id.ToString("N") + ".ownership"), work, Path.Combine(stage, "runtime"));
        if (mode == "torn")
        {
            journal.Dispose();
            using var tail = new FileStream(Path.Combine(stage, id.ToString("N") + ".ownership"), FileMode.Append, FileAccess.Write, FileShare.Read);
            tail.Write([32, 0, 0, 0, 123]); tail.Flush(true);
            File.WriteAllText(Path.Combine(stage, "ready"), "torn");
            await Task.Delay(Timeout.Infinite);
        }
        File.WriteAllText(Path.Combine(stage, "ready"), "complete");
        await Task.Delay(Timeout.Infinite);
    }

    internal static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var stage = Path.GetFullPath(Path.Combine(scratch, "office-journal-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(stage);
        var runtime = Path.Combine(stage, "runtime"); Directory.CreateDirectory(runtime);
        var contextRoot = Path.Combine(stage, "contexts"); Directory.CreateDirectory(contextRoot);
        var id = Guid.NewGuid(); var work = Work(stage, id);
        var path = Path.Combine(stage, id.ToString("N") + ".ownership");
        byte[] first;
        OfficeOwnershipIdentity identity;
        using (var journal = OfficeOwnershipJournal.Create(path, work, runtime))
        {
            identity = journal.Owner; first = Read(path);
            check(journal.Changes.Count == 1 && journal.Changes[0].Step == OfficeOwnershipStep.ProfileIntent,
                "Office journal persists creation intent before profile work");
            Refuses(() => OfficeOwnershipJournal.Open(path, contextRoot, runtime).Dispose(), "Office journal permits only one owner");
            var moved = stage + "-renamed";
            check(Path.GetDirectoryName(moved) == Path.GetDirectoryName(stage) &&
                stage.StartsWith(Path.GetFullPath(scratch) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "Office journal rename fixture remains within its owned scratch directory");
            Refuses(() => Directory.Move(stage, moved), "Office journal lease prevents parent replacement");
            Reject(new(OfficeOwnershipStep.DeleteIntent), "Office journal rejects deletion without confirmed creation and cleanup");
            Reject(new(OfficeOwnershipStep.ProfileCreated, "S-1-15-2-1"), "Office journal rejects a profile SID from another identity");
            journal.Record(new(OfficeOwnershipStep.ProfileCreated, OfficeOwnershipJournal.ProfileSid(work.ProfileName)));
            string[] paths = [runtime, Path.Combine(work.DirectoryPath, "input"), Path.Combine(work.DirectoryPath, "output"),
                Path.Combine(work.DirectoryPath, "profile"), Path.Combine(work.DirectoryPath, "temp")];
            for (var index = 0; index < paths.Length; index++)
            {
                Reject(new(OfficeOwnershipStep.GrantIntent, Path: stage, DirectoryIdentity: new string('A', 48), Writable: index >= 2),
                    "Office journal refuses an unrelated grant path " + index);
                journal.Record(new(OfficeOwnershipStep.GrantIntent, Path: paths[index], DirectoryIdentity: new string('A', 48), Writable: index >= 2));
                Reject(new(OfficeOwnershipStep.EngineIntent), "Office journal does not run before all grants are confirmed " + index);
                journal.Record(new(OfficeOwnershipStep.GrantApplied, Path: paths[index]));
            }
            journal.Record(new(OfficeOwnershipStep.EngineIntent));
            Reject(new(OfficeOwnershipStep.CleanupIntent), "Office journal refuses cleanup until process shutdown is recorded");
            journal.Record(new(OfficeOwnershipStep.ProcessesStopped)); journal.Record(new(OfficeOwnershipStep.CleanupIntent));
            Reject(new(OfficeOwnershipStep.DeleteIntent), "Office journal retains the profile until every grant is revoked");
            for (var index = paths.Length - 1; index >= 0; index--)
            {
                journal.Record(new(OfficeOwnershipStep.RevokeIntent, Path: paths[index]));
                journal.Record(new(OfficeOwnershipStep.GrantRevoked, Path: paths[index]));
            }
            journal.Record(new(OfficeOwnershipStep.DeleteIntent)); journal.Record(new(OfficeOwnershipStep.ProfileDeleted));
            Reject(new(OfficeOwnershipStep.ProfileIntent), "Office journal cannot reuse a completed profile identity");

            void Reject(OfficeOwnershipChange change, string message)
            {
                var before = Read(path); Refuses(() => journal.Record(change), message);
                check(Read(path).SequenceEqual(before), message + " without changing the persisted record");
            }
        }
        using (var reopened = OfficeOwnershipJournal.Open(path, contextRoot, runtime))
            check(reopened.Owner == identity && reopened.Changes.Count == 27 && reopened.Changes[^1].Step == OfficeOwnershipStep.ProfileDeleted,
                "Office journal reopens all 27 completed ownership transitions with the original owner identity");
        var complete = File.ReadAllBytes(path);
        Refuses(() => OfficeOwnershipJournal.Create(path, work, runtime).Dispose(), "Office journal creation cannot overwrite an existing record");
        Refuses(() => OfficeOwnershipJournal.Open(path, stage, runtime).Dispose(), "Office journal rejects an unrelated expected context root");
        Refuses(() => OfficeOwnershipJournal.Open(path, contextRoot, stage).Dispose(), "Office journal rejects an unrelated expected runtime");
        Refuses(() => OfficeOwnershipJournal.Create(Path.Combine(runtime, id.ToString("N") + ".ownership"), work, runtime + "\\").Dispose(),
            "Office journal refuses a trailing-separator runtime that could hide an overlapping journal path");
        check(File.ReadAllBytes(path).SequenceEqual(complete), "Office journal identity refusals preserve all stored bytes");
        var aliases = Path.Combine(stage, "alias"); Directory.CreateDirectory(aliases);
        var alias = Path.Combine(aliases, id.ToString("N") + ".ownership");
        check(CreateHardLink(alias, path, IntPtr.Zero), "Office journal creates a disposable hard-link refusal fixture");
        try
        {
            Refuses(() => OfficeOwnershipJournal.Open(alias, contextRoot, runtime).Dispose(), "Office journal refuses a hard-linked record");
            check(File.ReadAllBytes(path).SequenceEqual(complete), "Office journal alias refusal preserves the original record");
        }
        finally { File.Delete(alias); }

        var pendingId = Guid.NewGuid(); var pendingWork = Work(stage, pendingId);
        var pendingPath = Path.Combine(stage, pendingId.ToString("N") + ".ownership");
        using (var pending = OfficeOwnershipJournal.Create(pendingPath, pendingWork, runtime))
        {
            pending.Record(new(OfficeOwnershipStep.ProfileCreated, OfficeOwnershipJournal.ProfileSid(pendingWork.ProfileName)));
            pending.Record(new(OfficeOwnershipStep.GrantIntent, Path: runtime, DirectoryIdentity: new string('B', 48), Writable: false));
        }
        using (var pending = OfficeOwnershipJournal.Open(pendingPath, contextRoot, runtime))
        {
            check(pending.Changes[^1].Step == OfficeOwnershipStep.GrantIntent, "Office journal retains an uncertain grant across reopening");
            pending.Record(new(OfficeOwnershipStep.CleanupIntent));
            Refuses(() => pending.Record(new(OfficeOwnershipStep.DeleteIntent)), "Office journal requires revocation even when grant completion is uncertain");
            pending.Record(new(OfficeOwnershipStep.RevokeIntent, Path: runtime));
            pending.Record(new(OfficeOwnershipStep.GrantRevoked, Path: runtime));
            pending.Record(new(OfficeOwnershipStep.DeleteIntent)); pending.Record(new(OfficeOwnershipStep.ProfileDeleted));
            check(pending.Changes.Count == 8, "Office journal records cleanup of an uncertain grant without inventing confirmed application");
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(first);
        var json = Encoding.UTF8.GetString(first, 4, length);
        foreach (var (label, payload) in new[]
        {
            ("duplicate fields", json.Replace("\"Version\":1", "\"Version\":1,\"Version\":1", StringComparison.Ordinal)),
            ("unknown fields", json.Replace("\"Version\":1", "\"Unknown\":1,\"Version\":1", StringComparison.Ordinal)),
            ("unsupported version", json.Replace("\"Version\":1", "\"Version\":2", StringComparison.Ordinal)),
            ("missing required field", json.Replace("\"Version\":1,", "", StringComparison.Ordinal)),
            ("wrong sequence", json.Replace("\"Sequence\":0", "\"Sequence\":1", StringComparison.Ordinal)),
            ("numeric action", json.Replace("\"ProfileIntent\"", "0", StringComparison.Ordinal)),
            ("unknown action", json.Replace("\"ProfileIntent\"", "\"Erase\"", StringComparison.Ordinal))
        })
        {
            check(payload != json, "Office malformed journal fixture changes " + label);
            RefuseBytes(Frame(Encoding.UTF8.GetBytes(payload)), label);
        }
        RefuseBytes([], "empty file");
        RefuseBytes(first[..^1], "truncated checksum");
        RefuseBytes(first.Concat(new byte[] { 10, 0, 0, 0, 123 }).ToArray(), "interrupted trailing record");
        RefuseBytes(first.Concat(first).ToArray(), "replayed first record");
        var damaged = first.ToArray(); damaged[^1] ^= 1; RefuseBytes(damaged, "changed checksum");
        RefuseBytes([255, 255, 255, 127], "unbounded frame length");

        foreach (var mode in new[] { "complete", "torn" })
        {
            var childStage = Path.Combine(stage, mode); Directory.CreateDirectory(childStage);
            Directory.CreateDirectory(Path.Combine(childStage, "runtime")); Directory.CreateDirectory(Path.Combine(childStage, "contexts"));
            var childId = Guid.NewGuid(); var childPath = Path.Combine(childStage, childId.ToString("N") + ".ownership");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "--office-journal-hold", childStage, childId.ToString("N"), mode }) start.ArgumentList.Add(argument);
            using var child = Process.Start(start) ?? throw new IOException("Journal crash child did not start.");
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                while (!File.Exists(Path.Combine(childStage, "ready")) && !child.HasExited) await Task.Delay(10, deadline.Token);
                check(!child.HasExited && File.Exists(Path.Combine(childStage, "ready")), "Office journal writer is live before interruption: " + mode);
                var started = child.StartTime.ToUniversalTime().Ticks;
                _ = child.Handle; child.Kill(); await child.WaitForExitAsync(deadline.Token);
                check(child.HasExited, "Office journal writer death is confirmed: " + mode);
                byte[] before;
                var retry = 0;
                while (true)
                {
                    try { before = File.ReadAllBytes(childPath); break; }
                    catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 && retry++ < 99)
                    { await Task.Delay(20, deadline.Token); }
                }
                if (mode == "complete")
                {
                    using var reopened = OfficeOwnershipJournal.Open(childPath, Path.Combine(childStage, "contexts"), Path.Combine(childStage, "runtime"));
                    check(reopened.Owner.OwnerProcessId == child.Id && reopened.Owner.OwnerStartUtcTicks == started && reopened.Changes.Count == 1 &&
                        reopened.Changes[0].Step == OfficeOwnershipStep.ProfileIntent,
                        "Office journal preserves unconfirmed creation intent after owner loss without promoting it to ownership");
                }
                else Refuses(() => OfficeOwnershipJournal.Open(childPath, Path.Combine(childStage, "contexts"), Path.Combine(childStage, "runtime")).Dispose(),
                    "Office journal refuses a torn record after writer loss");
                check(File.ReadAllBytes(childPath).SequenceEqual(before), "Office journal replay preserves crash evidence: " + mode);
            }
            finally
            {
                if (!child.HasExited) child.Kill(); await child.WaitForExitAsync();
                await File.WriteAllTextAsync(Path.Combine(childStage, "stdout.log"), await stdout);
                await File.WriteAllTextAsync(Path.Combine(childStage, "stderr.log"), await stderr);
            }
        }

        void RefuseBytes(byte[] bytes, string label)
        {
            var folder = Path.Combine(stage, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var invalid = Path.Combine(folder, id.ToString("N") + ".ownership"); File.WriteAllBytes(invalid, bytes);
            Refuses(() => OfficeOwnershipJournal.Open(invalid, contextRoot, runtime).Dispose(), "Office journal rejects " + label);
            check(File.ReadAllBytes(invalid).SequenceEqual(bytes), "Office journal retains rejected evidence: " + label);
        }
        void Refuses(Action action, string message)
        {
            var refused = false;
            try { action(); } catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or
                UnauthorizedAccessException or System.ComponentModel.Win32Exception) { refused = true; }
            check(refused, message);
        }
    }

    private static OfficeExportWork Work(string stage, Guid id) => new(id, Path.Combine(stage, "contexts", "office-" + id.ToString("N")),
        "ContextSuite.Office.Evaluation." + id.ToString("N"), "docx", "none", 1, new string('A', 64));
    private static byte[] Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
    }
    private static byte[] Frame(byte[] payload)
    {
        var bytes = new byte[4 + payload.Length + 32]; BinaryPrimitives.WriteInt32LittleEndian(bytes, payload.Length);
        payload.CopyTo(bytes, 4);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(new byte[32]); hash.AppendData(bytes, 0, 4 + payload.Length); hash.GetHashAndReset().CopyTo(bytes, 4 + payload.Length);
        return bytes;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string name, string existing, IntPtr security);
}
