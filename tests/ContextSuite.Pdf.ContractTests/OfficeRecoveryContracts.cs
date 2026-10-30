using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;
using Microsoft.Win32;

internal static class OfficeRecoveryContracts
{
    internal static async Task RecoverStageAsync(string stage)
    {
        RequireStage(stage);
        using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(stage, "ready.json")));
        var path = receipt.RootElement.GetProperty("Journal").GetString()!;
        if (Path.GetDirectoryName(path) != stage) throw new IOException("Unexpected recovery journal location.");
        using var journal = OfficeOwnershipJournal.Open(path, Path.Combine(stage, "contexts"), Path.Combine(stage, "runtime"));
        var owner = await OfficeSandboxOwner.RecoverAsync(journal);
        await owner.DisposeAsync();
        Console.WriteLine("Recovered recorded disposable Office profile: " + owner.Name);
    }

    internal static async Task HoldAsync(string stage, string mode)
    {
        RequireStage(stage);
        if (mode == "worker") { await Task.Delay(Timeout.Infinite); return; }
        if (mode is not ("created" or "granted" or "engine" or "revoking" or "replaced")) throw new ArgumentException("Unknown recovery fixture.");
        var id = Guid.NewGuid();
        var directory = Path.Combine(stage, "contexts", "office-" + id.ToString("N"));
        var runtime = Path.Combine(stage, "runtime"); Directory.CreateDirectory(runtime);
        foreach (var name in new[] { "input", "output", "profile", "temp" }) Directory.CreateDirectory(Path.Combine(directory, name));
        var work = new OfficeExportWork(id, directory, "ContextSuite.Office.Evaluation." + id.ToString("N"), "docx", "none", 1,
            Convert.ToHexString(SHA256.HashData(new byte[] { 42 })));
        File.WriteAllBytes(work.SourcePath, [42]);
        var path = Path.Combine(stage, id.ToString("N") + ".ownership");
        using var journal = OfficeOwnershipJournal.Create(path, work, runtime);
        var owner = OfficeSandboxOwner.Create(journal);
        using var job = mode == "engine" ? WorkerProcessJob.CreateRecoverable(Guid.NewGuid()) : null;
        Process? worker = null;
        if (mode != "created")
        {
            owner.GrantDirectory(runtime, false);
            owner.GrantDirectory(Path.Combine(directory, "input"), false);
            if (mode != "granted")
                foreach (var name in new[] { "output", "profile", "temp" }) owner.GrantDirectory(Path.Combine(directory, name), true);
        }
        if (job is not null)
        {
            worker = Start(stage, "worker"); job.Assign(worker);
            journal.RecordEngineIntent(job.RecoveryIdentity!);
        }
        if (mode == "revoking")
        {
            journal.Record(new(OfficeOwnershipStep.CleanupIntent));
            journal.Record(new(OfficeOwnershipStep.RevokeIntent, Path: Path.Combine(directory, "temp")));
        }
        // Release the file while keeping the native owner live, so the parent
        // can prove that an unlocked journal is not permission to recover it.
        journal.Dispose();
        File.WriteAllText(Path.Combine(stage, "ready.pending"), JsonSerializer.Serialize(new { Journal = path, owner.Name, owner.Sid, Worker = worker?.Id }));
        File.Move(Path.Combine(stage, "ready.pending"), Path.Combine(stage, "ready.json"));
        await Task.Delay(Timeout.Infinite);
    }

    internal static async Task RunAsync(string parent, Action<bool, string> check)
    {
        var reports = new List<object>();
        foreach (var mode in new[] { "created", "granted", "engine", "revoking", "replaced" })
        {
            var stage = Path.Combine(parent, "recovery", mode); Directory.CreateDirectory(stage);
            using var child = Start(stage, mode);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string? path = null;
            Process? worker = null;
            OfficeOwnershipJournal? journal = null;
            try
            {
                var ready = Path.Combine(stage, "ready.json");
                while (!File.Exists(ready) && !child.HasExited) await Task.Delay(10, deadline.Token);
                check(!child.HasExited && File.Exists(ready), "recovery owner is live at its recorded boundary: " + mode);
                using var receipt = JsonDocument.Parse(File.ReadAllText(ready));
                path = receipt.RootElement.GetProperty("Journal").GetString()!;
                if (receipt.RootElement.GetProperty("Worker").ValueKind == JsonValueKind.Number)
                { worker = Process.GetProcessById(receipt.RootElement.GetProperty("Worker").GetInt32()); _ = worker.Handle; }
                journal = OfficeOwnershipJournal.Open(path, Path.Combine(stage, "contexts"), Path.Combine(stage, "runtime"));
                var before = ReadJournal(path);
                var refused = false;
                try { await OfficeSandboxOwner.RecoverAsync(journal); }
                catch (IOException error) when (error.Message.Contains("still running", StringComparison.Ordinal)) { refused = true; }
                check(refused && ReadJournal(path).SequenceEqual(before), "recovery refuses a live owner without changing its unlocked journal: " + mode);
                child.Kill(); await child.WaitForExitAsync(deadline.Token);
                check(child.HasExited, "recovery confirms original owner death: " + mode);
                if (mode == "replaced")
                {
                    // Substitute between ownership sessions, before recovery
                    // reopens the journal and holds its directory leases.
                    journal.Dispose(); journal = null;
                    var runtime = Path.Combine(stage, "runtime"); var retained = Path.Combine(stage, "runtime-retained");
                    if (Path.GetDirectoryName(runtime) != stage || Path.GetDirectoryName(retained) != stage || Directory.Exists(retained))
                        throw new IOException("Unexpected recovery replacement fixture paths.");
                    // A signaled process can release storage handles shortly
                    // afterward. Retry only sharing/lock errors on this fixture.
                    for (var attempt = 0; ; attempt++)
                    {
                        try { Directory.Move(runtime, retained); break; }
                        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 && attempt < 49)
                        { await Task.Delay(100, deadline.Token); }
                    }
                    try
                    {
                        Directory.CreateDirectory(runtime);
                        journal = OfficeOwnershipJournal.Open(path, Path.Combine(stage, "contexts"), runtime);
                        refused = false;
                        try { await OfficeSandboxOwner.RecoverAsync(journal); }
                        catch (IOException error) when (error.Message.Contains("recorded identity", StringComparison.Ordinal)) { refused = true; }
                        check(refused && ReadJournal(path).SequenceEqual(before) && HasSid(retained, receipt.RootElement.GetProperty("Sid").GetString()!),
                            "recovery refuses a replaced grant object without revoking the retained original");
                    }
                    finally
                    {
                        journal?.Dispose(); journal = null;
                        Directory.Delete(runtime); Directory.Move(retained, runtime);
                    }
                    journal = OfficeOwnershipJournal.Open(path, Path.Combine(stage, "contexts"), runtime);
                }
                var recovered = await OfficeSandboxOwner.RecoverAsync(journal);
                if (mode == "granted")
                {
                    var retained = ReadJournal(path);
                    var retainedSid = receipt.RootElement.GetProperty("Sid").GetString()!;
                    recovered.ReleaseRecoveryLeases();
                    check(ReadJournal(path).SequenceEqual(retained) && HasSid(Path.Combine(stage, "runtime"), retainedSid),
                        "recovery lease release retains durable state and pending permissions for retry");
                    journal.Dispose();
                    journal = OfficeOwnershipJournal.Open(path, Path.Combine(stage, "contexts"), Path.Combine(stage, "runtime"));
                    recovered = await OfficeSandboxOwner.RecoverAsync(journal);
                    check(ReadJournal(path).SequenceEqual(retained), "recovery reconstructs released leases without duplicating cleanup intent");
                }
                check(worker is null || worker.HasExited, "recovery stops the recorded process group before restoring cleanup: " + mode);
                refused = false;
                try { recovered.GrantDirectory(Path.Combine(stage, "runtime"), false); }
                catch (IOException) { refused = true; }
                check(refused, "recovered Office ownership permits cleanup only: " + mode);
                await recovered.DisposeAsync();
                var sid = receipt.RootElement.GetProperty("Sid").GetString()!;
                var name = receipt.RootElement.GetProperty("Name").GetString()!;
                using var mapping = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + sid);
                check(mapping is null && !Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(name)) &&
                    journal.Changes[^1].Step == OfficeOwnershipStep.ProfileDeleted, "recovery removes the exact recorded profile and completes its journal: " + mode);
                check(journal.Changes.Where(change => change.Step == OfficeOwnershipStep.GrantIntent).All(change => !HasSid(change.Path!, sid)),
                    "recovery removes all recorded root grants: " + mode);
                check(File.ReadAllBytes(journal.Owner.Work.SourcePath).SequenceEqual(new byte[] { 42 }), "recovery preserves the source snapshot: " + mode);
                reports.Add(new { Mode = mode, ProfileName = name, Sid = sid, Journal = path, Removed = true });
                File.WriteAllText(Path.Combine(parent, "recovery-results.json"), JsonSerializer.Serialize(reports));
            }
            finally
            {
                journal?.Dispose();
                if (!child.HasExited) child.Kill();
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await child.WaitForExitAsync(cleanup.Token);
                if (worker is not null)
                {
                    if (!worker.HasExited) worker.Kill();
                    await worker.WaitForExitAsync(cleanup.Token); worker.Dispose();
                }
            }
        }
    }

    private static void RequireStage(string stage)
    {
        if (!Path.IsPathFullyQualified(stage) || !stage.Contains("\\.codex-temp\\office-isolation\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use an owned disposable Office recovery stage.");
    }
    private static byte[] ReadJournal(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var bytes = new MemoryStream(); file.CopyTo(bytes); return bytes.ToArray();
    }
    private static Process Start(string stage, string mode)
    {
        RequireStage(stage);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--office-recovery-owner", stage, mode }) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("Recovery owner did not start.");
    }
    private static bool HasSid(string path, string sid) => new DirectoryInfo(path).GetAccessControl().GetAccessRules(true, true,
        typeof(System.Security.Principal.SecurityIdentifier)).Cast<FileSystemAccessRule>().Any(rule => rule.IdentityReference.Value == sid);
}
