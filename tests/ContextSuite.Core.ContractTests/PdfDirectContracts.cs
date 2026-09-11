using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Operations;
using ContextSuite.Core.ContractTests;

internal static class PdfDirectContracts
{
    public static async Task RunAsync(string scratch, string executable, string pdfFixtures, string audioFixtures, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        var pdf = Path.Combine(scratch, "Document ü.pdf");
        File.Copy(Path.Combine(pdfFixtures, "authored original ü.pdf"), pdf);
        var png = Path.Combine(scratch, "Image.png");
        ImageWorkerContracts.WritePng(png, compression: CompressionLevel.NoCompression);
        var flac = Path.Combine(scratch, "Audio.flac");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(audioFixtures, "source.flac"));
        var padding = new byte[4 + 131072]; padding[0] = 1; padding[1] = 2;
        await File.WriteAllBytesAsync(flac, bytes[..42].Concat(padding).Concat(bytes[42..]).ToArray());
        var paths = new[] { pdf, flac, png };
        var hashes = paths.Select(path => SHA256.HashData(File.ReadAllBytes(path))).ToArray();
        var clock = new Clock();
        var access = new Access(new LocalTrialStore(Path.Combine(scratch, "trial", "trial.json"), clock));
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new ForbiddenRecycle());
        var worker = new WorkerClient(executable, Path.Combine(scratch, "workers"));
        await using (var vm = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access))
        {
            var starts = 0; var finishes = 0; var quiet = new QuietWorkflow();
            vm.QuickBatchStarted += (request, rows) =>
            {
                starts++; quiet.Begin(request.RequestId, false, DateTimeOffset.UtcNow);
                foreach (var row in rows) row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FileRow.Result) && row.Path == png && row.Result.State == OperationState.Succeeded)
                        clock.Now = clock.Now.AddDays(4);
                };
            };
            vm.QuickBatchCompleted += (request, rows) => { finishes++; quiet.Complete(request.RequestId, rows.Select(row => row.Result).ToArray()); };
            vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Optimize must not open a planner.");
            check(vm.Admit(new(Guid.NewGuid(), "optimize", "auto", [.. paths])).Accepted, "PDF direct: mixed three-family selection is admitted");
            await vm.WaitForIdleAsync();
            check(access.Admissions == 1 && vm.Rows.All(row => row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated),
                "PDF direct: one admission covers PNG, FLAC and PDF across trial expiry");
            check(vm.Rows.Select(row => Path.GetExtension(row.OutputPath)).SequenceEqual([".pdf", ".flac", ".png"]),
                "PDF direct: combined rows retain selection order and original formats");
            check(starts == 1 && finishes == 1 && !quiet.NeedsAttention && !vm.HasProblems && !vm.ShowDetails && vm.Summary.Contains("saved"),
                "PDF direct: mixed success has one quiet completion and aggregate savings");
            var workerId = worker.ProcessId;
            vm.Admit(new(Guid.NewGuid(), "optimize", "lossless", [pdf])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Failed && access.Admissions == 2 && worker.ProcessId == workerId,
                "PDF direct: subsequent PDF-only batch is blocked by expired trial");
        }
        var switched = new Access();
        var retryFolder = Path.Combine(scratch, "retry-output"); Directory.CreateDirectory(retryFolder);
        await using (var vm = new MainViewModel(new WorkerClient(executable, Path.Combine(scratch, "retry-workers")),
            new() { PlayCompletionSound = false }, publisher, switched))
        {
            vm.Admit(new(Guid.NewGuid(), "optimize", "lossless", [pdf])); await vm.WaitForIdleAsync();
            check(vm.Rows.Single().Status.Contains("Activate") && vm.CanRetry && vm.HasProblems,
                "PDF direct: denied access gives activation guidance and retry");
            switched.Allowed = true;
            vm.Settings = new() { Optimize = new(ReplaceOriginals: true, OutputDirectory: retryFolder), PlayCompletionSound = false };
            vm.RetryFailed(); vm.RetryFailed();
            vm.Settings = new() { Optimize = new(ReplaceOriginals: true), PlayCompletionSound = false };
            await vm.WaitForIdleAsync();
            check(vm.Rows.Count == 2 && vm.DisplayRows.Count() == 1 && vm.Rows[^1].Action == "lossless" &&
                Path.GetDirectoryName(vm.Rows[^1].OutputPath) == retryFolder && !vm.CanRetry && !vm.HasProblems,
                "PDF direct: activation retry captures settings, keeps action and suppresses duplicate clicks");
            vm.Admit(new(Guid.NewGuid(), "optimize", "auto", [pdf])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.Publication?.Outcome == PublicationOutcome.CopyCreated && vm.Rows[^1].OutputPath != pdf,
                "PDF direct: source-folder overwrite preference still produces a PDF copy");
            var optimized = vm.Rows[^1].OutputPath;
            vm.Admit(new(Guid.NewGuid(), "optimize", "auto", [optimized])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Unchanged && !vm.HasProblems,
                "PDF direct: no smaller result remains a quiet successful outcome");
            switched.Allowed = false;
            vm.Admit(new(Guid.NewGuid(), "optimize", "auto", [pdf])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Failed, "PDF direct: deactivation blocks new PDF work");
            switched.Allowed = true; vm.RetryFailed(); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Action == "auto" && vm.Rows[^1].Result.State == OperationState.Succeeded && !vm.HasProblems,
                "PDF direct: reactivation retries the original Auto action");
        }
        var eligible = new Access { Allowed = true };
        await using (var vm = new MainViewModel(new WorkerClient(executable, Path.Combine(scratch, "other-workers")), new(), publisher, eligible))
        {
            var disguised = Path.Combine(scratch, "Document.png"); File.Copy(pdf, disguised);
            vm.Admit(new(Guid.NewGuid(), "optimize", "auto", [disguised])); await vm.WaitForIdleAsync();
            check(vm.Rows.Single().Result.State == OperationState.Unsupported && vm.Rows[0].Status.Contains("Rename it to .pdf") && eligible.Admissions == 0,
                "PDF direct: misleading extension has content-based guidance before admission");
            foreach (var action in new[] { "balanced", "smallest" })
            {
                vm.Admit(new(Guid.NewGuid(), "optimize", action, [pdf])); await vm.WaitForIdleAsync();
                check(vm.Rows[^1].Status.Contains("Auto or Lossless") && vm.Rows[^1].Result.State == OperationState.Unsupported && eligible.Admissions == 0,
                    "PDF direct: " + action + " explains PNG-only policy without admission");
            }
            vm.Admit(new(Guid.NewGuid(), "optimize", "auto", [Path.Combine(pdfFixtures, "owner-protected.pdf")])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Unsupported && eligible.Admissions == 0,
                "PDF direct: known encryption does not start paid admission");
            var offset = vm.Rows.Count;
            vm.Admit(new(Guid.NewGuid(), "optimize", "lossless", [Path.Combine(pdfFixtures, "unattached-signature-canary.pdf"), pdf, flac, png]));
            await vm.WaitForIdleAsync();
            check(vm.Rows.Skip(offset).Select(row => row.Result.State).SequenceEqual([OperationState.Unsupported, OperationState.Succeeded, OperationState.Succeeded, OperationState.Succeeded]) && eligible.Admissions == 1,
                "PDF direct: refused signature retains a combined partial result and later valid documents");
            vm.QuickBatchStarted += (_, rows) =>
            {
                foreach (var row in rows) row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FileRow.Result) && row.Path == png && row.Result.State == OperationState.Succeeded)
                        vm.CancelCommand.Execute(null);
                };
            };
            vm.Admit(new(Guid.NewGuid(), "optimize", "lossless", [pdf, flac, png])); await vm.WaitForIdleAsync();
            check(vm.Rows.TakeLast(3).Select(row => row.Result.State).SequenceEqual([OperationState.Cancelled, OperationState.Cancelled, OperationState.Succeeded]),
                "PDF direct: cancellation between families preserves committed PNG and skips audio/PDF");
        }
        var unavailable = new Access { Allowed = true };
        var missingWorker = new WorkerClient(Path.Combine(scratch, "missing-worker.exe"));
        await using (var vm = new MainViewModel(missingWorker, new(), publisher, unavailable))
        {
            vm.Admit(new(Guid.NewGuid(), "optimize", "auto", [pdf])); await vm.WaitForIdleAsync();
            check(vm.Rows.Single().Status.Contains("unavailable in this build") && unavailable.Admissions == 0 && missingWorker.ProcessId is null,
                "PDF direct: missing optional engine creates no worker or trial admission");
        }
        check(paths.Select((path, i) => SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hashes[i])).All(value => value) &&
            Directory.GetFiles(publisher.RecordDirectory).Length == 0 && !Directory.GetFiles(scratch, ".context-suite-*.tmp", SearchOption.AllDirectories).Any(),
            "PDF direct: generated originals and publication cleanup survive all paths");
        await File.WriteAllTextAsync(Path.Combine(scratch, "pdf-direct.json"), JsonSerializer.Serialize(new { paths, originalSha256 = hashes.Select(Convert.ToHexString), mixedAdmissions = access.Admissions }));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => 0;
    }
    private sealed class Access(IOperationAccess? inner = null) : IOperationAccess
    {
        public bool Allowed { get; set; }
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => inner?.ReadAccessAsync(token) ??
            Task.FromResult(new OperationAccessStatus(Allowed, Allowed ? "License active." : "Activate a license to optimize."));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException();
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token)
        { Admissions++; return inner?.AdmitOptimizationAsync(confirmed, token) ?? Admit(confirmed.Plan.BatchId, token); }
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedFlacOptimization confirmed, CancellationToken token)
        { Admissions++; return inner?.AdmitOptimizationAsync(confirmed, token) ?? Admit(confirmed.Plan.BatchId, token); }
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPdfOptimization confirmed, CancellationToken token)
        { Admissions++; return inner?.AdmitOptimizationAsync(confirmed, token) ?? Admit(confirmed.Plan.BatchId, token); }
        private async Task<OperationAdmission> Admit(Guid id, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return new(await ReadAccessAsync(token), id); }
    }
    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) => throw new InvalidOperationException("Copy tests must never recycle.");
    }
}
