using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;
using ContextSuite.Core.ContractTests;

internal static class AudioDirectContracts
{
    public static async Task RunAsync(string scratch, string executable, string fixtures, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        var flac = Path.Combine(scratch, "Audio.flac");
        var source = await File.ReadAllBytesAsync(Path.Combine(fixtures, "source.flac"));
        var padding = new byte[4 + 131072]; padding[0] = 1; padding[1] = 2;
        await File.WriteAllBytesAsync(flac, source[..42].Concat(padding).Concat(source[42..]).ToArray());
        var png = Path.Combine(scratch, "Image.png");
        ImageWorkerContracts.WritePng(png, compression: CompressionLevel.NoCompression);
        var originalFlac = SHA256.HashData(await File.ReadAllBytesAsync(flac));
        var originalPng = SHA256.HashData(await File.ReadAllBytesAsync(png));
        var clock = new Clock();
        var trialPath = Path.Combine(scratch, "trial", "trial.json");
        var access = new CountedTrial(new LocalTrialStore(trialPath, clock));
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new RefusingRecycler());
        var worker = new WorkerClient(executable, Path.Combine(scratch, "worker"));
        await using (var vm = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access))
        {
            var starts = 0; var finishes = 0; var quiet = new QuietWorkflow();
            vm.QuickBatchStarted += (request, rows) =>
            {
                starts++; quiet.Begin(request.RequestId, false, DateTimeOffset.UtcNow);
                foreach (var row in rows)
                    row.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(FileRow.Result) && row.Path == png && row.Result.State is OperationState.Succeeded or OperationState.Unchanged)
                            clock.Now = clock.Now.AddDays(8);
                    };
            };
            vm.QuickBatchCompleted += (request, rows) => { finishes++; quiet.Complete(request.RequestId, rows.Select(row => row.Result).ToArray()); };
            vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Optimization must not open a planner.");
            check(vm.Admit(new(Guid.NewGuid(), "optimize", "auto", [png, flac])).Accepted, "audio direct: mixed Auto selection is admitted");
            await vm.WaitForIdleAsync();
            check(access.Admissions == 1 && File.Exists(trialPath) && vm.Rows.All(row => row.Result.State == OperationState.Succeeded),
                "audio direct: one trial admission covers both families across expiry");
            check(vm.Rows[0].OutputPath.EndsWith(".png") && vm.Rows[1].OutputPath.EndsWith(".flac"),
                "audio direct: mixed optimization retains each format and names its output copy");
            check(starts == 1 && finishes == 1 && !quiet.NeedsAttention && !vm.HasProblems && !vm.ShowDetails && vm.Summary.Contains("saved"),
                "audio direct: one quiet completion and aggregate savings without a planner");
            var processId = worker.ProcessId;
            vm.Admit(new(Guid.NewGuid(), "optimize", "lossless", [flac])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Failed && access.Admissions == 2 && worker.ProcessId == processId,
                "audio direct: expired next batch is blocked with the shared worker retained");
        }

        var switchable = new SwitchableAccess();
        var retryOutput = Path.Combine(scratch, "retry-output"); Directory.CreateDirectory(retryOutput);
        await using (var vm = new MainViewModel(new WorkerClient(executable, Path.Combine(scratch, "retry-worker")),
            new() { PlayCompletionSound = false }, publisher, switchable))
        {
            vm.Admit(new(Guid.NewGuid(), "optimize", "lossless", [flac])); await vm.WaitForIdleAsync();
            check(vm.Rows.Single().Status.Contains("Activate") && vm.CanRetry && vm.HasProblems,
                "audio direct: blocked access gives activation guidance and a retry action");
            switchable.Allowed = true;
            vm.Settings = new() { Optimize = new(OutputDirectory: retryOutput), PlayCompletionSound = false };
            vm.RetryFailed(); vm.RetryFailed();
            vm.Settings = new() { PlayCompletionSound = false };
            await vm.WaitForIdleAsync();
            check(vm.Rows.Count == 2 && vm.DisplayRows.Count() == 1 && vm.Rows[^1].Action == "lossless" &&
                vm.Rows[^1].Settings.Preferences.OutputDirectory == retryOutput && Path.GetDirectoryName(vm.Rows[^1].OutputPath) == retryOutput &&
                !vm.CanRetry && !vm.HasProblems, "audio direct: activation retry keeps action/files, captures current settings and suppresses duplicate clicks");
            switchable.Allowed = false;
            vm.Admit(new(Guid.NewGuid(), "optimize", "auto", [flac])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Failed, "audio direct: deactivation blocks new Auto work");
            switchable.Allowed = true; vm.RetryFailed(); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Action == "auto" && vm.Rows[^1].Result.State == OperationState.Succeeded && !vm.HasProblems,
                "audio direct: reactivation retries the same Auto action");
        }

        var supportedAccess = new SwitchableAccess { Allowed = true };
        await using (var vm = new MainViewModel(new WorkerClient(executable, Path.Combine(scratch, "other-worker")),
            new() { PlayCompletionSound = false }, publisher, supportedAccess))
        {
            var disguisedFlac = Path.Combine(scratch, "Disguised audio.png"); File.Copy(flac, disguisedFlac);
            var disguisedPng = Path.Combine(scratch, "Disguised image.flac"); File.Copy(png, disguisedPng);
            vm.Admit(new(Guid.NewGuid(), "optimize", "auto", [disguisedFlac, disguisedPng])); await vm.WaitForIdleAsync();
            check(vm.Rows.All(row => row.Result.State == OperationState.Unsupported && row.Status.Contains("Rename it to")) && supportedAccess.Admissions == 0 &&
                File.ReadAllBytes(disguisedFlac).SequenceEqual(File.ReadAllBytes(flac)) && File.ReadAllBytes(disguisedPng).SequenceEqual(File.ReadAllBytes(png)),
                "audio direct: misleading extensions give content-based rename guidance without admission or writes");
            foreach (var action in new[] { "balanced", "smallest" })
            {
                vm.Admit(new(Guid.NewGuid(), "optimize", action, [flac])); await vm.WaitForIdleAsync();
                check(vm.Rows[^1].Result.State == OperationState.Unsupported && vm.Rows[^1].Status.Contains("Auto or Lossless") && supportedAccess.Admissions == 0,
                    "audio direct: " + action + " explains FLAC policy without paid admission");
            }
            var broken = Path.Combine(scratch, "broken.flac"); var bytes = await File.ReadAllBytesAsync(flac); bytes[^1] ^= 1; await File.WriteAllBytesAsync(broken, bytes);
            var before = vm.Rows.Count;
            vm.Admit(new(Guid.NewGuid(), "optimize", "lossless", [png, broken, flac])); await vm.WaitForIdleAsync();
            check(vm.Rows.Skip(before).Select(row => row.Result.State).SequenceEqual([OperationState.Succeeded, OperationState.Failed, OperationState.Succeeded]) &&
                supportedAccess.Admissions == 1, "audio direct: corrupt FLAC does not prevent valid PNG and FLAC copies in one batch");
            vm.QuickBatchStarted += (_, rows) =>
            {
                foreach (var row in rows)
                    row.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(FileRow.Result) && row.Path == png && row.Result.State == OperationState.Succeeded)
                            vm.CancelCommand.Execute(null);
                    };
            };
            vm.Admit(new(Guid.NewGuid(), "optimize", "lossless", [png, flac])); await vm.WaitForIdleAsync();
            check(vm.Rows[^2].Result.State == OperationState.Succeeded && vm.Rows[^1].Result.State == OperationState.Cancelled,
                "audio direct: cancellation between families retains the finished copy");
        }
        var unavailableAccess = new SwitchableAccess { Allowed = true };
        await using (var vm = new MainViewModel(new WorkerClient(Path.Combine(scratch, "missing", "ContextSuite.Worker.exe")),
            new(), publisher, unavailableAccess))
        {
            vm.Admit(new(Guid.NewGuid(), "optimize", "auto", [flac])); await vm.WaitForIdleAsync();
            check(vm.Rows.Single().Result.State == OperationState.Unsupported && vm.Rows[0].Status.Contains("unavailable in this build") && unavailableAccess.Admissions == 0,
                "audio direct: missing engine yields a clear result without trial admission or worker launch");
        }
        check(SHA256.HashData(await File.ReadAllBytesAsync(flac)).SequenceEqual(originalFlac) &&
            SHA256.HashData(await File.ReadAllBytesAsync(png)).SequenceEqual(originalPng) && Directory.GetFiles(publisher.RecordDirectory).Length == 0,
            "audio direct: originals and publication cleanup survive every direct-action path");
        await File.WriteAllTextAsync(Path.Combine(scratch, "direct-audio.json"), JsonSerializer.Serialize(new { originalFlac = Convert.ToHexString(originalFlac), originalPng = Convert.ToHexString(originalPng), trialAdmissions = access.Admissions }));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => 0;
    }
    private sealed class CountedTrial(IOperationAccess inner) : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => inner.ReadAccessAsync(token);
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException();
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitOptimizationAsync(confirmed, token); }
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedFlacOptimization confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitOptimizationAsync(confirmed, token); }
    }
    private sealed class SwitchableAccess : IOperationAccess
    {
        public bool Allowed { get; set; }
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => Task.FromResult(new OperationAccessStatus(Allowed, Allowed ? "License active." : "Activate a license to optimize."));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException();
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => Admit(confirmed.Plan.BatchId, token);
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedFlacOptimization confirmed, CancellationToken token) => Admit(confirmed.Plan.BatchId, token);
        private async Task<OperationAdmission> Admit(Guid id, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Admissions++; return new(await ReadAccessAsync(token), id); }
    }
    private sealed class RefusingRecycler : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) => throw new InvalidOperationException("Copy tests must never recycle.");
    }
}
