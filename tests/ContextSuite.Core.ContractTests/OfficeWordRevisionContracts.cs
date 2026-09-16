using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;
using Microsoft.Win32;

internal static class OfficeWordRevisionContracts
{
    internal static async Task RunAsync(string workerPath, string evidence, bool paragraphMarks = false)
    {
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new InvalidDataException("Use new isolated Office execution evidence.");
        var originals = Path.Combine(root, "originals"); Directory.CreateDirectory(originals);
        var contexts = Path.Combine(root, "contexts");
        var retained = Path.Combine(root, "retained-journals"); Directory.CreateDirectory(retained);
        var output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        var names = (paragraphMarks ? WordRevisionStructureFixtures.Create(originals, true) :
            WordRevisionFixtures.Create(originals).Concat(WordRevisionStructureFixtures.Create(originals)))
            .Select(item => item.Name).ToArray();
        var expectedCount = paragraphMarks ? 6 : 13;
        var files = names.Select(name => Path.Combine(originals, name)).ToArray();
        var before = files.ToDictionary(path => path, path => new { Sha256 = Hash(path), Written = File.GetLastWriteTimeUtc(path) });
        var checks = new List<string>();
        FileResult[] results;
        await using var worker = new WorkerClient(workerPath, Path.Combine(root, "workers"));
        var access = new LocalTrialStore(Path.Combine(root, "trial.json"));
        await using (var vm = new MainViewModel(worker,
            new SuiteSettings { Convert = new(OutputDirectory: output, ReplaceOriginals: true) },
            new OutputPublisher(Path.Combine(root, "publications"), new NoRecycle()), access, contexts))
        {
            vm.OfficeCalculationRequested += _ => throw new InvalidOperationException("Word must not request spreadsheet policy.");
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
            File.WriteAllText(Path.Combine(root, "outcomes.json"), JsonSerializer.Serialize(results));
            Check(results.Length == expectedCount, $"one Word command returns all {expectedCount} revision/control outcomes");
            foreach (var result in results)
            {
                Check(result.State == OperationState.Succeeded && result.Publication is { IsCommitted: true } publication &&
                    File.Exists(publication.OutputPath) && Path.GetDirectoryName(publication.OutputPath) == output &&
                    result.EngineIdentity!.Contains("qpdf 12.4.1; PDFium 8044"),
                    "validated PDF copy published: " + Path.GetFileName(result.Path) + " - " + result.Message);
            }
        }
        Check(before.All(pair => Hash(pair.Key) == pair.Value.Sha256 && File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Written),
            $"all {expectedCount} original documents retain bytes and timestamps");
        Check(!Directory.EnumerateFileSystemEntries(contexts).Any() && worker.ProcessId is null,
            "application disposal leaves no worker or Office context");
        var profiles = new List<object>();
        foreach (var record in Directory.EnumerateFiles(retained, "*.ownership"))
        {
            using var journal = OfficeOwnershipJournal.Open(record, contexts, worker.OfficeEngineDirectory);
            var work = journal.Owner.Work;
            var sid = journal.Changes.Single(change => change.Step == OfficeOwnershipStep.ProfileCreated).Sid!;
            using var mapping = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + sid);
            Check(mapping is null && !Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName)) &&
                !Directory.Exists(work.DirectoryPath), "revision export profile and context removed: " + work.ItemId);
            profiles.Add(new { work.ProfileName, Sid = sid, Removed = true, Journal = record, ContextRetired = true });
        }
        Check(profiles.Count == expectedCount, $"all {expectedCount} native export lifetimes have retained cleanup evidence");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true,
            Mode = paragraphMarks ? "WordParagraphRevisions" : "WordRevisions", Checks = checks, Results = results, Originals = before, Profiles = profiles },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} application Word revision export checks. Independent fidelity inspection is still required.");

        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
            checks.Add(message); Console.WriteLine("PASS: " + message);
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Word revisions must retain originals.");
    }
}
