using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

internal static class ImagePdfPrecisionWorkerContracts
{
    public static async Task RunAsync(string fixtures, string executable)
    {
        using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixtures, "image-pdf-precision-resources.json")));
        var cases = report.RootElement.GetProperty("observations").EnumerateArray().ToArray();
        if (cases.Length != 9) throw new Exception("Use the complete nine-case precision resource matrix.");
        var originals = report.RootElement.GetProperty("originals").EnumerateArray()
            .ToDictionary(item => Path.GetFileName(item.GetProperty("path").GetString()!), item => item.GetProperty("Hash").GetString()!);
        var root = Path.Combine(fixtures, "worker");
        if (Directory.Exists(root)) throw new IOException("Use a fresh precision worker evidence directory.");
        Directory.CreateDirectory(root);
        var copies = new Dictionary<string, (string Hash, DateTime Written)>();
        foreach (var item in cases)
        {
            var name = item.GetProperty("name").GetString()! + ".png";
            var source = Path.Combine(fixtures, name);
            if (Hash(source) != originals[name]) throw new Exception("Authored precision source changed before worker testing.");
            var copy = Path.Combine(root, name);
            File.Copy(source, copy);
            copies.Add(copy, (originals[name], File.GetLastWriteTimeUtc(copy)));
        }
        var records = Path.Combine(root, "records");
        var scratch = Path.Combine(root, "workers");
        var publisher = new OutputPublisher(records, new NoRecycle(), replacementVerified: true);
        var access = new LocalTrialStore(Path.Combine(root, "trial.json"));
        await using var worker = new WorkerClient(executable, scratch);
        var executor = new ImagePdfExecutor(worker, publisher, access);
        int? processId = null;
        var observations = new List<object>();
        foreach (var item in cases)
        {
            var name = item.GetProperty("name").GetString()!;
            var path = Path.Combine(root, name + ".png");
            var watch = Stopwatch.StartNew();
            var source = await worker.ProbeAsync(new(Guid.NewGuid(), path), default);
            processId ??= worker.ProcessId;
            if (worker.ProcessId != processId || processId is null || !worker.HasImagePdfConverter)
                throw new Exception("The precision matrix did not retain its sequential PDF worker.");
            var confirmed = ImagePdfPlan.Create(Guid.NewGuid(), [source], new("convert", new(ReplaceOriginals: true))).Confirm(false);
            var result = await executor.ExecuteAsync(confirmed, null, default);
            var publication = result.Result.Publication;
            if (!result.Admission.IsAllowed || result.Result.State != OperationState.Succeeded ||
                publication?.Outcome != PublicationOutcome.CopyCreated || publication.OutputPath != Path.Combine(root, name + " - Converted.pdf") ||
                Hash(publication.OutputPath) != item.GetProperty("pdfSha256").GetString())
                throw new Exception("Staged worker output differs from the independently sample-validated PDF: " + name);
            CheckOriginals();
            if (Directory.Exists(records) && Directory.EnumerateFileSystemEntries(records).Any() ||
                Directory.GetFiles(root, ".context-suite-*.tmp").Length != 0)
                throw new Exception("Precision publication retained a journal or output reservation.");
            using var process = Process.GetProcessById(processId.Value);
            observations.Add(new { name, milliseconds = watch.Elapsed.TotalMilliseconds, publication.OutputPath,
                pdfSha256 = Hash(publication.OutputPath), cumulativeWorkerPeakWorkingSetBytes = process.PeakWorkingSet64 });
            Console.WriteLine("PASS: staged precision copy " + name);
        }
        using var owned = Process.GetProcessById(processId!.Value);
        await worker.DisposeAsync();
        if (!owned.HasExited || Directory.Exists(scratch) && Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new Exception("Precision worker or scratch survived disposal.");
        CheckOriginals();
        File.WriteAllText(Path.Combine(root, "precision-worker.json"), JsonSerializer.Serialize(new
        {
            observations, processId,
            limits = "Generated PNG matrix, named copies and exact validated PDF identity. Cumulative worker peaks exclude validator children and app host. Not rendered appearance, native-allocation-failure or visible acceptance."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Passed nine staged precision workflows, original/journal preservation and sequential worker reuse/exit.");

        void CheckOriginals()
        {
            foreach (var (path, identity) in copies)
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                if (Convert.ToHexString(SHA256.HashData(input)) != identity.Hash || File.GetLastWriteTimeUtc(path) != identity.Written)
                    throw new Exception("A precision workflow changed or retained a lease on an original.");
            }
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token)
            => throw new Exception("Image-PDF precision tests must retain originals even with overwrite selected.");
    }
}
