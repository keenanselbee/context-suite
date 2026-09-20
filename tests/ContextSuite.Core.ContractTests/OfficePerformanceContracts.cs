using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;
using Microsoft.Win32;

internal static class OfficePerformanceContracts
{
    private sealed record Sample(string Format, int Iteration, double Seconds, int WorkerPid,
        long WorkerPeakWorkingSetBytes, string Output, string Sha256, long Bytes, int CalculationPrompts,
        string ProfileName, string Sid, string Journal, bool ProfileRemoved, string[] Pages);

    internal static async Task RunAsync(string evidence, string executable)
    {
        evidence = Path.GetFullPath(evidence);
        if (!evidence.Contains("\\.codex-temp\\office-performance\\", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(evidence) != "runs" || Directory.Exists(evidence))
            throw new ArgumentException("Use fresh isolated Office performance evidence.");
        Directory.CreateDirectory(evidence);
        var fixtures = Path.Combine(evidence, "originals"); Directory.CreateDirectory(fixtures);
        OfficeFixtures.Create(fixtures);
        RemoveHiddenSlide(Directory.GetFiles(fixtures, "*.pptx").Single());
        var sources = new[] { "docx", "xlsx", "pptx" }.Select(format => Directory.GetFiles(fixtures, "*." + format).Single()).ToArray();
        var originals = sources.ToDictionary(path => path,
            path => new { Sha256 = Hash(path), Written = File.GetLastWriteTimeUtc(path), Bytes = new FileInfo(path).Length });
        PdfTextReader.Initialize(Path.Combine(Path.GetDirectoryName(executable)!, "pdf-renderer", "pdfium.dll"));
        var samples = new List<Sample>();
        for (var iteration = 0; iteration < 5; iteration++)
            foreach (var source in sources)
            {
                samples.Add(await MeasureAsync(source, iteration));
                await File.WriteAllTextAsync(Path.Combine(evidence, "samples.json"),
                    JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{samples[^1].Format} / {iteration + 1}: {samples[^1].Seconds:F3} s");
            }
        var groups = samples.GroupBy(sample => sample.Format).Select(group =>
        {
            var times = group.Select(sample => sample.Seconds).Order().ToArray();
            return new { Format = group.Key, Runs = times.Length, MedianSeconds = times[2],
                MaximumSeconds = times[^1], Passed = times.Length == 5 && times[2] <= 45 && times[^1] <= 75 };
        }).ToArray();
        var preserved = originals.All(pair => Hash(pair.Key) == pair.Value.Sha256 && File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Written);
        var passed = preserved && samples.Count == 15 && groups.Length == 3 && groups.All(group => group.Passed);
        await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, OriginalsPreserved = preserved, MedianLimitSeconds = 45, MaximumLimitSeconds = 75,
            Groups = groups, Samples = samples, Sources = originals,
            Scope = "Two-page DOCX, one-page XLSX (saved values), two-slide PPTX, each five fresh application models/workers. " +
                "Includes recovery scan, preflight, runtime hashing, profile lifecycle, export, independent validation and publication. " +
                "Automatic spreadsheet saved-values answer and retaining the cleanup journal are included. " +
                "Excludes fixture generation, audit hashes, post-export page/text inspection and disposal. " +
                "Managed worker peak only; native job memory, cold storage, human decisions and final expanded-candidate acceptance are unmeasured."
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!passed) throw new InvalidOperationException("Office performance budget missed; retain all recorded samples.");

        async Task<Sample> MeasureAsync(string source, int iteration)
        {
            var format = Path.GetExtension(source)[1..];
            var directory = Path.Combine(evidence, format + "-" + iteration); Directory.CreateDirectory(directory);
            var outputs = Path.Combine(directory, "outputs"); Directory.CreateDirectory(outputs);
            var records = Path.Combine(directory, "publications");
            var contexts = Path.Combine(directory, "contexts");
            var scratch = Path.Combine(directory, "worker");
            var retainedDirectory = Path.Combine(directory, "retained-journals"); Directory.CreateDirectory(retainedDirectory);
            Process? process = null;
            var prompts = 0;
            Sample sample;
            var watch = Stopwatch.StartNew();
            try
            {
                await using (var worker = new WorkerClient(executable, scratch))
                await using (var vm = new MainViewModel(worker,
                    new SuiteSettings { Convert = new(OutputDirectory: outputs), PlayCompletionSound = false },
                    new OutputPublisher(records, new ForbiddenRecycle()), new LocalTrialStore(Path.Combine(directory, "trial.json")), contexts))
                {
                    vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Office commands must not open an image planner.");
                    vm.OfficeFontsRequested += (_, _) => throw new InvalidOperationException("Ordinary fixtures must not require a font substitution decision.");
                    vm.OfficeCalculationRequested += token =>
                    {
                        token.ThrowIfCancellationRequested(); prompts++;
                        if (format != "xlsx") throw new InvalidDataException("Unexpected spreadsheet choice.");
                        return Task.FromResult<string?>("cached");
                    };
                    vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [source]));
                    var row = vm.Rows.Single();
                    row.PropertyChanged += (_, _) =>
                    {
                        if (row.Result.State != OperationState.Running || row.Status != "Removing temporary files") return;
                        var journalPath = Directory.EnumerateFiles(contexts, "*.ownership").Single();
                        var copy = Path.Combine(retainedDirectory, Path.GetFileName(journalPath));
                        if (!File.Exists(copy)) File.Copy(journalPath, copy);
                    };
                    await vm.WaitForIdleAsync();
                    watch.Stop();
                    await File.WriteAllTextAsync(Path.Combine(directory, "timing.json"), JsonSerializer.Serialize(new
                    {
                        Format = format, Iteration = iteration, Seconds = watch.Elapsed.TotalSeconds,
                        WorkerPid = worker.ProcessId, CalculationPrompts = prompts
                    }));
                    await File.WriteAllTextAsync(Path.Combine(directory, "outcome.json"), JsonSerializer.Serialize(row.Result));
                    if (row.Result.State != OperationState.Succeeded || vm.HasProblems || !row.HasOutput ||
                        row.Result.Publication?.Outcome != PublicationOutcome.CopyCreated ||
                        row.Result.EngineIdentity?.Contains("qpdf 12.4.1; PDFium 8044", StringComparison.Ordinal) != true ||
                        prompts != (format == "xlsx" ? 1 : 0))
                        throw new InvalidDataException("Office performance did not publish a validated copy: " + row.Status);
                    process = Process.GetProcessById(worker.ProcessId ?? throw new IOException("Worker did not start."));
                    process.Refresh(); var peak = process.PeakWorkingSet64;
                    var output = Directory.GetFiles(outputs).Single();
                    if (Path.GetFileName(output) != OutputNames.Create(source, "convert", "pdf"))
                        throw new InvalidDataException("Unexpected Office output name.");
                    var probe = await worker.ProbePdfPagesAsync(new(Guid.NewGuid(), output), default);
                    var width = format == "pptx" ? 720 : 612; var height = format == "pptx" ? 405 : 792;
                    if (probe.Document.Pages.Length != (format == "xlsx" ? 1 : 2) || probe.Document.Pages.Any(page =>
                        Math.Abs(page.WidthPoints - width) > 0.1 || Math.Abs(page.HeightPoints - height) > 0.1))
                        throw new InvalidDataException("Office output page geometry changed.");
                    var pages = PdfTextReader.Read(output);
                    await File.WriteAllTextAsync(Path.Combine(directory, "text.json"), JsonSerializer.Serialize(pages));
                    VerifyText(format, pages);
                    var retained = Directory.GetFiles(retainedDirectory, "*.ownership").Single();
                    using var journal = OfficeOwnershipJournal.Open(retained, contexts, worker.OfficeEngineDirectory);
                    var work = journal.Owner.Work;
                    var sid = journal.Changes.Single(change => change.Step == OfficeOwnershipStep.ProfileCreated).Sid!;
                    using var mapping = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + sid);
                    if (mapping is not null || Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName)) ||
                        Directory.Exists(work.DirectoryPath) || !journal.Changes.Any(change => change.Step == OfficeOwnershipStep.ProfileDeleted))
                        throw new IOException("Office profile or registry mapping remains.");
                    sample = new(format, iteration, watch.Elapsed.TotalSeconds, process.Id, peak,
                        output, Hash(output), new FileInfo(output).Length, prompts, work.ProfileName, sid, retained, true, pages);
                }
                if (!process.HasExited || Directory.EnumerateFileSystemEntries(records).Any() ||
                    Directory.EnumerateFileSystemEntries(contexts).Any() || Directory.EnumerateDirectories(scratch).Any() ||
                    Directory.EnumerateFiles(directory, ".context-suite-*.tmp", SearchOption.AllDirectories).Any() ||
                    originals.Any(pair => Hash(pair.Key) != pair.Value.Sha256 || File.GetLastWriteTimeUtc(pair.Key) != pair.Value.Written))
                    throw new IOException("Office benchmark changed a source or left worker/profile/publication scratch.");
                return sample;
            }
            finally { process?.Dispose(); }
        }
    }

    private static void VerifyText(string format, string[] pages)
    {
        var passed = format switch
        {
            "docx" => pages.Length == 2 && pages[0].Contains("page one") && pages[0].Contains("caf\u00e9 \u00fc") &&
                pages[0].Contains("Value 42") && pages[1].Contains("page two") && pages[0].Contains("FIRST PAGE HEADER") &&
                pages[1].Contains("RUNNING HEADER") && pages[0].Contains("Page 1 of 2") && pages[1].Contains("Page 2 of 2"),
            "xlsx" => pages.Length == 1 && pages[0].Contains("Excel caf\u00e9") && pages[0].Contains("Formula result") &&
                pages[0].Contains('5') && !pages[0].Contains("HIDDEN") && !pages[0].Contains("OUTSIDE"),
            "pptx" => pages.Length == 2 && pages[0].Contains("slide 1 caf\u00e9") && pages[1].Contains("slide 3 caf\u00e9") &&
                pages.All(page => !page.Contains("HIDDEN")),
            _ => false
        };
        if (!passed) throw new InvalidDataException("Office PDF lost expected authored text or page order.");
    }

    private static void RemoveHiddenSlide(string path)
    {
        // Modify only the newly authored benchmark package, before fingerprinting.
        // The existing two visible pages retain their original slide 1/3 labels.
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        foreach (var (part, attribute, value) in new[]
        {
            ("ppt/presentation.xml", "id", "257"),
            ("ppt/_rels/presentation.xml.rels", "Id", "rId2"),
            ("[Content_Types].xml", "PartName", "/ppt/slides/slide2.xml")
        })
        {
            var entry = zip.GetEntry(part)!;
            XDocument xml; using (var input = entry.Open()) xml = XDocument.Load(input);
            xml.Descendants().Single(element => (string?)element.Attribute(attribute) == value).Remove();
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry(part).Open(), new UTF8Encoding(false));
            xml.Save(writer);
        }
        zip.GetEntry("ppt/slides/slide2.xml")!.Delete();
        zip.GetEntry("ppt/slides/_rels/slide2.xml.rels")!.Delete();
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input));
    }

    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Office benchmark must preserve originals.");
    }
}
