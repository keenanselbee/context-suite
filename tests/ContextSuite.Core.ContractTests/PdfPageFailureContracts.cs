using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.ContractTests;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

internal static class PdfPageFailureContracts
{
    public static async Task RunAsync(string root, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use a new PDF page interruption directory.");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "authored repeated rectangles.pdf");
        WritePdf(path);
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var modified = File.GetLastWriteTimeUtc(path);
        var records = Path.Combine(root, "records");
        var workerRoot = Path.Combine(root, "workers");
        var publisher = new OutputPublisher(records, new NoRecycle());
        var clock = new ImageInterruptionContracts.DeadlineClock();
        await using var worker = new WorkerClient(executable, workerRoot, clock);
        var access = new LocalTrialStore(Path.Combine(root, "trial.json"));
        var executor = new PdfPageConversionExecutor(worker, publisher, access);
        var baselineSource = await worker.ProbePdfPagesAsync(new(Guid.NewGuid(), path), default);
        var baseline = await executor.ExecuteAsync(Plan(baselineSource), null, default);
        check(baseline.Pages.Single().Result.Publication?.Outcome == PublicationOutcome.CopyCreated,
            "PDF page interruption: authored native-work fixture first completes validated rendering");
        var evidence = new List<object>();
        foreach (var fault in new[] { "cancel", "client-timeout", "worker-crash" })
        {
            var source = await worker.ProbePdfPagesAsync(new(Guid.NewGuid(), path), default);
            var outputId = Guid.NewGuid();
            var reservation = await publisher.ReserveAsync(new(outputId, path, "png", new("convert", new()), PageNumber: 1));
            using var cancel = new CancellationTokenSource();
            var operation = worker.RenderPdfPageAsync(new(source, 0, outputId, reservation.TemporaryPath), cancel.Token);
            var workerId = worker.ProcessId!.Value;
            Process? native = null;
            try
            {
                var watch = Stopwatch.StartNew();
                while (!operation.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(15))
                {
                    native = PdfFailureContracts.FindChild(workerId,
                        Path.Combine(Path.GetDirectoryName(executable)!, "pdf-renderer", "ContextSuite.PdfRenderer.exe"));
                    if (native is not null && !native.HasExited) break;
                    native?.Dispose(); native = null;
                    await Task.Delay(2);
                }
                if (native is null || native.HasExited || operation.IsCompleted)
                    throw new InvalidOperationException("Did not observe the live owned PDF renderer: " + fault);
                var nativeId = native.Id;
                check(IsLocked(reservation.TemporaryPath), "PDF page interruption: live renderer and reserved-output lock observed before " + fault);
                if (fault == "cancel") cancel.Cancel();
                else if (fault == "client-timeout")
                {
                    check(clock.Current!.Due == TimeSpan.FromSeconds(120), "PDF page interruption: reviewed client deadline is 120 seconds");
                    clock.Current.Expire();
                }
                else
                {
                    using var owned = Process.GetProcessById(workerId);
                    owned.Kill(); // Worker only; the native job must terminate its renderer.
                }
                try { await operation.WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Interrupted rendering succeeded."); }
                catch (OperationCanceledException) when (fault == "cancel")
                { check(true, "PDF page interruption: native-phase cancellation remains cancellation"); }
                catch (MediaWorkerException error) when (fault != "cancel")
                {
                    check(error.Failure == (fault == "client-timeout" ? ImageFailure.TimedOut : ImageFailure.WorkerTerminated),
                        "PDF page interruption: typed failure for " + fault);
                }
                await native.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                check(native.HasExited && worker.ProcessId is null, "PDF page interruption: native child and worker exit after " + fault);
                var result = await publisher.AbandonAsync(reservation, fault == "cancel");
                check(result.Outcome == (fault == "cancel" ? PublicationOutcome.Cancelled : PublicationOutcome.Failed) &&
                    !File.Exists(reservation.TemporaryPath) && !File.Exists(reservation.Record.OutputPath) &&
                    !Directory.EnumerateDirectories(workerRoot).Any() && !Directory.EnumerateFiles(records).Any(),
                    "PDF page interruption: owned reservation, journal and worker scratch are cleaned after " + fault);
                check(SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(hash) && File.GetLastWriteTimeUtc(path) == modified,
                    "PDF page interruption: original bytes and timestamp survive " + fault);
                var retrySource = await worker.ProbePdfPagesAsync(new(Guid.NewGuid(), Path.Combine(fixtures, "authored original \u00fc.pdf")), default);
                var resumed = await executor.ExecuteAsync(Plan(retrySource), null, default);
                check(worker.ProcessId != workerId && resumed.Pages.Count == 2 &&
                    resumed.Pages.All(page => page.Result.Publication?.Outcome == PublicationOutcome.CopyCreated),
                    "PDF page interruption: fresh worker publishes both validated page copies after " + fault);
                evidence.Add(new { fault, workerId, nativeId, result, resumed });
            }
            finally
            {
                cancel.Cancel();
                try { await operation; } catch (Exception) { }
                if (!reservation.Finished) await publisher.AbandonAsync(reservation, true);
                native?.Dispose();
            }
        }
        await File.WriteAllTextAsync(Path.Combine(root, "pdf-page-interruptions.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        ConfirmedPdfPageConversion Plan(PdfRasterSource source) =>
            PdfPageConversionPlan.Create(Guid.NewGuid(), [source], new("convert", new(OutputDirectory: root))).Confirm();
    }

    private static bool IsLocked(string path)
    {
        try { using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return false; }
        catch (IOException error) when ((error.HResult & 0xffff) == 32) { return true; }
    }

    private static void WritePdf(string path)
    {
        using var file = File.Create(path);
        var offsets = new List<long> { 0 };
        Write("%PDF-1.4\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 720 720] /Resources << >> /Contents 4 0 R >>");
        var content = string.Concat(Enumerable.Repeat("0.2 0.4 0.6 rg 10 10 500 500 re f\n", 2000));
        Object(4, $"<< /Length {content.Length} >>\nstream\n{content}endstream");
        var xref = file.Position;
        Write("xref\n0 5\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        void Write(string text) { file.Write(Encoding.ASCII.GetBytes(text)); }
        void Object(int id, string body) { offsets.Add(file.Position); Write($"{id} 0 obj\n{body}\nendobj\n"); }
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("PDF page interruption tests never recycle.");
    }
}
