using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.ContractTests;

internal static class PdfPageDirectContracts
{
    public static async Task RunAsync(string root, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use new PDF direct evidence.");
        Directory.CreateDirectory(root);
        var pdf = Path.Combine(root, "Document.pdf"); File.Copy(Path.Combine(fixtures, "authored original ü.pdf"), pdf);
        var bmp = Path.Combine(root, "Image.bmp");
        var bytes = new byte[58]; bytes[0] = 66; bytes[1] = 77;
        foreach (var (offset, value) in new[] { (2, 58), (10, 54), (14, 40), (18, 1), (22, 1), (34, 4) }) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        bytes[26] = 1; bytes[28] = 24; bytes[54] = 127; bytes[55] = 63; bytes[56] = 255;
        await File.WriteAllBytesAsync(bmp, bytes);
        var png = Path.Combine(root, "Already.png"); ImageWorkerContracts.WritePng(png);
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(pdf));
        var clock = new Clock(); var access = new Access(new LocalTrialStore(Path.Combine(root, "trial.json"), clock));
        await using (var vm = Create(root, access))
        {
            var sound = false; var quiet = new QuietWorkflow(); var starts = 0; var finishes = 0;
            vm.QuickBatchStarted += (request, rows) =>
            {
                starts++; quiet.Begin(request.RequestId, true, DateTimeOffset.UtcNow);
                foreach (var row in rows) row.PropertyChanged += (_, e) =>
                { if (e.PropertyName == nameof(FileRow.Result) && row.Path == bmp && row.Result.State == OperationState.Succeeded) clock.Now = clock.Now.AddDays(4); };
            };
            vm.QuickBatchCompleted += (request, rows) => { finishes++; sound = quiet.Complete(request.RequestId, rows.Select(row => row.Result).ToArray()); };
            vm.Admit(new(Guid.NewGuid(), "convert", "png", [pdf, bmp, png])); await vm.WaitForIdleAsync();
            check(vm.Rows.Select(row => row.Path).SequenceEqual([pdf, bmp, png]) && vm.Rows[0].SavedPdfPages == 2 && vm.Rows[1].Result.State == OperationState.Succeeded &&
                vm.Rows[2].Result.State == OperationState.Unchanged, "PDF page direct: PNG target handles mixed PDF/image/already-PNG selection in original row order");
            check(access.Admissions == 1 && access.ImageAdmissions == 1 && starts == 1 && finishes == 1 && sound && !quiet.NeedsAttention && !vm.HasProblems,
                "PDF page direct: one admission across expiry and one quiet completion cover the mixed batch");
            check(vm.Rows[0].Outcome == "2 of 2 pages saved" && vm.Rows[0].ResultDetails.Contains("Page 1:") && vm.Rows[0].ResultDetails.Contains("Page 2:") &&
                !vm.Summary.Contains("No files changed") && vm.Rows[0].HasOutput, "PDF page direct: one document row exposes every page path and correct completion summary");
        }

        var retryRoot = Path.Combine(root, "retry"); Directory.CreateDirectory(retryRoot);
        var retryAccess = new Access();
        await using (var vm = Create(retryRoot, retryAccess))
        {
            vm.Settings = vm.Settings with { Convert = new(OutputDirectory: retryRoot, ReplaceOriginals: true) };
            var cancelOnce = true;
            vm.QuickBatchStarted += (_, rows) =>
            {
                foreach (var row in rows) row.PropertyChanged += (_, e) =>
                { if (cancelOnce && e.PropertyName == nameof(FileRow.Result) && row.SavedPdfPages == 1) { cancelOnce = false; vm.CancelCommand.Execute(null); } };
            };
            var quiet = new QuietWorkflow();
            vm.QuickBatchStarted += (request, _) => quiet.Begin(request.RequestId, true, DateTimeOffset.UtcNow);
            vm.QuickBatchCompleted += (request, rows) => quiet.Complete(request.RequestId, rows.Select(row => row.Result).ToArray());
            vm.Admit(new(Guid.NewGuid(), "convert", "png", [pdf])); await vm.WaitForIdleAsync();
            var partial = vm.Rows.Single(); var firstCopy = partial.OutputPath; var firstHash = SHA256.HashData(await File.ReadAllBytesAsync(firstCopy));
            check(partial.SavedPdfPages == 1 && partial.Result.State == OperationState.Cancelled && partial.Result.PartialOutput && vm.CanRetry && vm.HasProblems && vm.ShowDetails && quiet.NeedsAttention,
                "PDF page direct: partial cancellation is visible and retryable without a success sound");
            check(!vm.Summary.Contains("No files changed") && partial.Status.Contains("1 page not converted"), "PDF page direct: partial summary acknowledges saved and unfinished pages");
            var secondFolder = Path.Combine(retryRoot, "new-folder"); Directory.CreateDirectory(secondFolder);
            vm.Settings = vm.Settings with { Convert = new(OutputDirectory: secondFolder, ReplaceOriginals: true) };
            vm.RetryFailed(); vm.RetryFailed(); await vm.WaitForIdleAsync();
            var resumed = vm.DisplayRows.Single();
            check(vm.Rows.Count == 2 && partial.WasRetried && resumed.SavedPdfPages == 2 && resumed.Result.State == OperationState.Succeeded && !vm.CanRetry,
                "PDF page direct: repeated retry queues once and replaces the displayed partial attempt");
            check(Directory.GetFiles(retryRoot, "*.png").Length == 1 && Directory.GetFiles(secondFolder, "*.png").Select(Path.GetFileName).SequenceEqual(["Document - Page 002.png"]) &&
                SHA256.HashData(await File.ReadAllBytesAsync(firstCopy)).SequenceEqual(firstHash), "PDF page direct: retry uses current folder only for missing pages and preserves first output exactly");
            check(resumed.ResultDetails.Contains(firstCopy) && resumed.ResultDetails.Contains(secondFolder) && retryAccess.Admissions == 2,
                "PDF page direct: resumed document retains both output locations and takes one new admission");
        }

        var changedRoot = Path.Combine(root, "changed"); Directory.CreateDirectory(changedRoot);
        var changed = Path.Combine(changedRoot, "Changed.pdf"); File.Copy(pdf, changed);
        await using (var vm = Create(changedRoot, new()))
        {
            var cancelChangedOnce = true;
            vm.QuickBatchStarted += (_, rows) => rows[0].PropertyChanged += (_, e) =>
            { if (cancelChangedOnce && e.PropertyName == nameof(FileRow.Result) && rows[0].SavedPdfPages == 1 && rows[0].Result.State == OperationState.Running)
                { cancelChangedOnce = false; vm.CancelCommand.Execute(null); } };
            vm.Admit(new(Guid.NewGuid(), "convert", "png", [changed])); await vm.WaitForIdleAsync();
            var count = Directory.GetFiles(changedRoot, "*.png").Length;
            await File.AppendAllTextAsync(changed, "\n");
            vm.RetryFailed(); await vm.WaitForIdleAsync();
            check(vm.DisplayRows.Single().PdfResumeBlocked && vm.DisplayRows.Single().Status.Contains("Start a new Convert command") && !vm.CanRetry &&
                Directory.GetFiles(changedRoot, "*.png").Length == count, "PDF page direct: changed source blocks stale-page resume and preserves completed copies");
        }

        var deniedRoot = Path.Combine(root, "denied"); Directory.CreateDirectory(deniedRoot);
        var deniedAccess = new Access { Allowed = false };
        await using (var vm = Create(deniedRoot, deniedAccess))
        {
            vm.Admit(new(Guid.NewGuid(), "convert", "png", [pdf])); await vm.WaitForIdleAsync();
            check(vm.Rows.Single().Status.Contains("Activate") && vm.CanRetry && Directory.GetFiles(deniedRoot, "*.png").Length == 0,
                "PDF page direct: denied access explains activation and creates no output");
            deniedAccess.Allowed = true; vm.RetryFailed(); await vm.WaitForIdleAsync();
            check(vm.DisplayRows.Single().SavedPdfPages == 2 && !vm.HasProblems, "PDF page direct: activation-equivalent access change makes retry usable");
        }
        var failureRoot = Path.Combine(root, "failure-retry"); Directory.CreateDirectory(failureRoot);
        await using (var vm = Create(failureRoot, new(), new FailSecondOnce()))
        {
            vm.Admit(new(Guid.NewGuid(), "convert", "png", [pdf])); await vm.WaitForIdleAsync();
            check(vm.Rows.Single().SavedPdfPages == 1 && vm.Rows.Single().Result.State == OperationState.Failed && vm.CanRetry,
                "PDF page direct: failed later publication exposes partial result and retry");
            var recovery = Directory.GetFiles(Path.Combine(failureRoot, "records"), "*.json").Single();
            vm.RetryFailed(); await vm.WaitForIdleAsync();
            check(vm.DisplayRows.Single().SavedPdfPages == 2 && Directory.GetFiles(failureRoot, "*.png").Length == 2 &&
                File.Exists(recovery) && vm.DisplayRows.Single().ResultDetails.Contains(recovery),
                "PDF page direct: failure retry creates only missing copy and retains earlier recovery location");
        }
        var absent = new WorkerClient(Path.Combine(root, "absent", "ContextSuite.Worker.exe"));
        var absentAccess = new Access();
        await using (var vm = new MainViewModel(absent, new(), new OutputPublisher(Path.Combine(root, "absent-records"), new ForbiddenRecycle()), absentAccess))
        {
            vm.Admit(new(Guid.NewGuid(), "convert", "png", [pdf])); await vm.WaitForIdleAsync();
            check(vm.Rows.Single().Result.State == OperationState.Unsupported && vm.Rows.Single().Status.Contains("unavailable") && absent.ProcessId is null && absentAccess.Admissions == 0,
                "PDF page direct: missing renderer starts neither worker nor trial admission");
        }
        var otherRoot = Path.Combine(root, "unsupported"); Directory.CreateDirectory(otherRoot);
        await using (var vm = Create(otherRoot, new()))
        {
            vm.Admit(new(Guid.NewGuid(), "convert", "jpeg", [pdf])); await vm.WaitForIdleAsync();
            check(vm.Rows.Single().Result.State == OperationState.Unsupported && vm.Rows.Single().Status.Contains("Convert > PNG"), "PDF page direct: other targets explain supported PNG action");
            var renamed = Path.Combine(otherRoot, "renamed.txt"); File.Copy(pdf, renamed);
            vm.Admit(new(Guid.NewGuid(), "convert", "png", [renamed])); await vm.WaitForIdleAsync();
            check(vm.Rows.Last().Result.State == OperationState.Unsupported && vm.Rows.Last().Status.Contains("Rename"), "PDF page direct: content/extension disagreement is explained before rendering");
            vm.Admit(new(Guid.NewGuid(), "convert", "png", [Path.Combine(fixtures, "owner-protected.pdf")])); await vm.WaitForIdleAsync();
            check(vm.Rows.Last().Result.State == OperationState.Failed && vm.Rows.Last().Status.Contains("unprotected") && !vm.Rows.Last().HasOutput,
                "PDF page direct: protected-file failure uses document guidance and publishes nothing");
        }
        check(SHA256.HashData(await File.ReadAllBytesAsync(pdf)).SequenceEqual(hash) && File.ReadAllBytes(bmp).SequenceEqual(bytes), "PDF page direct: source PDF and image stay unchanged");
        await File.WriteAllTextAsync(Path.Combine(root, "pdf-page-direct.json"), JsonSerializer.Serialize(new { pdf, sourceSha256 = Convert.ToHexString(hash), mixedAdmissions = access.Admissions, retryAdmissions = retryAccess.Admissions }));

        MainViewModel Create(string scratch, Access current, PublicationIo? io = null)
        {
            var vm = new MainViewModel(new WorkerClient(executable, Path.Combine(scratch, "workers")),
                new() { Convert = new(OutputDirectory: scratch) }, new OutputPublisher(Path.Combine(scratch, "records"), new ForbiddenRecycle(), io: io), current);
            vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Routine PDF/PNG must not open a planner.");
            return vm;
        }
    }
    private sealed class Access(IOperationAccess? inner = null) : IOperationAccess
    {
        public bool Allowed { get; set; } = true;
        public int Admissions { get; private set; }
        public int ImageAdmissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => inner?.ReadAccessAsync(token) ?? Task.FromResult(new OperationAccessStatus(Allowed, Allowed ? "License active." : "Activate a license to convert."));
        public async Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token)
        { Admissions++; ImageAdmissions++; return inner is null ? new(await ReadAccessAsync(token), confirmed.Plan.BatchId) : await inner.AdmitConversionAsync(confirmed, token); }
        public async Task<OperationAdmission> AdmitConversionAsync(ConfirmedPdfPageConversion confirmed, CancellationToken token)
        { Admissions++; return inner is null ? new(await ReadAccessAsync(token), confirmed.Plan.BatchId) : await inner.AdmitConversionAsync(confirmed, token); }
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected Optimize admission.");
    }
    private sealed class FailSecondOnce : PublicationIo
    {
        private bool _failed;
        public override void Move(string temporary, string destination)
        {
            if (!_failed && destination.Contains("Page 002")) { _failed = true; throw new IOException("Generated second-page publication failure."); }
            base.Move(temporary, destination);
        }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) => throw new InvalidOperationException("Page copies must not recycle.");
    }
}
