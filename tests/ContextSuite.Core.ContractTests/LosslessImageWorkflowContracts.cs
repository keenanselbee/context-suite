using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Transport;

namespace ContextSuite.Core.ContractTests;

internal static class LosslessImageWorkflowContracts
{
    public static async Task RunAsync(string scratch, string? executable, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "lossless-image-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(root, "policy.jpg"), new('A', 64), 1000,
            ImageFormat.Jpeg, 16, 8, 8, 6, false, true, "sRGB", ["exif"]);
        var settings = new BatchSettings("optimize", new(ReplaceOriginals: true));
        var plan = LosslessImageOptimizationPlan.Create(Guid.NewGuid(), [source], settings);
        var confirmed = plan.Confirm();
        check(confirmed.Plan.Items.Single().CanExecute && confirmed.Plan.Settings.Preferences.ReplaceOriginals,
            "lossless image policy: JPEG confirmation preserves saved settings while mandatory copies remain available");
        var webp = source with { ItemId = Guid.NewGuid(), Path = Path.Combine(root, "policy.webp"), Format = ImageFormat.WebP, IsLossy = false };
        check(LosslessImageOptimizationPlan.Create(Guid.NewGuid(), [webp], settings).Confirm().Plan.HasExecutableItems,
            "lossless image policy: still lossless WebP has an explicit recipe");
        check(LosslessImageOptimizationPlan.Create(Guid.NewGuid(), [webp with { IsLossy = true }], settings).HasExecutableItems,
            "image policy: lossy WebP can preserve its decoded samples without implying restored quality");
        foreach (var preset in Enum.GetValues<ImageOptimizationPreset>())
        foreach (var image in new[] { source, webp, webp with { IsLossy = true } })
        {
            var selected = LosslessImageOptimizationPlan.Create(Guid.NewGuid(), [image], settings, preset).Confirm();
            var policy = LosslessImageOptimizationPlan.PolicyFor(image.Format, preset);
            new LosslessImageOptimizationWork(image, Path.Combine(root, $".context-suite-{image.ItemId:N}.tmp"), policy).Validate();
            check(selected.Plan.Preset == preset && LosslessImageOptimizationPlan.PresetFor(image.Format, policy) == preset,
                $"image policy: {image.Format}/{preset} survives confirmation and work validation");
        }
        Reject(() => (plan with { Preset = (ImageOptimizationPreset)99 }).Confirm(), "unknown preset");
        check(!LosslessImageOptimizationPlan.Create(Guid.NewGuid(), [source with { Format = ImageFormat.Png }], settings).HasExecutableItems,
            "lossless image policy: PNG remains with its existing recipes");
        check(!LosslessImageOptimizationPlan.Create(Guid.NewGuid(), [source with { UnsupportedReason = "unsupported variant" }], settings).HasExecutableItems,
            "lossless image policy: unsupported source cannot enter execution");
        Reject(() => (plan with { Items = [plan.Items[0] with { BlockReason = "forged" }] }).Confirm(), "forged plan items");
        Reject(() => LosslessImageOptimizationPlan.Create(Guid.Empty, [source], settings), "empty batch ID");
        Reject(() => LosslessImageOptimizationPlan.Create(Guid.NewGuid(), [source, source], settings), "duplicate item IDs");
        Reject(() => LosslessImageOptimizationPlan.Create(Guid.NewGuid(), [source], new("convert", new())), "wrong operation");
        var work = new LosslessImageOptimizationWork(source, Path.Combine(root, $".context-suite-{source.ItemId:N}.tmp"), LosslessImageOptimizationPlan.JpegPolicy);
        work.Validate();
        Reject(() => (work with { Policy = LosslessImageOptimizationPlan.WebPPolicy }).Validate(), "cross-format policy");
        Reject(() => (work with { TemporaryPath = source.Path }).Validate(), "source as reservation");
        Reject(() => (work with { TemporaryPath = Path.Combine(root, "wrong.tmp") }).Validate(), "unreserved filename");
        Reject(() => (work with { Policy = "jpeg-balanced-preserve-v99" }).Validate(), "unknown recipe version");
        Reject(() => (work with { Source = source with { FileBytes = 128L * 1024 * 1024 + 1 } }).Validate(), "oversized source facts");
        var command = new WorkerCommand(1, Guid.NewGuid(), "image-lossless-optimize", LosslessImageWork: work);
        command.Validate();
        new WorkerCommand(1, Guid.NewGuid(), "image-lossless-probe", Probe: new(source.ItemId, source.Path)).Validate();
        Reject(() => (command with { Command = "png-optimize" }).Validate(), "lossless payload on PNG command");
        Reject(() => (command with { LosslessImageWork = null }).Validate(), "missing lossless work");
        Reject(() => (command with { Probe = new(source.ItemId, source.Path) }).Validate(), "mixed probe and execution payload");
        Reject(() => (command with { AudioBytes = [1] }).Validate(), "unrelated payload");
        Reject(() => (command with { RequestId = Guid.Empty }).Validate(), "missing worker request ID");

        var clock = new Clock();
        var trialPath = Path.Combine(root, "policy-trial", "trial.json");
        IOperationAccess trial = new LocalTrialStore(trialPath, clock);
        check(!File.Exists(trialPath), "lossless image policy: planning and work validation leave trial unstarted");
        var admitted = await trial.AdmitOptimizationAsync(confirmed, default);
        check(admitted.IsAllowed && admitted.BatchId == plan.BatchId && File.Exists(trialPath),
            "lossless image policy: confirmed plan starts one isolated trial admission");
        clock.Now = clock.Now.AddDays(8);
        check(!(await trial.AdmitOptimizationAsync(confirmed, default)).IsAllowed,
            "lossless image policy: expired trial blocks a new admission");
        if (executable is null) return;

        var pngPath = Path.Combine(root, "authored.png");
        ImageWorkerContracts.WritePng(pngPath, compression: CompressionLevel.NoCompression);
        var jpegPath = Path.Combine(root, "photo.jpg");
        WriteJpeg(jpegPath);
        await using var worker = new WorkerClient(executable, Path.Combine(root, "worker"));
        var png = await worker.ProbeAsync(new(Guid.NewGuid(), pngPath), default);
        var webpPath = await ConvertToWebP(true);
        var lossyPath = await ConvertToWebP(false);
        var originals = new[] { pngPath, jpegPath, webpPath, lossyPath }.ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        var jpeg = await worker.ProbeLosslessImageAsync(new(Guid.NewGuid(), jpegPath), default);
        var lossless = await worker.ProbeLosslessImageAsync(new(Guid.NewGuid(), webpPath), default);
        check(jpeg.Format == ImageFormat.Jpeg && lossless.Format == ImageFormat.WebP && !lossless.IsLossy,
            "lossless image worker: independent JPEG and converted VP8L receive typed probe facts");
        var workerId = worker.ProcessId;
        var lossy = await worker.ProbeLosslessImageAsync(new(Guid.NewGuid(), lossyPath), default);
        check(lossy.Format == ImageFormat.WebP && lossy.IsLossy, "image worker: still lossy WebP retains honest encoding facts");
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle(), replacementVerified: true);
        var executor = new LosslessImageOptimizationExecutor(worker, publisher);
        var freshTrial = new CountedAccess(new LocalTrialStore(Path.Combine(root, "worker-trial", "trial.json")));
        var batch = LosslessImageOptimizationPlan.Create(Guid.NewGuid(), [jpeg, lossless], settings).Confirm();
        var admission = await freshTrial.AdmitOptimizationAsync(batch, default);
        var collision = Path.Combine(root, OutputNames.Create(jpegPath, "optimize", "jpg"));
        await File.WriteAllTextAsync(collision, "existing output canary");
        var execution = await executor.ExecuteAdmittedAsync(batch, admission, null, default);
        check(execution.Results[0].Publication?.Outcome == PublicationOutcome.CopyCreated && execution.Results[0].Publication!.OutputPath != collision &&
            await File.ReadAllTextAsync(collision) == "existing output canary", "lossless image worker: smaller JPEG copy resolves collision without overwrite");
        check(execution.Results.All(row => row.State is OperationState.Succeeded or OperationState.Unchanged) && freshTrial.Admissions == 1 && worker.ProcessId == workerId,
            "lossless image worker: JPEG and VP8L share one admission and sequential worker");
        check(execution.Results.Where(row => row.State == OperationState.Succeeded).All(row => row.Publication!.Outcome == PublicationOutcome.CopyCreated) &&
            originals.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)),
            "lossless image worker: overwrite preference still creates copies and preserves every original");
        var publishedJpeg = execution.Results[0].Publication!.OutputPath!;
        check(WindowsPixels(jpegPath).SequenceEqual(WindowsPixels(publishedJpeg)), "lossless image worker: Windows independently verifies exact JPEG decoded pixels");
        var repeated = await worker.ProbeLosslessImageAsync(new(Guid.NewGuid(), publishedJpeg), default);
        var repeat = LosslessImageOptimizationPlan.Create(Guid.NewGuid(), [repeated], settings).Confirm();
        var repeatAdmission = await freshTrial.AdmitOptimizationAsync(repeat, default);
        var beforeRepeat = Directory.GetFiles(root).Length;
        var unchanged = await executor.ExecuteAdmittedAsync(repeat, repeatAdmission, null, default);
        check(unchanged.Results.Single().State == OperationState.Unchanged && Directory.GetFiles(root).Length == beforeRepeat,
            "lossless image worker: repeated fixed JPEG recipe publishes no duplicate");

        foreach (var extension in new[] { "jpeg", "jpe", "jfif" })
        {
            var aliasPath = Path.Combine(root, "alias." + extension);
            File.Copy(jpegPath, aliasPath);
            var alias = await worker.ProbeLosslessImageAsync(new(Guid.NewGuid(), aliasPath), default);
            var aliasPlan = LosslessImageOptimizationPlan.Create(Guid.NewGuid(), [alias], settings).Confirm();
            var aliasResult = await executor.ExecuteAdmittedAsync(aliasPlan, await freshTrial.AdmitOptimizationAsync(aliasPlan, default), null, default);
            check(aliasResult.Results.Single().Publication is { Outcome: PublicationOutcome.CopyCreated, OutputPath: { } output } &&
                Path.GetExtension(output) == "." + extension && SHA256.HashData(File.ReadAllBytes(aliasPath)).SequenceEqual(originals[jpegPath]),
                "lossless image worker: JPEG alias preserves source and output extension " + extension);
        }
        var reserved = Path.Combine(root, $".context-suite-{jpeg.ItemId:N}.tmp");
        await File.WriteAllTextAsync(reserved, "reservation canary");
        await RefuseWorker(new(jpeg, reserved, LosslessImageOptimizationPlan.JpegPolicy), "nonempty reservation");
        check(await File.ReadAllTextAsync(reserved) == "reservation canary", "lossless image worker: nonempty reservation bytes preserved");
        File.Delete(reserved);
        await File.WriteAllBytesAsync(reserved, []);
        await RefuseWorker(new(jpeg with { Sha256 = new('0', 64) }, reserved, LosslessImageOptimizationPlan.JpegPolicy), "stale source digest");
        check(new FileInfo(reserved).Length == 0, "lossless image worker: stale source cannot write reserved output");
        File.Delete(reserved);
        var changedPath = Path.Combine(root, "changed.jpg");
        File.Copy(jpegPath, changedPath);
        var changed = await worker.ProbeLosslessImageAsync(new(Guid.NewGuid(), changedPath), default);
        await File.AppendAllTextAsync(changedPath, "changed");
        var changedPlan = LosslessImageOptimizationPlan.Create(Guid.NewGuid(), [changed], settings).Confirm();
        var changedResult = await executor.ExecuteAdmittedAsync(changedPlan, await freshTrial.AdmitOptimizationAsync(changedPlan, default), null, default);
        check(changedResult.Results.Single().State == OperationState.Failed && new FileInfo(changedPath).Length == changed.FileBytes + 7,
            "lossless image worker: source change fails safely before publication");

        var mixedClock = new Clock();
        var mixedTrialPath = Path.Combine(root, "mixed-trial", "trial.json");
        var mixedAccess = new CountedAccess(new LocalTrialStore(mixedTrialPath, mixedClock));
        await using (var view = new MainViewModel(new WorkerClient(executable, Path.Combine(root, "mixed-worker")),
            new SuiteSettings { PlayCompletionSound = false }, publisher, mixedAccess))
        {
            var starts = 0; var finishes = 0;
            view.QuickBatchStarted += (_, rows) =>
            {
                starts++;
                foreach (var row in rows)
                    row.PropertyChanged += (_, change) =>
                    {
                        if (change.PropertyName == nameof(FileRow.Result) && row.Path == pngPath && row.Result.State is OperationState.Succeeded or OperationState.Unchanged)
                            mixedClock.Now = mixedClock.Now.AddDays(8);
                    };
            };
            view.QuickBatchCompleted += (_, _) => finishes++;
            view.ConversionRequested += (_, _) => throw new InvalidOperationException("Optimization must not open a conversion planner.");
            view.Admit(new(Guid.NewGuid(), "optimize", "auto", [pngPath, jpegPath, webpPath, lossyPath]));
            await view.WaitForIdleAsync();
            check(starts == 1 && finishes == 1 && mixedAccess.Admissions == 1 && File.Exists(mixedTrialPath) &&
                view.Rows.All(row => row.Result.State is OperationState.Succeeded or OperationState.Unchanged),
                "image direct: mixed PNG/JPEG/VP8L/VP8 completes under one admission across expiry");
            check(view.Rows.Where(row => row.Path != pngPath && row.HasOutput).All(row => row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated) &&
                originals.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)),
                "lossless image direct: mixed copy workflow preserves all originals");
            view.Admit(new(Guid.NewGuid(), "optimize", "lossless", [jpegPath]));
            await view.WaitForIdleAsync();
            check(mixedAccess.Admissions == 2 && view.Rows[^1].Result.State == OperationState.Failed,
                "lossless image direct: next batch after expiry requires fresh access");
        }
        foreach (var preset in new[] { ImageOptimizationPreset.Balanced, ImageOptimizationPreset.Smallest })
        {
            var selected = LosslessImageOptimizationPlan.Create(Guid.NewGuid(), [jpeg, lossless, lossy], settings, preset).Confirm();
            var permitted = await freshTrial.AdmitOptimizationAsync(selected, default);
            var optimized = await executor.ExecuteAdmittedAsync(selected, permitted, null, default);
            check(optimized.Results.All(row => row.State is OperationState.Succeeded or OperationState.Unchanged) &&
                optimized.Results.All(row => row.Message.Contains("Requested: " + preset, StringComparison.Ordinal)),
                $"image workflow: {preset} executes JPEG, VP8L, and VP8 with truthful recipe details");
            check(optimized.Results.Where(row => row.State == OperationState.Succeeded)
                    .All(row => row.Publication?.Outcome == PublicationOutcome.CopyCreated) &&
                originals.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)),
                $"image workflow: {preset} keeps originals even with overwrite enabled");
            await using var view = new MainViewModel(new WorkerClient(executable, Path.Combine(root, "preset-worker-" + preset)),
                new SuiteSettings { PlayCompletionSound = false }, publisher, freshTrial);
            view.Admit(new(Guid.NewGuid(), "optimize", preset.ToString().ToLowerInvariant(), [jpegPath, webpPath, lossyPath]));
            await view.WaitForIdleAsync();
            check(view.Rows.Count == 3 && view.Rows.All(row => row.Result.State is OperationState.Succeeded or OperationState.Unchanged),
                $"image direct: {preset} dispatches every admitted still image encoding");
        }
        check(!Directory.GetFiles(root, ".context-suite-*.tmp").Any() && !Directory.GetFiles(Path.Combine(root, "records")).Any(),
            "lossless image workflow: completed, unchanged, and refused operations leave no active reservations");

        async Task<string> ConvertToWebP(bool losslessEncoding)
        {
            var options = new ImageConversionOptions(ImageFormat.WebP, Quality: 100, WebPLossless: losslessEncoding);
            if (!losslessEncoding) options = options with { Quality = 80 };
            var conversion = ImageConversionPlanner.Create(Guid.NewGuid(), [png with { ItemId = Guid.NewGuid() }], options, new("convert", new()));
            var item = conversion.Confirm(true, false, false).Plan.Items.Single();
            var temporary = Path.Combine(root, $".context-suite-{item.Source.ItemId:N}.tmp");
            await File.WriteAllBytesAsync(temporary, []);
            await worker.ConvertAsync(new(item, options, temporary), default);
            var path = Path.Combine(root, losslessEncoding ? "lossless.webp" : "lossy.webp");
            File.Move(temporary, path);
            return path;
        }

        async Task RefuseWorker(LosslessImageOptimizationWork request, string name)
        {
            try { await worker.OptimizeLosslessImageAsync(request, default); check(false, "lossless image worker: " + name); }
            catch (MediaWorkerException) { check(true, "lossless image worker: refuses " + name); }
        }

        void Reject(Action action, string name)
        {
            var rejected = false;
            try { action(); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "lossless image policy: rejects " + name);
        }
    }

    private static void WriteJpeg(string path)
    {
        var pixels = Enumerable.Range(0, 97 * 65).SelectMany(index => new byte[] { (byte)(index % 97 * 2), (byte)(index / 97 * 3), 120 }).ToArray();
        var image = BitmapSource.Create(97, 65, 96, 96, PixelFormats.Bgr24, null, pixels, 97 * 3);
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var encoded = new MemoryStream();
        encoder.Save(encoded);
        var bytes = encoded.ToArray();
        // JPEG permits FF fill bytes before a marker. The fixed coefficient
        // rewrite removes these bytes without touching metadata or samples.
        using var output = File.Create(path);
        output.Write(bytes.AsSpan(0, bytes.Length - 2));
        output.Write(Enumerable.Repeat((byte)255, 4096).ToArray());
        output.Write(bytes.AsSpan(bytes.Length - 2));
    }

    private static byte[] WindowsPixels(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = new JpegBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var image = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(image.PixelWidth * image.PixelHeight * 4)];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("New lossless image handlers must publish copies.");
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => 0;
    }

    private sealed class CountedAccess(IOperationAccess inner) : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => inner.ReadAccessAsync(token);
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected conversion admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitOptimizationAsync(confirmed, token); }
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedLosslessImageOptimization confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitOptimizationAsync(confirmed, token); }
    }
}
