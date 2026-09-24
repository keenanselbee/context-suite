using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;
using Microsoft.Win32;

internal static class OfficeExcelLayoutContracts
{
    internal static async Task RunAsync(string workerPath, string evidence, string? printFixtures = null)
    {
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new InvalidDataException("Use fresh isolated Excel layout evidence.");
        var originals = Path.Combine(root, "originals"); Directory.CreateDirectory(originals);
        var names = printFixtures is null ? ExcelLayoutFixtures.Create(originals) :
            new[] { "manual-break", "repeat-title", "fit-one-page", "disjoint-areas", "hidden-cells" }
                .Select(name => "Excel print " + name + ".xlsx").ToArray();
        if (printFixtures is not null)
            foreach (var name in names) File.Copy(Path.Combine(printFixtures, name), Path.Combine(originals, name));
        var files = names.Select(name => Path.Combine(originals, name)).ToArray();
        var before = Directory.EnumerateFiles(originals).ToDictionary(path => path,
            path => new { Sha256 = Hash(path), Written = File.GetLastWriteTimeUtc(path) });
        var contexts = Path.Combine(root, "contexts");
        var retained = Path.Combine(root, "retained-journals"); Directory.CreateDirectory(retained);
        var output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        var checks = new List<string>();
        var reviews = new Dictionary<string, string[]>();
        FileResult[] results;
        var prompts = new List<string>();
        var mode = "cached";
        await using var worker = new WorkerClient(workerPath, Path.Combine(root, "workers"));
        await using (var vm = new MainViewModel(worker,
            new SuiteSettings { Convert = new(OutputDirectory: output, ReplaceOriginals: true) },
            new OutputPublisher(Path.Combine(root, "publications"), new NoRecycle()),
            new LocalTrialStore(Path.Combine(root, "trial.json")), contexts))
        {
            vm.OfficeCalculationRequested += token =>
            { token.ThrowIfCancellationRequested(); prompts.Add(mode); return Task.FromResult<string?>(mode); };
            vm.OfficeFontsRequested += (review, token) =>
            {
                token.ThrowIfCancellationRequested();
                reviews.Add(review.SourcePath, [.. review.MissingFontFamilies]);
                return Task.FromResult(true); // Retain outputs for independent diagnosis if a font report differs.
            };
            foreach (var file in files)
            {
                mode = file.EndsWith("recalculate.xlsx", StringComparison.Ordinal) ? "recalculate" : "cached";
                vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [file]));
                var row = vm.Rows.Last();
                row.PropertyChanged += (_, _) =>
                {
                    if (row.Result.State != OperationState.Running || row.Status != "Removing temporary files") return;
                    var record = Directory.EnumerateFiles(contexts, "*.ownership").Single();
                    var copy = Path.Combine(retained, Path.GetFileName(record));
                    if (!File.Exists(copy)) File.Copy(record, copy);
                };
                await vm.WaitForIdleAsync();
            }
            Check(prompts.SequenceEqual(printFixtures is null ? new[] { "cached", "recalculate", "cached", "recalculate" } :
                Enumerable.Repeat("cached", names.Length)), "each workbook retains its selected calculation mode");
            results = vm.Rows.Select(row => row.Result).ToArray();
            File.WriteAllText(Path.Combine(root, "outcomes.json"), JsonSerializer.Serialize(new { Results = results, Reviews = reviews }));
            Check(results.Select(row => row.Path).SequenceEqual(files), "commands retain the selected workbook order");
            foreach (var result in results)
                Check(result.State == OperationState.Succeeded && result.Publication is { IsCommitted: true } publication &&
                    File.Exists(publication.OutputPath) && Path.GetDirectoryName(publication.OutputPath) == output &&
                    result.EngineIdentity!.Contains("qpdf 12.4.1; PDFium 8044"),
                    "validated Excel PDF copy published: " + Path.GetFileName(result.Path) + " - " + result.Message);
        }
        Check(before.All(pair => Hash(pair.Key) == pair.Value.Sha256 && File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Written),
            "all generated originals retain bytes and timestamps");
        Check(!Directory.EnumerateFileSystemEntries(contexts).Any() && worker.ProcessId is null &&
            !Directory.EnumerateFileSystemEntries(Path.Combine(root, "publications")).Any(),
            "application disposal leaves no worker, context or publication journal");
        var profiles = new List<object>();
        foreach (var record in Directory.EnumerateFiles(retained, "*.ownership"))
        {
            using var journal = OfficeOwnershipJournal.Open(record, contexts, worker.OfficeEngineDirectory);
            var work = journal.Owner.Work;
            var sid = journal.Changes.Single(change => change.Step == OfficeOwnershipStep.ProfileCreated).Sid!;
            using var mapping = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + sid);
            Check(mapping is null && !Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName)) &&
                !Directory.Exists(work.DirectoryPath), "Excel profile and context removed: " + work.ItemId);
            profiles.Add(new { work.ProfileName, Sid = sid, Removed = true, Journal = record, ContextRetired = true });
        }
        Check(profiles.Count == names.Length, "every Excel export has native cleanup evidence");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = reviews.Count == 0, Mode = printFixtures is null ? "ExcelLayout" : "ExcelPrint", Checks = checks,
            CalculationChoices = prompts, Results = results, Originals = before, Profiles = profiles, Reviews = reviews
        }, new JsonSerializerOptions { WriteIndented = true }));
        Check(reviews.Count == 0, "authored Arial workbooks need no font review");
        Console.WriteLine($"Passed {checks.Count} Excel layout application checks. Independent PDF inspection remains required.");

        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
            checks.Add(message); Console.WriteLine("PASS: " + message);
        }
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Excel originals must remain untouched.");
    }
}
