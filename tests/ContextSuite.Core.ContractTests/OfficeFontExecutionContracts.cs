using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using Microsoft.Win32;

internal static class OfficeFontExecutionContracts
{
    internal static async Task RunAsync(string workerPath, string fixtures, string evidence)
    {
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new InvalidDataException("Use a new repository Office font evidence directory.");
        var originals = Path.Combine(root, "originals"); Directory.CreateDirectory(originals);
        var contexts = Path.Combine(root, "contexts");
        var retained = Path.Combine(root, "retained-journals"); Directory.CreateDirectory(retained);
        var reviewed = Path.Combine(root, "review-journals"); Directory.CreateDirectory(reviewed);
        var sources = new Dictionary<string, OfficeConversionSource>();
        var before = new Dictionary<string, (string Hash, DateTime Written)>();
        foreach (var (family, format) in new[] { ("Word", "docx"), ("Excel", "xlsx"), ("PowerPoint", "pptx") })
            foreach (var variant in new[] { "control", "missing" })
            {
                var name = family + " font " + variant + "." + format;
                var copy = Path.Combine(originals, name); File.Copy(Path.Combine(fixtures, name), copy);
                before.Add(copy, (Hash(copy), File.GetLastWriteTimeUtc(copy)));
                sources.Add(format + "-" + variant, new(Guid.NewGuid(), copy, format, format == "xlsx" ? "cached" : "none", new FileInfo(copy).Length, Hash(copy)));
            }
        var checks = new List<string>(); var reports = new List<object>(); var profiles = new List<object>();
        await using var worker = new WorkerClient(workerPath, Path.Combine(root, "workers"));
        var access = new LocalTrialStore(Path.Combine(root, "trial.json"));
        var prompts = 0; string scenario = ""; string currentOutput = ""; var corrupt = false;
        using var cancellation = new CancellationTokenSource();
        Func<OfficeFontReview, CancellationToken, Task<bool>> reviewFonts = (review, token) =>
            {
                prompts++;
                Check(review.MissingFontFamilies.SequenceEqual(new[] { "ContextSuiteAbsentFont9361" }) && before.ContainsKey(review.SourcePath),
                    scenario + ": real native callback reaches the application as the exact missing family and original path");
                var record = Directory.EnumerateFiles(contexts, "*.ownership").Single();
                // The live journal is exclusively owned. Inspect a read-only
                // snapshot rather than competing with its write lease.
                var reviewRecord = Path.Combine(reviewed, Path.GetFileName(record)); File.Copy(record, reviewRecord);
                using (var journal = OfficeOwnershipJournal.Open(reviewRecord, contexts, worker.OfficeEngineDirectory))
                {
                    Check(journal.Changes[^1].Step == OfficeOwnershipStep.ProfileDeleted && journal.Changes.Any(change => change.Step == OfficeOwnershipStep.ProcessesStopped),
                        scenario + ": native process and profile cleanup precede review");
                    Check(!Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(journal.Owner.Work.ProfileName)),
                        scenario + ": removed profile cannot access the waiting candidate");
                }
                Check(!File.Exists(Path.Combine(currentOutput, OutputNames.Create(review.SourcePath, "convert", "pdf"))) &&
                    Directory.EnumerateFiles(currentOutput, "*.tmp").Any(),
                    scenario + ": independently validated temporary PDF is not yet published");
                if (scenario == "cancel") { cancellation.Cancel(); return Task.FromResult(true); }
                if (scenario == "review-error") throw new InvalidOperationException("Simulated unavailable review UI.");
                return Task.FromResult(scenario is not ("skip-continue" or "viewmodel") || Path.GetExtension(review.SourcePath) != ".docx");
            };
        await using var executor = new OfficeConversionExecutor(worker, new OutputPublisher(Path.Combine(root, "records"), new NoRecycle()),
            access, contexts, reviewFonts: reviewFonts);
        await Run("quiet", [sources["docx-control"], sources["xlsx-control"], sources["pptx-control"]], executor, [OperationState.Succeeded, OperationState.Succeeded, OperationState.Succeeded]);
        Check(prompts == 0, "three control documents publish quietly without a font prompt");
        await Run("skip-continue", [sources["docx-missing"], sources["xlsx-missing"], sources["pptx-missing"]], executor,
            [OperationState.Cancelled, OperationState.Succeeded, OperationState.Succeeded]);
        Check(prompts == 3, "Skip affects only its document; later missing-font files each require their own decision");
        await Run("accept-retry", [sources["docx-missing"]], executor, [OperationState.Succeeded]);
        Check(prompts == 4, "retrying a skipped document requests a fresh per-document choice");
        await using (var absentReview = new OfficeConversionExecutor(worker, new OutputPublisher(Path.Combine(root, "no-review-records"), new NoRecycle()), access, contexts))
            await Run("no-handler", [sources["docx-missing"]], absentReview, [OperationState.Cancelled]);
        Check(prompts == 4, "missing review handler never grants consent");
        await Run("cancel", [sources["docx-missing"], sources["pptx-missing"]], executor, [OperationState.Cancelled, OperationState.Cancelled], cancellation.Token);
        Check(prompts == 5, "batch cancellation wins over a simultaneous affirmative review response and stops the next file");
        await Run("review-error", [sources["docx-missing"]], executor, [OperationState.Failed]);
        corrupt = true;
        await Run("validation-failure", [sources["docx-missing"]], executor, [OperationState.Failed]);
        Check(prompts == 6, "invalid PDF is refused before review and cannot be accepted");
        corrupt = false; scenario = "viewmodel"; currentOutput = Path.Combine(root, scenario); Directory.CreateDirectory(currentOutput);
        await using (var vm = new MainViewModel(worker, new ContextSuite.Core.Settings.SuiteSettings { Convert = new(OutputDirectory: currentOutput) },
            new OutputPublisher(Path.Combine(root, "viewmodel-records"), new NoRecycle()), access, contexts))
        {
            vm.OfficeCalculationRequested += _ => Task.FromResult<string?>("cached");
            vm.OfficeFontsRequested += reviewFonts;
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [sources["docx-missing"].Path, sources["xlsx-missing"].Path]));
            foreach (var row in vm.Rows)
                row.PropertyChanged += (_, _) => Capture(row.Result);
            await vm.WaitForIdleAsync();
            Check(prompts == 8 && vm.Rows.Select(row => row.Result.State).SequenceEqual(new[] { OperationState.Cancelled, OperationState.Succeeded }),
                "direct view-model event delivers each review; Skip leaves the following spreadsheet conversion usable");
            Check(vm.Rows[1].OfficeCalculation == "cached" && vm.Rows[1].HasOutput && !vm.Rows[0].HasOutput &&
                Directory.EnumerateFiles(currentOutput, "*.pdf").Count() == 1,
                "direct font review preserves the target/calculation choice and publishes only the accepted PDF");
            reports.Add(new { Scenario = scenario, Results = vm.Rows.Select(row => row.Result).ToArray() });
        }
        Check(before.All(pair => Hash(pair.Key) == pair.Value.Hash && File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Written),
            "every original retains its bytes and modification time across all review outcomes");
        Check(!Directory.EnumerateFileSystemEntries(contexts).Any() && executor.PendingRecoveryRecords.Count == 0,
            "all review paths retire their contexts and ownership records");
        foreach (var record in Directory.EnumerateFiles(retained, "*.ownership"))
        {
            using var journal = OfficeOwnershipJournal.Open(record, contexts, worker.OfficeEngineDirectory);
            var work = journal.Owner.Work;
            var sid = journal.Changes.Single(change => change.Step == OfficeOwnershipStep.ProfileCreated).Sid!;
            using var mapping = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + sid);
            Check(mapping is null && !Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName)) && !Directory.Exists(work.DirectoryPath),
                work.Format + ": profile mapping, profile folder and context are absent");
            profiles.Add(new { work.ProfileName, Sid = sid, Removed = true, Journal = record, ContextRetired = true });
        }
        Check(profiles.Count == 13, "thirteen started exports have complete native cleanup evidence");
        await worker.DisposeAsync();
        Check(worker.ProcessId is null && !Directory.EnumerateFiles(Path.Combine(root, "workers"), "*", SearchOption.AllDirectories).Any(),
            "worker and validation scratch files are released");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks, Reports = reports, Profiles = profiles },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} Office font execution checks.");

        async Task Run(string name, OfficeConversionSource[] selected, OfficeConversionExecutor current, OperationState[] expected, CancellationToken token = default)
        {
            scenario = name; currentOutput = Path.Combine(root, name); Directory.CreateDirectory(currentOutput);
            var plan = OfficeConversionPlan.Create(Guid.NewGuid(), selected, new("convert", new(OutputDirectory: currentOutput, ReplaceOriginals: true))).Confirm();
            var result = await current.ExecuteAsync(plan, Capture, token);
            reports.Add(new { Scenario = name, result.Results });
            Check(result.Results.Select(item => item.State).SequenceEqual(expected), name + ": expected per-file outcomes");
            Check(Directory.EnumerateFiles(currentOutput, "*.pdf").Count() == expected.Count(state => state == OperationState.Succeeded) &&
                !Directory.EnumerateFiles(currentOutput, "*.tmp").Any(), name + ": only approved or quiet validated copies remain; no temporary output remains");
            Check(result.Results.Where(item => item.State == OperationState.Succeeded).All(item => item.Publication is { Outcome: PublicationOutcome.CopyCreated } &&
                item.EngineIdentity?.Contains("qpdf 12.4.1; PDFium 8044", StringComparison.Ordinal) == true), name + ": published copies retain independent validation identity");
            Check(!Directory.EnumerateFileSystemEntries(contexts).Any() && current.PendingRecoveryRecords.Count == 0, name + ": context cleanup completed");
        }
        void Capture(FileResult progress)
        {
            Console.WriteLine(scenario + ": " + progress.State + ": " + progress.Message);
            if (corrupt && progress.State == OperationState.Running && progress.Message == "Validating PDF")
            {
                var context = Directory.EnumerateDirectories(contexts, "office-*").Single();
                File.WriteAllText(Path.Combine(context, "output", "candidate.pdf"), "invalid candidate");
            }
            if (progress.State != OperationState.Running || progress.Message != "Removing temporary files") return;
            var record = Directory.EnumerateFiles(contexts, "*.ownership").Single();
            var copy = Path.Combine(retained, Path.GetFileName(record));
            if (!File.Exists(copy)) File.Copy(record, copy);
        }
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); checks.Add(message); Console.WriteLine("PASS: " + message); }
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Office PDF copies must never recycle an original.");
    }
}
