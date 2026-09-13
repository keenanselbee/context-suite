using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

internal static class ImagePdfResourceWorkerContracts
{
    public static async Task RunAsync(string root, string executable, string fixtures)
    {
        if (Directory.Exists(root)) throw new IOException("Use a fresh resource workflow directory.");
        Directory.CreateDirectory(root);
        var fixtureReport = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtures, "image-pdf-resources.json")));
        using var reportLease = fixtureReport;
        var expected = fixtureReport.RootElement.GetProperty("originals").EnumerateArray()
            .ToDictionary(x => Path.GetFileName(x.GetProperty("path").GetString()!), x => x.GetProperty("Hash").GetString()!);
        var names = new[] { "maximum-page.bmp", "second-page.bmp", "third-page.bmp", "after-refusal.bmp" };
        var originals = new Dictionary<string, (string Hash, DateTime Written)>();
        foreach (var name in names)
        {
            var source = Path.Combine(fixtures, name);
            if (Hash(source) != expected[name]) throw new Exception("Generated resource fixture changed.");
            var copy = Path.Combine(root, name);
            File.Copy(source, copy);
            originals.Add(copy, (expected[name], File.GetLastWriteTimeUtc(copy)));
        }
        var records = Path.Combine(root, "records");
        var scratch = Path.Combine(root, "workers");
        var access = new LocalTrialStore(Path.Combine(root, "trial.json"));
        var publisher = new OutputPublisher(records, new NoRecycle(), replacementVerified: true);
        await using var worker = new WorkerClient(executable, scratch);
        var sources = new List<ImageSourceFacts>();
        foreach (var name in names) sources.Add(await worker.ProbeAsync(new(Guid.NewGuid(), Path.Combine(root, name)), default));
        if (!worker.HasImagePdfConverter || worker.ProcessId is not int processId)
            throw new Exception("Packaged image-PDF worker did not start.");
        var executor = new ImagePdfExecutor(worker, publisher, access);
        var observations = new List<object>();
        using var owned = Process.GetProcessById(processId);
        var validatorPath = Path.Combine(Path.GetDirectoryName(executable)!, "pdf-validator", "ContextSuite.ImagePdfValidator.exe");
        var children = new Dictionary<int, Process>();
        long workerPeak = 0, nativePeak = 0, combinedSamplePeak = 0;
        using var sampling = new CancellationTokenSource();
        var monitor = ObserveAsync(sampling.Token);
        try
        {
            await Execute("maximum-page", [sources[0]], true);
            await Execute("output-cap", sources.Take(3), false);
            await Execute("after-refusal", [sources[3]], true);
            if (worker.ProcessId != processId || owned.HasExited)
                throw new Exception("Resource refusal unexpectedly replaced the sequential worker.");
        }
        finally
        {
            sampling.Cancel();
            await monitor;
            foreach (var child in children.Values) child.Dispose();
        }
        if (children.Count == 0) throw new Exception("No live owned independent validator was observed.");
        await worker.DisposeAsync();
        if (!owned.HasExited || Directory.Exists(scratch) && Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new Exception("Resource worker or scratch survived disposal.");
        CheckOriginals();
        File.WriteAllText(Path.Combine(root, "image-pdf-resource-worker.json"), JsonSerializer.Serialize(new
        {
            observations, processId, observedValidatorProcesses = children.Count,
            workerPeakWorkingSetBytes = workerPeak, validatorPeakWorkingSetBytes = nativePeak,
            maximumSampledCombinedWorkingSetBytes = combinedSamplePeak,
            limits = "20 ms polling can miss transient child peaks. Worker peak is cumulative; the application host is excluded. Generated opaque BMPs only, not a universal memory ceiling or visible acceptance."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Passed three staged image-PDF resource workflows, source/publication cleanup and worker reuse/exit checks.");

        async Task Execute(string name, IEnumerable<ImageSourceFacts> selection, bool shouldComplete)
        {
            var plan = ImagePdfPlan.Create(Guid.NewGuid(), selection, new("convert", new(ReplaceOriginals: true))).Confirm(true);
            var watch = Stopwatch.StartNew();
            var result = await executor.ExecuteAsync(plan, null, default);
            var publication = result.Result.Publication;
            if (!result.Admission.IsAllowed || shouldComplete && publication?.Outcome != PublicationOutcome.CopyCreated ||
                !shouldComplete && (result.Result.State != OperationState.Failed || publication?.IsCommitted == true ||
                    result.Result.Message != "PDF conversion reached a processing limit. Select fewer or smaller images and try again. All originals were kept."))
                throw new Exception("Unexpected resource workflow outcome: " + name + " " + JsonSerializer.Serialize(result));
            long? outputBytes = null;
            if (shouldComplete)
            {
                var output = publication!.OutputPath!;
                if (!File.Exists(output) || Path.GetFileName(output) != Path.GetFileNameWithoutExtension(plan.Plan.Pages[0].Source.Path) + " - Converted.pdf")
                    throw new Exception("Large resource copy has an unexpected name or is missing.");
                if (name == "maximum-page" && Hash(output) != Hash(Path.Combine(fixtures, "maximum-page.pdf")))
                    throw new Exception("Packaged worker differs from the independently sample-verified writer output.");
                outputBytes = new FileInfo(output).Length;
            }
            else if (File.Exists(Path.Combine(root, "maximum-page - Combined.pdf")))
                throw new Exception("Output-cap refusal published a partial combined document.");
            CheckOriginals();
            if (Directory.Exists(records) && Directory.EnumerateFiles(records).Any() || Directory.GetFiles(root, ".context-suite-*.tmp").Length != 0)
                throw new Exception("Resource workflow retained a journal or output reservation.");
            observations.Add(new { name, milliseconds = watch.Elapsed.TotalMilliseconds, state = result.Result.State.ToString(),
                result.Result.Message, outputBytes, outputPath = publication?.OutputPath });
            Console.WriteLine("PASS: staged resource workflow " + name);
        }

        async Task ObserveAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                owned.Refresh();
                if (owned.HasExited) break;
                workerPeak = Math.Max(workerPeak, owned.PeakWorkingSet64);
                long combined = owned.WorkingSet64;
                var child = PdfFailureContracts.FindChild(processId, validatorPath);
                if (child is not null)
                {
                    if (children.ContainsKey(child.Id)) child.Dispose();
                    else children.Add(child.Id, child);
                }
                foreach (var process in children.Values)
                {
                    try
                    {
                        process.Refresh();
                        if (process.HasExited) continue;
                        nativePeak = Math.Max(nativePeak, process.PeakWorkingSet64);
                        combined += process.WorkingSet64;
                    }
                    catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
                }
                combinedSamplePeak = Math.Max(combinedSamplePeak, combined);
                try { await Task.Delay(20, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        }

        void CheckOriginals()
        {
            foreach (var (path, original) in originals)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                if (Convert.ToHexString(SHA256.HashData(stream)) != original.Hash || File.GetLastWriteTimeUtc(path) != original.Written)
                    throw new Exception("A resource workflow changed or retained a lease on an original.");
            }
        }
    }

    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token)
            => throw new Exception("Combined PDF resource tests must retain all originals.");
    }
}
