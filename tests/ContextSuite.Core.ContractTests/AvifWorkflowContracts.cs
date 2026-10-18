using System.Diagnostics;
using System.Security.Cryptography;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class AvifWorkflowContracts
{
    public static async Task RunAsync(string scratch, string executable, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "avif-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle());
        var options = new ImageConversionOptions(ImageFormat.Avif, Quality: ImageConversionOptions.AvifQuality,
            Metadata: ImageMetadataMode.Automatic);
        await using var worker = new WorkerClient(executable, Path.Combine(root, "worker"));
        check(worker.HasAvifEncoder, "AVIF workflow has encoder and independent HEIF decoder payloads");

        var opaquePath = Path.Combine(root, "opaque.png");
        var alphaPath = Path.Combine(root, "alpha.png");
        ImageWorkerContracts.WritePng(opaquePath);
        ImageWorkerContracts.WritePng(alphaPath, transparent: true);
        var opaque = await worker.ProbeAsync(new(Guid.NewGuid(), opaquePath), default);
        var sources = new List<ImageSourceFacts> { await worker.ProbeAsync(new(Guid.NewGuid(), alphaPath), default) };
        foreach (var target in new[] { ImageFormat.Jpeg, ImageFormat.WebP, ImageFormat.Bmp, ImageFormat.Tga })
        {
            var preparation = new ImageConversionOptions(target, WebPLossless: target == ImageFormat.WebP,
                Metadata: ImageMetadataMode.Automatic);
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [opaque with { ItemId = Guid.NewGuid() }],
                preparation, new("convert", new()));
            var item = plan.Items.Single();
            var reservation = await publisher.ReserveAsync(new(item.Source.ItemId, opaquePath, preparation.Extension, plan.Settings));
            var encoded = await worker.ConvertAsync(new(item, preparation, reservation.TemporaryPath), default);
            var published = await publisher.PublishAsync(reservation, encoded.Validation);
            sources.Add(await worker.ProbeAsync(new(Guid.NewGuid(), published.OutputPath!), default));
        }
        check(sources.Select(source => source.Format).SequenceEqual(new[] { ImageFormat.Png, ImageFormat.Jpeg,
            ImageFormat.WebP, ImageFormat.Bmp, ImageFormat.Tga }), "AVIF workflow prepares all five admitted raster inputs");

        var originals = sources.ToDictionary(source => source.Path, source => SHA256.HashData(File.ReadAllBytes(source.Path)));
        foreach (var source in sources)
        {
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [source], options,
                new("convert", new(ReplaceOriginals: true)), true);
            check(!plan.ReplaceOriginal && plan.CanConfirmQuickAction, "AVIF direct plan keeps source under overwrite settings: " + source.Format);
            var reservation = await publisher.ReserveAsync(new(source.ItemId, source.Path, "avif", plan.Settings));
            var encoded = await worker.ConvertAsync(new(plan.Items[0], options, reservation.TemporaryPath), default);
            check(encoded.Validation.MatchesPlan && !File.Exists(reservation.Record.OutputPath),
                "AVIF worker validates without publishing: " + source.Format);
            var published = await publisher.PublishAsync(reservation, encoded.Validation);
            var reopened = await worker.ProbeAsync(new(Guid.NewGuid(), published.OutputPath!), default);
            check(published.Outcome == PublicationOutcome.CopyCreated && reopened.Format == ImageFormat.Avif &&
                reopened.Width == source.Width && reopened.Height == source.Height && reopened.BitDepth == 8 &&
                reopened.HasTransparency == source.HasTransparency && reopened.ProfileNames.Contains("icc") &&
                SHA256.HashData(File.ReadAllBytes(source.Path)).SequenceEqual(originals[source.Path]),
                "AVIF output decodes with expected geometry, alpha, color tag and original copy: " + source.Format);
            if (source.HasTransparency)
            {
                var decoded = await worker.PreviewAsync(new(reopened), default);
                check(decoded.BgraPixels[3] == 128,
                    "AVIF alpha is retained exactly through the independent HEIF output decoder");
            }
        }

        var invalid = Path.Combine(root, "broken.png");
        File.WriteAllText(invalid, "not an image");
        var access = new CountingAccess();
        await using (var directWorker = new WorkerClient(executable, Path.Combine(root, "direct-worker")))
        await using (var direct = new MainViewModel(directWorker,
            new SuiteSettings { Convert = new(ReplaceOriginals: true), PlayCompletionSound = false },
            publisher, access))
        {
            var starts = 0; var ends = 0;
            direct.QuickBatchStarted += (_, _) => starts++;
            direct.QuickBatchCompleted += (_, _) => ends++;
            direct.ConversionRequested += (_, _) => throw new InvalidOperationException("Routine AVIF output must not open a planner.");
            direct.Admit(new(Guid.NewGuid(), "convert", "avif", [.. sources.Select(source => source.Path), invalid]));
            await direct.WaitForIdleAsync();
            check(starts == 1 && ends == 1 && access.Admissions == 1 && direct.Rows.Count == 6 &&
                direct.Rows.Count(row => row.Result.State == OperationState.Succeeded && row.HasOutput) == 5 &&
                direct.Rows.Single(row => row.Path == invalid).Result.State == OperationState.Unsupported,
                "AVIF mixed direct batch uses one admission and reports the unsupported item separately");
            check(direct.Rows.Where(row => row.HasOutput).All(row => row.OutputPath!.EndsWith(".avif", StringComparison.OrdinalIgnoreCase)) &&
                originals.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)),
                "AVIF direct command publishes copies despite replacement settings");
        }

        var executor = new ImageBatchExecutor(worker, publisher, new LocalTrialStore(Path.Combine(root, "fault-trial.json")));
        foreach (var fault in new[] { "cancel", "crash", "changed" })
        {
            var batch = sources.Take(3).Select(source => source with { ItemId = Guid.NewGuid() }).ToArray();
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), batch, options, new("convert", new()));
            using var cancellation = new CancellationTokenSource();
            var fired = false;
            byte[]? restore = null;
            var result = await executor.ExecuteAsync(plan.Confirm(false, false, false), (item, state) =>
            {
                if (fired || item.Source.ItemId != batch[1].ItemId || state.Message != "Encoding and validating output") return;
                fired = true;
                if (fault == "cancel") cancellation.Cancel();
                else if (fault == "crash") { using var owned = Process.GetProcessById(worker.ProcessId!.Value); owned.Kill(true); owned.WaitForExit(5000); }
                else { restore = File.ReadAllBytes(item.Source.Path); File.AppendAllText(item.Source.Path, "changed"); }
            }, cancellation.Token);
            if (restore is not null) File.WriteAllBytes(batch[1].Path, restore);
            check(fired && result.Results[0].State == OperationState.Succeeded &&
                result.Results[1].State == (fault == "cancel" ? OperationState.Cancelled : OperationState.Failed) &&
                result.Results[2].State == (fault == "cancel" ? OperationState.Cancelled : OperationState.Succeeded),
                "AVIF batch retains completed copies after " + fault);
            check(!Directory.GetFiles(root, ".context-suite-*.tmp").Any(), "AVIF failed reservations are cleaned: " + fault);
            var before = Directory.GetFiles(root, "*.avif").ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
            var retrySources = batch.Where((_, index) => result.Results[index].State != OperationState.Succeeded).ToArray();
            var retry = ImageConversionPlanner.Create(Guid.NewGuid(), retrySources, options, new("convert", new()));
            var replay = await executor.ExecuteAsync(retry.Confirm(false, false, false), null, default);
            check(replay.Results.All(row => row.State == OperationState.Succeeded) &&
                Directory.GetFiles(root, "*.avif").Length == before.Count + retrySources.Length &&
                before.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)),
                "AVIF retry adds only failed or cancelled outputs after " + fault);
        }

        foreach (var missing in new[] { "avif", "heif" })
        {
            var unavailablePath = CopyWorkerWithout(executable, Path.Combine(root, "missing-" + missing), missing);
            await using var unavailable = new WorkerClient(unavailablePath, Path.Combine(root, "missing-worker-" + missing));
            check(!unavailable.HasAvifEncoder, "AVIF app gate detects absent " + missing + " engine");
            var source = await unavailable.ProbeAsync(new(Guid.NewGuid(), alphaPath), default);
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [source], options, new("convert", new()));
            var reservation = await publisher.ReserveAsync(new(source.ItemId, alphaPath, "avif", plan.Settings));
            try
            {
                await unavailable.ConvertAsync(new(plan.Items[0], options, reservation.TemporaryPath), default);
                throw new InvalidOperationException("Missing " + missing + " engine was accepted.");
            }
            catch (MediaWorkerException error)
            { check(error.Failure == ImageFailure.EngineUnavailable, "AVIF worker refuses absent " + missing + " engine with typed failure"); }
            await publisher.AbandonAsync(reservation, false);
        }
    }

    public static async Task RunInterruptionAsync(string scratch, string executable, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "avif-active-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "large.png");
        ImageInterruptionContracts.WriteNoisePng(path);
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle());
        var clock = new ImageInterruptionContracts.DeadlineClock();
        await using var worker = new WorkerClient(executable, Path.Combine(root, "worker"), clock);
        check(worker.HasAvifEncoder, "Active AVIF workflow has encoder and independent decoder payloads");
        var options = new ImageConversionOptions(ImageFormat.Avif, Quality: ImageConversionOptions.AvifQuality,
            Metadata: ImageMetadataMode.Automatic);
        foreach (var fault in new[] { "cancel", "crash", "timeout" })
        {
            var source = await worker.ProbeAsync(new(Guid.NewGuid(), path), default);
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [source], options, new("convert", new()));
            var reservation = await publisher.ReserveAsync(new(source.ItemId, path, "avif", plan.Settings));
            using var cancellation = new CancellationTokenSource();
            using var owned = Process.GetProcessById(worker.ProcessId!.Value);
            var cpu = owned.TotalProcessorTime;
            var conversion = worker.ConvertAsync(new(plan.Items[0], options, reservation.TemporaryPath), cancellation.Token);
            var observed = Stopwatch.StartNew();
            while (!conversion.IsCompleted && observed.Elapsed < TimeSpan.FromSeconds(10))
            {
                owned.Refresh();
                if (owned.TotalProcessorTime - cpu >= TimeSpan.FromMilliseconds(30)) break;
                await Task.Delay(5);
            }
            owned.Refresh();
            check(!conversion.IsCompleted && owned.TotalProcessorTime - cpu >= TimeSpan.FromMilliseconds(30),
                "AVIF worker is computing an active large-image request before fault: " + fault);
            if (fault == "cancel") cancellation.Cancel();
            else if (fault == "crash") owned.Kill(true);
            else
            {
                check(clock.Current!.Due == TimeSpan.FromSeconds(120), "AVIF conversion retains the 120-second deadline");
                clock.Current.Expire();
            }
            try { await conversion.WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Interrupted AVIF returned success."); }
            catch (OperationCanceledException) when (fault == "cancel") { check(true, "Active AVIF cancellation stays cancellation"); }
            catch (MediaWorkerException error) when (fault != "cancel")
            {
                check(error.Failure == (fault == "crash" ? ImageFailure.WorkerTerminated : ImageFailure.TimedOut),
                    "Active AVIF fault retains its typed result: " + fault);
            }
            await publisher.AbandonAsync(reservation, fault == "cancel");
            check(!File.Exists(reservation.TemporaryPath) && !File.Exists(reservation.Record.OutputPath) &&
                SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hash),
                "Active AVIF fault publishes nothing and keeps original: " + fault);
            await worker.ProbeAsync(new(Guid.NewGuid(), path), default);
            check(worker.ProcessId != owned.Id, "AVIF starts a fresh worker after active interruption: " + fault);
        }
    }

    private static string CopyWorkerWithout(string executable, string target, string missing)
    {
        var source = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in new[] { "avif", "heif" }.Where(directory => directory != missing))
        {
            var from = Path.Combine(source, directory);
            if (!Directory.Exists(from)) continue;
            var into = Path.Combine(target, directory);
            Directory.CreateDirectory(into);
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(into, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }
        return Path.Combine(target, Path.GetFileName(executable));
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("AVIF output must keep original files.");
    }

    private sealed class CountingAccess : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationAccessStatus(true, "Available"));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken cancellationToken)
        {
            Admissions++;
            return Task.FromResult(new OperationAdmission(new(true, "Available"), confirmed.Plan.BatchId));
        }
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unexpected optimization admission.");
    }
}
