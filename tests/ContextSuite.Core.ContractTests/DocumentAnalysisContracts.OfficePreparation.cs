using System.ComponentModel;
using System.IO.Compression;
using System.Security.Cryptography;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;

internal static partial class DocumentAnalysisContracts
{
    internal static async Task OfficePreparationFailureContractsAsync(string scratch, Action<bool, string> check)
    {
        var stage = Path.GetFullPath(Path.Combine(scratch, "office-copy-failures-" + Guid.NewGuid().ToString("N")));
        var runtime = Path.Combine(stage, "runtime"); Directory.CreateDirectory(runtime);
        var original = Path.Combine(stage, "original.docx");
        var bytes = Zip([.. OpenXmlParts("docx"), ("padding.txt", new string('a', 512 * 1024))], CompressionLevel.NoCompression);
        File.WriteAllBytes(original, bytes); var written = File.GetLastWriteTimeUtc(original);
        foreach (var scenario in new[] { "before", "partial", "failure", "locked" })
        {
            var root = Path.Combine(stage, scenario); Directory.CreateDirectory(root);
            using var cancellation = new CancellationTokenSource();
            OfficeExportWork? work = null; long copied = 0; FileStream? locked = null;
            OfficeContextPreparation? pending = null;
            try
            {
                using var prepared = await OfficeContextPreparation.CreateAsync(root, runtime, original, "docx", "none", cancellation.Token,
                    copyProgress: (current, count) =>
                    {
                        work = current; copied = count;
                        if (count == 0)
                        {
                            check(OfficePreparationCrashContracts.ReadRecord(Path.Combine(root, current.ItemId.ToString("N") + ".ownership")).Length > 40 &&
                                !File.Exists(current.SourcePath), "Office copy: durable ownership precedes snapshot creation: " + scenario);
                            if (scenario == "before") cancellation.Cancel();
                            if (scenario == "locked") locked = new FileStream(Path.Combine(current.DirectoryPath, "temp", "held.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                        }
                        else if (scenario == "failure") throw new IOException("Authored copy interruption.");
                        else cancellation.Cancel();
                    });
                check(false, "Office interrupted copy must not complete: " + scenario);
            }
            catch (OfficePreparationCleanupException error) when (scenario == "locked")
            {
                pending = error.Preparation;
                check(error.Failure is OperationCanceledException && File.Exists(work!.SourcePath) &&
                    pending.Journal.Changes[^1].Step == OfficeOwnershipStep.RetirementIntent,
                    "Office obstructed copy cleanup retains ownership and durable retirement intent");
                try { using var writer = new FileStream(original, FileMode.Open, FileAccess.Write, FileShare.Read); check(false, "Office pending copy original lease"); }
                catch (IOException) { check(true, "Office pending copy cleanup keeps the original protected"); }
            }
            catch (OperationCanceledException) when (scenario is "before" or "partial")
            { check(true, "Office successful cleanup preserves cancellation: " + scenario); }
            catch (IOException error) when (scenario == "failure")
            { check(error.Message == "Authored copy interruption.", "Office successful cleanup preserves the original copy failure"); }
            finally { locked?.Dispose(); }
            if (pending is not null) pending.RetireUnstarted();
            check(!Directory.EnumerateFileSystemEntries(root).Any() && work is not null &&
                !Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName)),
                "Office interrupted copy removes owned temporary files without creating a native profile: " + scenario);
            check(scenario == "before" ? copied == 0 : copied > 0 && copied < bytes.Length,
                "Office interruption occurs at the declared actual copy boundary: " + scenario);
            using (var exclusive = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.None))
                check(true, "Office copy cleanup releases its original lease: " + scenario);
        }
        var completeRoot = Path.Combine(stage, "unstarted"); Directory.CreateDirectory(completeRoot);
        using (var prepared = await OfficeContextPreparation.CreateAsync(completeRoot, runtime, original, "docx", "none", default))
        {
            prepared.RetireUnstarted();
            check(!Directory.EnumerateFileSystemEntries(completeRoot).Any(), "Office prepared but unstarted conversion can retire without a native profile");
        }
        foreach (var (name, count, records, allowed) in new[] { ("entries-full", 511, false, false), ("entries-room", 510, false, true),
            ("records-full", 256, true, false), ("records-room", 255, true, true) })
        {
            var root = Path.Combine(stage, name); Directory.CreateDirectory(root);
            for (var index = 0; index < count; index++) File.WriteAllText(Path.Combine(root, index + (records ? ".ownership" : ".retained")), "keep");
            try
            {
                using var prepared = await OfficeContextPreparation.CreateAsync(root, runtime, original, "docx", "none", default);
                check(allowed && Directory.EnumerateFileSystemEntries(root).Count() == count + 2, "Office storage admission reserves room for context and journal: " + name);
                prepared.RetireUnstarted();
            }
            catch (IOException error)
            { check(!allowed && (string?)error.Data["OfficeRetainedDirectory"] == root, "Office storage refusal identifies retained work for review: " + name); }
            check(Directory.EnumerateFileSystemEntries(root).Count() == count && Directory.EnumerateFiles(root).All(path => File.ReadAllText(path) == "keep"),
                "Office storage bounds preserve all earlier files: " + name);
        }
        var workerRoot = Path.Combine(stage, "worker"); Directory.CreateDirectory(Path.Combine(workerRoot, "office-engine"));
        var heldRoot = Path.Combine(stage, "executor-held");
        FileStream? obstruction = null;
        await using (var worker = new WorkerClient(Path.Combine(workerRoot, "absent.exe")))
        await using (var executor = new OfficeConversionExecutor(worker, new OutputPublisher(Path.Combine(stage, "held-publications"), null!), null!, heldRoot,
            (work, copied) =>
            {
                if (copied == 0) obstruction = new FileStream(Path.Combine(work.DirectoryPath, "temp", "held.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                else throw new IOException("Authored blocked preparation failure.");
            }))
        {
            var other = Path.Combine(stage, "held-other.docx"); File.WriteAllBytes(other, bytes);
            var confirmed = OfficeConversionPlan.Create(Guid.NewGuid(), new[] { original, other }.Select(path =>
                new OfficeConversionSource(Guid.NewGuid(), path, "docx", "none", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)))), new("convert", new())).Confirm();
            try
            {
                var result = await executor.ExecuteAdmittedAsync(confirmed, new(new(true, "Fixture admission."), confirmed.Plan.BatchId), null, default);
                check(result.Results.All(item => item.State == OperationState.Failed) && executor.PendingRecoveryRecords.Count == 1 &&
                    result.Results[0].Publication?.RecoveryRecordPath == executor.PendingRecoveryRecords.Single() && result.Results[1].Message.Contains("Earlier Office work"),
                    "Office executor retains failed preparation ownership and blocks following work");
                check(worker.ProcessId is null && !Directory.Exists(Path.Combine(stage, "held-publications")),
                    "Office failed preparation starts no native export or output reservation");
                try { using var writer = new FileStream(original, FileMode.Open, FileAccess.Write, FileShare.Read); check(false, "Office executor pending source lease"); }
                catch (IOException) { check(true, "Office executor preserves the original lease after preparation cleanup fails"); }
            }
            finally { obstruction?.Dispose(); }
            await executor.DisposeAsync();
            check(executor.PendingRecoveryRecords.Count == 0 && !Directory.EnumerateFileSystemEntries(heldRoot).Any(),
                "Office executor disposal retries unstarted cleanup without another conversion");
        }
        await using (var worker = new WorkerClient(Path.Combine(workerRoot, "absent.exe")))
        await using (var executor = new OfficeConversionExecutor(worker, new OutputPublisher(Path.Combine(stage, "publications"), null!), null!, Path.Combine(stage, "entries-full")))
        {
            var other = Path.Combine(stage, "other.docx"); File.WriteAllBytes(other, bytes);
            var confirmed = OfficeConversionPlan.Create(Guid.NewGuid(), new[] { original, other }.Select(path =>
                new OfficeConversionSource(Guid.NewGuid(), path, "docx", "none", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)))), new("convert", new())).Confirm();
            var result = await executor.ExecuteAdmittedAsync(confirmed, new(new(true, "Fixture admission."), confirmed.Plan.BatchId), null, default);
            check(result.Results.All(item => item.State == OperationState.Failed) && result.Results[0].Publication?.RecoveryRecordPath == executor.PendingPreparationDirectory &&
                result.Results[1].Message.Contains("Earlier Office work"), "Office full storage blocks remaining batch work with a retained review location");
            check(worker.ProcessId is null && !Directory.Exists(Path.Combine(stage, "publications")), "Office storage refusal starts no worker or output reservation");
        }
        check(File.ReadAllBytes(original).SequenceEqual(bytes) && File.GetLastWriteTimeUtc(original) == written,
            "Office copy cancellation, failure and capacity checks preserve original bytes and timestamp");
        await OfficePreparationCrashContracts.RunAsync(stage, bytes, check);
    }
}
