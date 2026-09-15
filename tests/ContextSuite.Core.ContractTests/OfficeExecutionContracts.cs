using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using Microsoft.Win32;

internal static class OfficeExecutionContracts
{
    internal static async Task RunCleanupAsync(string workerPath, string fixtures, string evidence)
    {
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new InvalidDataException("Use a new repository cleanup evidence directory.");
        var originals = Path.Combine(root, "originals"); Directory.CreateDirectory(originals);
        var contexts = Path.Combine(root, "contexts");
        var output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        var sources = new List<OfficeConversionSource>();
        foreach (var format in new[] { "docx", "xlsx" })
        {
            var fixture = Directory.EnumerateFiles(fixtures, "*." + format).Single();
            var path = Path.Combine(originals, Path.GetFileName(fixture)); File.Copy(fixture, path);
            sources.Add(new(Guid.NewGuid(), path, format, format == "xlsx" ? "cached" : "none", new FileInfo(path).Length, Hash(path)));
        }
        var checks = new List<string>();
        await using var worker = new WorkerClient(workerPath, Path.Combine(root, "workers"));
        await using var executor = new OfficeConversionExecutor(worker, new OutputPublisher(Path.Combine(root, "publications"), new NoRecycle()),
            new LocalTrialStore(Path.Combine(root, "trial.json")), contexts);
        var confirmed = OfficeConversionPlan.Create(Guid.NewGuid(), sources, new("convert", new(OutputDirectory: output))).Confirm();
        FileStream? obstruction = null;
        string? context = null;
        OfficeConversionExecution execution;
        try
        {
            execution = await executor.ExecuteAsync(confirmed, result =>
            {
                if (result.State != OperationState.Running || result.Message != "Creating PDF") return;
                context = Directory.EnumerateDirectories(contexts, "office-*").Single();
                // A caller-authored cache file makes the context non-fresh. The
                // worker refuses it before Office launch; its exclusive handle
                // then obstructs grant cleanup until the test releases it.
                obstruction = new FileStream(Path.Combine(context, "temp", "locked-cache.txt"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            }, default);
            Check(execution.Results.All(result => result.State == OperationState.Failed) && executor.PendingRecoveryRecords.Count == 1,
                "cleanup failure retains one owner and declines the remaining document");
            Check(execution.Results[0].Publication?.RecoveryRecordPath == executor.PendingRecoveryRecords.Single(),
                "cleanup failure exposes its retained ownership record");
            Check(Directory.EnumerateDirectories(contexts, "office-*").Count() == 1 && !Directory.EnumerateFiles(output).Any() &&
                context is not null && !File.Exists(Path.Combine(context, "output", "candidate.pdf")),
                "failed fresh-context admission starts no Office export or PDF publication");
            var held = false;
            try { using var attempt = new FileStream(sources[0].Path, FileMode.Open, FileAccess.Write, FileShare.Read); }
            catch (IOException) { held = true; }
            Check(held, "pending owner retains the original read lease while cleanup is uncertain");
        }
        finally { obstruction?.Dispose(); }
        var record = executor.PendingRecoveryRecords.Single();
        var retained = Path.Combine(root, "retained-journals"); Directory.CreateDirectory(retained);
        var evidenceRecord = Path.Combine(retained, Path.GetFileName(record)); File.Copy(record, evidenceRecord);
        string profileName, sid;
        using (var journal = OfficeOwnershipJournal.Open(evidenceRecord, contexts, worker.OfficeEngineDirectory))
        {
            profileName = journal.Owner.Work.ProfileName;
            sid = journal.Changes.Single(change => change.Step == OfficeOwnershipStep.ProfileCreated).Sid!;
        }
        await executor.DisposeAsync();
        Check(executor.PendingRecoveryRecords.Count == 0, "same executor completes cleanup after the obstruction is released");
        Check(!File.Exists(record) && !Directory.EnumerateFileSystemEntries(contexts).Any(),
            "cleanup retry retires the completed profile journal and all generated context files");
        using var mapping = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + sid);
        Check(mapping is null && !Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", profileName)),
            "retried profile folder and Windows mapping are absent");
        Check(sources.All(source => Hash(source.Path) == source.Sha256), "cleanup failure and retry preserve all originals");
        using (new FileStream(sources[0].Path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        Check(true, "completed cleanup releases the original file lease");
        await worker.DisposeAsync();
        Check(worker.ProcessId is null, "cleanup retry leaves no worker running");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks,
            Reports = new[] { execution }, Profiles = new[] { new { ProfileName = profileName, Sid = sid, Removed = true, Journal = evidenceRecord, ContextRetired = true } } },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} Office pending-cleanup checks.");
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); checks.Add(message); Console.WriteLine("PASS: " + message); }
    }

    internal static async Task RunAsync(string workerPath, string fixtures, string evidence)
    {
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new InvalidDataException("Use a new repository Office execution evidence directory.");
        var originals = Path.Combine(root, "originals"); Directory.CreateDirectory(originals);
        var contexts = Path.Combine(root, "contexts");
        var retained = Path.Combine(root, "retained-journals"); Directory.CreateDirectory(retained);
        var records = Path.Combine(root, "publications");
        var sources = new List<OfficeConversionSource>();
        var before = new Dictionary<string, (string Hash, DateTime Written)>();
        foreach (var format in new[] { "docx", "xlsx", "pptx" })
        {
            var fixture = Directory.EnumerateFiles(fixtures, "*." + format).Single();
            var copy = Path.Combine(originals, Path.GetFileName(fixture)); File.Copy(fixture, copy);
            before.Add(copy, (Hash(copy), File.GetLastWriteTimeUtc(copy)));
            sources.Add(new(Guid.NewGuid(), copy, format, format == "xlsx" ? "cached" : "none", new FileInfo(copy).Length, Hash(copy)));
        }
        var checks = new List<string>(); var results = new List<object>();
        await using var worker = new WorkerClient(workerPath, Path.Combine(root, "workers"));
        var publisher = new OutputPublisher(records, new NoRecycle());
        IOperationAccess access = new LocalTrialStore(Path.Combine(root, "trial.json"));
        await using var executor = new OfficeConversionExecutor(worker, publisher, access, contexts);
        var plan = OfficeConversionPlan.Create(Guid.NewGuid(), sources, new("convert", new(ReplaceOriginals: true))).Confirm();
        var collision = Path.Combine(originals, OutputNames.Create(sources[0].Path, "convert", "pdf"));
        File.WriteAllText(collision, "existing unrelated output");
        var first = await executor.ExecuteAsync(plan, Capture, default);
        Check(first.Admission.IsAllowed && first.Results.Count == 3, "one admitted batch returns all three Office outcomes");
        foreach (var (result, source) in first.Results.Zip(sources))
        {
            Check(result.State == OperationState.Succeeded && result.Publication is { Outcome: PublicationOutcome.CopyCreated }, source.Format + ": validated PDF copy published");
            Check(result.Path == source.Path && result.Publication!.OutputPath is { } output && File.Exists(output) && new FileInfo(output).Length > 0,
                source.Format + ": result names its actual output");
            Check(result.EngineIdentity?.Contains("qpdf 12.4.1; PDFium 8044", StringComparison.Ordinal) == true, source.Format + ": publication retains validation engine identity");
        }
        Check(File.ReadAllText(collision) == "existing unrelated output" && Path.GetFileName(first.Results[0].Publication!.OutputPath) ==
            OutputNames.Create(sources[0].Path, "convert", "pdf", 2), "collision chooses a numbered PDF without altering the existing file");
        Check(executor.PendingRecoveryRecords.Count == 0, "normal batch has no pending native cleanup");
        results.Add(first);
        await File.WriteAllTextAsync(Path.Combine(root, "progress.json"), JsonSerializer.Serialize(results));

        using var cancellation = new CancellationTokenSource();
        var failedOutput = Path.Combine(root, "failed-output"); Directory.CreateDirectory(failedOutput);
        var failedPublisher = new OutputPublisher(Path.Combine(root, "failed-records"), new NoRecycle(), io: new FailPublication());
        await using (var failedExecutor = new OfficeConversionExecutor(worker, failedPublisher, access, contexts))
        {
            var failurePlan = OfficeConversionPlan.Create(Guid.NewGuid(), [sources[0]], new("convert", new(OutputDirectory: failedOutput))).Confirm();
            var failed = await failedExecutor.ExecuteAsync(failurePlan, Capture, default);
            Check(failed.Results.Single().State == OperationState.Failed && !Directory.EnumerateFiles(failedOutput, "*.pdf").Any(),
                "publication failure cannot report a PDF copy");
            Check(failedExecutor.PendingRecoveryRecords.Count == 0, "publication failure occurs after verified native cleanup");
            Check(!Directory.EnumerateFiles(failedOutput, "*.tmp").Any(), "pre-publication failure releases its reservation");
            results.Add(failed);
        }
        var cancelledOutput = Path.Combine(root, "cancelled-output"); Directory.CreateDirectory(cancelledOutput);
        var cancelledPublisher = new OutputPublisher(Path.Combine(root, "cancelled-records"), new NoRecycle(), io: new CancelPublication(cancellation));
        await using (var cancelledExecutor = new OfficeConversionExecutor(worker, cancelledPublisher, access, contexts))
        {
            var cancelPlan = OfficeConversionPlan.Create(Guid.NewGuid(), [sources[0], sources[1]], new("convert", new(OutputDirectory: cancelledOutput))).Confirm();
            var cancelled = await cancelledExecutor.ExecuteAsync(cancelPlan, Capture, cancellation.Token);
            Check(cancelled.Results.Count == 2 && cancelled.Results.All(result => result.State == OperationState.Cancelled),
                "cancellation before publication cancels current and remaining documents");
            Check(!Directory.EnumerateFiles(cancelledOutput).Any() && cancelledExecutor.PendingRecoveryRecords.Count == 0,
                "cancellation leaves no PDF/reservation or pending profile cleanup");
            results.Add(cancelled);
        }
        Check(before.All(pair => Hash(pair.Key) == pair.Value.Hash && File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Written),
            "all original bytes and modification times remain unchanged");
        var warningOutput = Path.Combine(root, "warning-output"); Directory.CreateDirectory(warningOutput);
        await using (var warningExecutor = new OfficeConversionExecutor(worker, publisher, access, contexts))
        {
            FileStream? obstruction = null;
            try
            {
                var warningPlan = OfficeConversionPlan.Create(Guid.NewGuid(), [sources[0]], new("convert", new(OutputDirectory: warningOutput))).Confirm();
                var warning = await warningExecutor.ExecuteAsync(warningPlan, result =>
                {
                    Capture(result);
                    if (result.State != OperationState.Running || result.Message != "Removing temporary files") return;
                    var context = Directory.EnumerateDirectories(contexts, "office-*").Single();
                    obstruction = new FileStream(Path.Combine(context, "temp", "locked-cache.txt"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                }, default);
                Check(warning.Results.Single() is { State: OperationState.Succeeded, Publication: { IsCommitted: true, CleanupWarning: true } } &&
                    File.Exists(warning.Results.Single().Publication!.OutputPath), "temporary cleanup failure preserves the published PDF and reports a warning");
                Check(warningExecutor.PendingRecoveryRecords.Count == 1 && File.Exists(warningExecutor.PendingRecoveryRecords.Single()),
                    "post-publication cleanup warning retains its journal for retry");
                results.Add(warning);
            }
            finally { obstruction?.Dispose(); }
            await warningExecutor.DisposeAsync();
            Check(warningExecutor.PendingRecoveryRecords.Count == 0 && !Directory.EnumerateFileSystemEntries(contexts).Any(),
                "post-publication cleanup retries without publishing another PDF");
            Check(Directory.EnumerateFiles(warningOutput, "*.pdf").Count() == 1 && before.All(pair => Hash(pair.Key) == pair.Value.Hash &&
                File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Written), "cleanup retry preserves the one output and all original bytes/times");
        }
        var journals = Directory.EnumerateFiles(retained, "*.ownership").ToArray();
        Check(journals.Length == 6, "only admitted started documents create six isolated contexts");
        Check(!Directory.EnumerateFileSystemEntries(contexts).Any(), "all six completed contexts and journals are retired");
        var cleanup = new List<object>();
        foreach (var path in journals)
        {
            using var journal = OfficeOwnershipJournal.Open(path, contexts, worker.OfficeEngineDirectory);
            var work = journal.Owner.Work;
            var sid = journal.Changes.Single(change => change.Step == OfficeOwnershipStep.ProfileCreated).Sid!;
            Check(journal.Changes[^1].Step == OfficeOwnershipStep.ProfileDeleted && journal.Changes.Any(change => change.Step == OfficeOwnershipStep.ProcessesStopped),
                work.Format + ": durable journal records worker stop before completed profile cleanup");
            using var mapping = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + sid);
            Check(mapping is null && !Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", work.ProfileName)),
                work.Format + ": Windows profile folder and mapping are absent");
            Check(!Directory.Exists(work.DirectoryPath) && !File.Exists(work.SourcePath) && !File.Exists(work.CandidatePath),
                work.Format + ": generated snapshot and candidate are removed after completion");
            cleanup.Add(new { work.ProfileName, Sid = sid, Removed = true, Journal = path, work.CandidatePath, ContextRetired = true });
        }
        await worker.DisposeAsync();
        Check(worker.ProcessId is null && !Directory.EnumerateFiles(Path.Combine(root, "workers"), "*", SearchOption.AllDirectories).Any(),
            "final worker exit releases all worker scratch files");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks, Reports = results, Profiles = cleanup },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} Office execution checks.");

        void Capture(FileResult result)
        {
            Console.WriteLine(result.State + ": " + Path.GetFileName(result.Path) + ": " + result.Message);
            if (result.State == OperationState.Running && result.Message == "Removing temporary files")
            {
                var record = Directory.EnumerateFiles(contexts, "*.ownership").Single();
                File.Copy(record, Path.Combine(retained, Path.GetFileName(record)));
            }
        }

        void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); checks.Add(message); Console.WriteLine("PASS: " + message); }
    }

    private static string Hash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Office copy conversion must never recycle an original.");
    }
    private sealed class FailPublication : PublicationIo
    {
        public override void Checkpoint(PublicationStage stage)
        { if (stage == PublicationStage.Validated) throw new IOException("Simulated publication failure after validation."); }
    }
    private sealed class CancelPublication(CancellationTokenSource cancellation) : PublicationIo
    {
        public override void Checkpoint(PublicationStage stage)
        { if (stage == PublicationStage.Validated) cancellation.Cancel(); }
    }
}
