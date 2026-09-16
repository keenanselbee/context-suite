using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;
using Microsoft.Win32;

internal static class OfficeWordFontStyleContracts
{
    internal static async Task SourcesAsync(string scratch, Action<bool, string> check)
    {
        var originals = Path.Combine(scratch, "word-font-styles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(originals);
        WordFontStyleFixtures.Create(originals);
        await InspectSourcesAsync(originals, check);
    }

    private static async Task InspectSourcesAsync(string originals, Action<bool, string> check)
    {
        foreach (var item in WordFontStyleFixtures.Cases)
        {
            var path = Path.Combine(originals, WordFontStyleFixtures.Name(item.Key));
            var before = Hash(path); var written = File.GetLastWriteTimeUtc(path);
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var result = await OfficeSourcePreflight.InspectOpenXmlAsync(path, input, default);
            check(result.FormatId == "docx" && result.Refusal is null && result.WordFonts is { Available: true, TextRuns: 1, ResolvedRuns: 1 } &&
                result.WordFonts.Families.SequenceEqual([item.Family]) && result.WordFonts.CoverageIssues.IsEmpty,
                "authored Word style source resolves: " + item.Key);
            check(before == Hash(path) && written == File.GetLastWriteTimeUtc(path), "authored Word style source unchanged: " + item.Key);
            var expectedInactive = item.Key is "deleted-missing" or "old-formatting";
            check(result.WordFonts!.KeepActiveReports([WordFontStyleFixtures.Missing]).IsEmpty == expectedInactive,
                "authored Word revision-only report policy: " + item.Key);
        }
    }

    internal static async Task RunAsync(string workerPath, string evidence)
    {
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new InvalidDataException("Use fresh isolated Word font-style evidence.");
        var originals = Path.Combine(root, "originals"); Directory.CreateDirectory(originals);
        var contexts = Path.Combine(root, "contexts");
        var retained = Path.Combine(root, "retained-journals"); Directory.CreateDirectory(retained);
        var output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        var names = WordFontStyleFixtures.Create(originals).Select(item => item.Name).ToArray();
        var files = names.Select(name => Path.Combine(originals, name)).ToArray();
        var before = files.ToDictionary(path => path, path => new { Sha256 = Hash(path), Written = File.GetLastWriteTimeUtc(path) });
        var checks = new List<string>();
        await InspectSourcesAsync(originals, Check);
        var reviews = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        FileResult[] results;
        await using var worker = new WorkerClient(workerPath, Path.Combine(root, "workers"));
        await using (var vm = new MainViewModel(worker,
            new SuiteSettings { Convert = new(OutputDirectory: output, ReplaceOriginals: true) },
            new OutputPublisher(Path.Combine(root, "publications"), new NoRecycle()),
            new LocalTrialStore(Path.Combine(root, "trial.json")), contexts))
        {
            vm.OfficeCalculationRequested += _ => throw new InvalidOperationException("Word has no calculation choice.");
            vm.OfficeFontsRequested += (review, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (!before.ContainsKey(review.SourcePath) || !reviews.TryAdd(review.SourcePath, [.. review.MissingFontFamilies]))
                    throw new InvalidDataException("Unknown source or repeated font review.");
                return Task.FromResult(true); // Explicit test-only acceptance for these authored documents.
            };
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [.. files]));
            foreach (var row in vm.Rows)
                row.PropertyChanged += (_, _) =>
                {
                    if (row.Result.State != OperationState.Running || row.Status != "Removing temporary files") return;
                    var record = Directory.EnumerateFiles(contexts, "*.ownership").Single();
                    var copy = Path.Combine(retained, Path.GetFileName(record));
                    if (!File.Exists(copy)) File.Copy(record, copy);
                };
            await vm.WaitForIdleAsync();
            results = vm.Rows.Select(row => row.Result).ToArray();
            File.WriteAllText(Path.Combine(root, "outcomes.json"), JsonSerializer.Serialize(new { Results = results, Reviews = reviews }));
            Check(results.Length == names.Length, "one Word command returns every authored font-style result");
            foreach (var result in results)
                Check(result.State == OperationState.Succeeded && result.Publication is { IsCommitted: true } publication &&
                    File.Exists(publication.OutputPath) && Path.GetDirectoryName(publication.OutputPath) == output &&
                    result.EngineIdentity!.Contains("qpdf 12.4.1; PDFium 8044"),
                    "validated style PDF copy published: " + Path.GetFileName(result.Path) + " - " + result.Message);
        }
        Check(before.All(pair => Hash(pair.Key) == pair.Value.Sha256 && File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Written),
            "all authored Word originals retain bytes and timestamps");
        Check(!Directory.EnumerateFileSystemEntries(contexts).Any() && worker.ProcessId is null,
            "application disposal leaves no worker or Word font context");
        var profiles = new List<object>();
        foreach (var record in Directory.EnumerateFiles(retained, "*.ownership"))
        {
            using var journal = OfficeOwnershipJournal.Open(record, contexts, worker.OfficeEngineDirectory);
            var work = journal.Owner.Work;
            var sid = journal.Changes.Single(change => change.Step == OfficeOwnershipStep.ProfileCreated).Sid!;
            using var mapping = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + sid);
            Check(mapping is null && !Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName)) &&
                !Directory.Exists(work.DirectoryPath), "font-style profile and context removed: " + work.ItemId);
            profiles.Add(new { work.ProfileName, Sid = sid, Removed = true, Journal = record, ContextRetired = true });
        }
        Check(profiles.Count == names.Length, "every font-style export has native cleanup evidence");
        var reviewMatches = WordFontStyleFixtures.Cases.All(item =>
        {
            var path = Path.Combine(originals, WordFontStyleFixtures.Name(item.Key));
            return item.Family == WordFontStyleFixtures.Missing
                ? reviews.TryGetValue(path, out var families) && families.SequenceEqual([WordFontStyleFixtures.Missing])
                : !reviews.ContainsKey(path);
        });
        // Retain complete publication/cleanup evidence even if the signal comparison
        // exposes a renderer difference. Independent PDF inspection remains necessary.
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = reviewMatches,
            Mode = "WordFontStyles", Checks = checks, Results = results, Originals = before, Profiles = profiles,
            Reviews = reviews, ReviewMatches = reviewMatches }, new JsonSerializerOptions { WriteIndented = true }));
        Check(reviewMatches, "only the two actively missing font cases request review");
        Console.WriteLine($"Passed {checks.Count} Word font-style application checks. Independent PDF inspection remains required.");

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
            throw new InvalidOperationException("Word font-style originals must remain untouched.");
    }
}
