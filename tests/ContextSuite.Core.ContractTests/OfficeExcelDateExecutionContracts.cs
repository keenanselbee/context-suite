using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Licensing;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using Microsoft.Win32;

internal static class OfficeExcelDateExecutionContracts
{
    internal static async Task RunAsync(string workerPath, string fixtures, string evidence)
    {
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new IOException("Use fresh owned Office execution evidence.");
        Directory.CreateDirectory(root);
        var contexts = Path.Combine(root, "contexts");
        var retained = Path.Combine(root, "retained-journals"); Directory.CreateDirectory(retained);
        var checks = new List<string>(); var results = new List<object>();
        await using var worker = new WorkerClient(workerPath, Path.Combine(root, "workers"));
        await using var executor = new OfficeConversionExecutor(worker,
            new OutputPublisher(Path.Combine(root, "publications"), new NoRecycle()),
            new LocalTrialStore(Path.Combine(root, "trial.json")), contexts);
        foreach (var variant in new[] { "1900-default", "1900-explicit", "1904" })
        foreach (var calculation in new[] { "cached", "recalculate" })
        {
            var path = Path.Combine(fixtures, "Excel dates " + variant + ".xlsx");
            var originalHash = Hash(path); var written = File.GetLastWriteTimeUtc(path);
            var folder = Path.Combine(root, variant + "-" + calculation); Directory.CreateDirectory(folder);
            var source = new OfficeConversionSource(Guid.NewGuid(), path, "xlsx", calculation, new FileInfo(path).Length, originalHash);
            var plan = OfficeConversionPlan.Create(Guid.NewGuid(), [source], new("convert", new(OutputDirectory: folder, ReplaceOriginals: true))).Confirm();
            var observed = false;
            var execution = await executor.ExecuteAsync(plan, row =>
            {
                if (row.State != OperationState.Running || row.Message != "Removing temporary files") return;
                var record = Directory.EnumerateFiles(contexts, "*.ownership").Single();
                var retainedRecord = Path.Combine(retained, Path.GetFileName(record));
                File.Copy(record, retainedRecord);
                using var journal = OfficeOwnershipJournal.Open(retainedRecord, contexts, worker.OfficeEngineDirectory);
                var work = journal.Owner.Work;
                File.Copy(work.CandidatePath, Path.Combine(folder, "candidate.pdf"));
                File.Copy(work.WorkbookPath, Path.Combine(folder, "calculated.xlsx"));
                observed = true;
            }, default);
            var result = execution.Results.Single();
            File.WriteAllText(Path.Combine(folder, "outcome.json"), JsonSerializer.Serialize(result));
            var refuse = variant != "1904" && calculation == "recalculate";
            Check(execution.Admission.IsAllowed && observed, variant + "/" + calculation + ": export reaches native PDF and calculated workbook completion");
            if (refuse)
                Check(result.State == OperationState.Failed && result.Message.Contains("calculated workbook contains dates", StringComparison.Ordinal) &&
                    result.Publication?.IsCommitted != true && Directory.EnumerateFiles(folder, "*.pdf").Count() == 1,
                    variant + "/" + calculation + ": known calculated date error blocks publication with explanation");
            else
                Check(result.State == OperationState.Succeeded && result.Publication is { Outcome: PublicationOutcome.CopyCreated } publication &&
                    File.Exists(publication.OutputPath) && Hash(publication.OutputPath!) == Hash(Path.Combine(folder, "candidate.pdf")),
                    variant + "/" + calculation + ": validated cached/1904 PDF copy published");
            Check(Hash(path) == originalHash && File.GetLastWriteTimeUtc(path) == written, "original bytes and timestamp remain unchanged");
            Check(executor.PendingRecoveryRecords.Count == 0 && !Directory.EnumerateFileSystemEntries(contexts).Any() &&
                !Directory.EnumerateFiles(folder, "*.tmp").Any(), "date outcome leaves no pending context or reservation");
            results.Add(new { Variant = variant, Calculation = calculation, Source = path, SourceSha256 = originalHash, SourceWriteTimeUtc = written,
                CandidateSha256 = Hash(Path.Combine(folder, "candidate.pdf")), WorkbookSha256 = Hash(Path.Combine(folder, "calculated.xlsx")),
                Refused = refuse, Result = result });
            File.WriteAllText(Path.Combine(root, "outcomes.json"), JsonSerializer.Serialize(results));
        }
        var profiles = new List<object>();
        foreach (var path in Directory.EnumerateFiles(retained, "*.ownership"))
        {
            using var journal = OfficeOwnershipJournal.Open(path, contexts, worker.OfficeEngineDirectory);
            var work = journal.Owner.Work;
            var sid = journal.Changes.Single(change => change.Step == OfficeOwnershipStep.ProfileCreated).Sid!;
            using var mapping = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + sid);
            Check(journal.Changes[^1].Step == OfficeOwnershipStep.ProfileDeleted && mapping is null &&
                !Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName)) && !Directory.Exists(work.DirectoryPath),
                "native profile, mapping and calculated-workbook context removed: " + work.ItemId);
            profiles.Add(new { work.ProfileName, Sid = sid, Removed = true, Journal = path, ContextRetired = true });
        }
        Check(profiles.Count == 6, "all six native attempts have cleanup evidence");
        await worker.DisposeAsync();
        Check(worker.ProcessId is null && !Directory.EnumerateFileSystemEntries(Path.Combine(root, "publications")).Any(),
            "final worker and publication journals are cleared");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Mode = "ExcelCalculatedDates",
            Checks = checks, Results = results, Profiles = profiles }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} Excel calculated-date application checks.");

        void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); checks.Add(message); Console.WriteLine("PASS: " + message); }
    }

    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Office PDF must preserve originals.");
    }
}
