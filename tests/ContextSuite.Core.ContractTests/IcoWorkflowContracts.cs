using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class IcoWorkflowContracts
{
    public static async Task RunAsync(string scratch, string executable, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "ico-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle());
        var options = new ImageConversionOptions(ImageFormat.Ico, Metadata: ImageMetadataMode.Automatic);
        await using var worker = new WorkerClient(executable, Path.Combine(root, "worker"));
        var sources = new List<ImageSourceFacts>();
        foreach (var (width, height) in new[] { (1, 1), (20, 10), (512, 256), (256, 512) })
        {
            var path = Path.Combine(root, $"source-{width}-{height}.png");
            WriteSource(path, width, height);
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            var facts = await worker.ProbeAsync(new(Guid.NewGuid(), path), default);
            sources.Add(facts);
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new(true)), true);
            check(!plan.ReplaceOriginal && plan.CanConfirmQuickAction, "ICO direct plan overrides overwrite with a quiet copy");
            var reservation = await publisher.ReserveAsync(new(facts.ItemId, path, "ico", plan.Settings));
            var result = await worker.ConvertAsync(new(plan.Items[0], options, reservation.TemporaryPath), default);
            check(!File.Exists(reservation.Record.OutputPath), "ICO worker validates without publishing");
            var published = await publisher.PublishAsync(reservation, result.Validation);
            check(published.Outcome == PublicationOutcome.CopyCreated && SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hash),
                "ICO validated copy publishes without changing its original");
            var preview = await worker.PreviewAsync(new(facts, options), default);
            using var stream = File.OpenRead(published.OutputPath!);
            // Independent Windows ICO decoder, not the production writer/parser.
            var decoded = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var layouts = IconFrameLayout.Create((uint)width, (uint)height);
            check(decoded.Frames.Count == layouts.Length, "Windows decodes every ICO frame");
            // WPF orders ICO frames by preferred display size, not directory order.
            var ordered = decoded.Frames.OrderBy(frame => frame.PixelWidth).ToArray();
            for (var i = 0; i < decoded.Frames.Count; i++)
            {
                var layout = layouts[i];
                var frame = ordered[i];
                check(frame.PixelWidth == layout.Size && frame.PixelHeight == layout.Size, "Windows ICO frame dimensions: " + layout.Size);
                var rgba = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var actual = new byte[layout.Size * layout.Size * 4];
                rgba.CopyPixels(actual, (int)layout.Size * 4, 0);
                var expected = new byte[actual.Length];
                for (var y = layout.Top; y < layout.Top + layout.Height; y++)
                for (var x = layout.Left; x < layout.Left + layout.Width; x++)
                { expected[(y * layout.Size + x) * 4 + 2] = 255; expected[(y * layout.Size + x) * 4 + 3] = 128; }
                check(actual.SequenceEqual(expected), "Windows independently verifies straight alpha, red pixels and padding: " + layout.Size);
                if (i == decoded.Frames.Count - 1)
                    check(preview.Width == layout.Size && preview.Height == layout.Size && preview.BgraPixels.SequenceEqual(actual),
                        "ICO preview matches independent Windows decode");
            }
        }
        var invalid = Path.Combine(root, "broken.png"); File.WriteAllText(invalid, "not an image");
        await using (var direct = new MainViewModel(new WorkerClient(executable, Path.Combine(root, "direct")),
            new SuiteSettings { Convert = new(true) }, publisher, new LocalTrialStore(Path.Combine(root, "trial.json"))))
        {
            direct.ConversionRequested += (_, _) => throw new InvalidOperationException("ICO must not open a routine planner.");
            direct.Admit(new(Guid.NewGuid(), "convert", "ico", [sources[0].Path, invalid]));
            await direct.WaitForIdleAsync();
            check(direct.Rows.Count == 2 && direct.Rows.Count(row => row.Result.State == OperationState.Succeeded && row.HasOutput) == 1 &&
                direct.Rows.Count(row => row.Result.State == OperationState.Unsupported) == 1,
                "ICO direct command publishes valid selection and reports unsupported file separately");
            check(direct.Rows.Single(row => row.HasOutput).OutputPath!.EndsWith(".ico", StringComparison.OrdinalIgnoreCase),
                "ICO direct command routes to the icon encoder");
        }

        var executor = new ImageBatchExecutor(worker, publisher, new LocalTrialStore(Path.Combine(root, "fault-trial.json")));
        foreach (var fault in new[] { "cancel", "crash", "changed" })
        {
            var faultSources = sources.Take(3).Select(s => s with { ItemId = Guid.NewGuid() }).ToArray();
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), faultSources, options, new("convert", new()));
            using var cancellation = new CancellationTokenSource();
            var fired = false;
            byte[]? restore = null;
            var result = await executor.ExecuteAsync(plan.Confirm(false, false, false), (item, state) =>
            {
                if (fired || item.Source.ItemId != faultSources[1].ItemId || state.Message != "Encoding and validating output") return;
                fired = true;
                if (fault == "cancel") cancellation.Cancel();
                else if (fault == "crash") { using var owned = Process.GetProcessById(worker.ProcessId!.Value); owned.Kill(true); owned.WaitForExit(5000); }
                else { restore = File.ReadAllBytes(item.Source.Path); File.AppendAllText(item.Source.Path, "changed"); }
            }, cancellation.Token);
            if (restore is not null) File.WriteAllBytes(faultSources[1].Path, restore);
            check(fired && result.Results[0].State == OperationState.Succeeded &&
                result.Results[1].State == (fault == "cancel" ? OperationState.Cancelled : OperationState.Failed) &&
                result.Results[2].State == (fault == "cancel" ? OperationState.Cancelled : OperationState.Succeeded),
                "ICO batch preserves completed outputs and handles " + fault);
            check(!Directory.GetFiles(root, ".context-suite-*.tmp").Any(), "ICO failed reservations are cleaned: " + fault);
            var before = Directory.GetFiles(root, "*.ico").ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
            var retrySources = faultSources.Where((_, i) => result.Results[i].State != OperationState.Succeeded).ToArray();
            var retry = ImageConversionPlanner.Create(Guid.NewGuid(), retrySources, options, new("convert", new()));
            var replay = await executor.ExecuteAsync(retry.Confirm(false, false, false), null, default);
            check(replay.Results.All(r => r.State == OperationState.Succeeded) &&
                Directory.GetFiles(root, "*.ico").Length == before.Count + retrySources.Length &&
                before.All(p => SHA256.HashData(File.ReadAllBytes(p.Key)).SequenceEqual(p.Value)),
                "ICO retry publishes only failed/cancelled items, preserving earlier outputs: " + fault);
        }
    }

    public static async Task RunInterruptionAsync(string scratch, string executable, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "ico-active-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "large.png");
        ImageInterruptionContracts.WriteNoisePng(path);
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle());
        var clock = new ImageInterruptionContracts.DeadlineClock();
        await using var worker = new WorkerClient(executable, Path.Combine(root, "worker"), clock);
        var options = new ImageConversionOptions(ImageFormat.Ico, Metadata: ImageMetadataMode.Automatic);
        foreach (var fault in new[] { "cancel", "crash", "timeout" })
        {
            var source = await worker.ProbeAsync(new(Guid.NewGuid(), path), default);
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [source], options, new("convert", new()));
            var reservation = await publisher.ReserveAsync(new(source.ItemId, path, "ico", plan.Settings));
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
                "ICO worker is computing an active large-image request before fault: " + fault);
            if (fault == "cancel") cancellation.Cancel();
            else if (fault == "crash") owned.Kill(true);
            else
            {
                check(clock.Current!.Due == TimeSpan.FromSeconds(120), "ICO conversion retains the 120-second deadline");
                clock.Current.Expire();
            }
            try { await conversion.WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Interrupted ICO returned success."); }
            catch (OperationCanceledException) when (fault == "cancel") { check(true, "Active ICO cancellation stays cancellation"); }
            catch (MediaWorkerException error) when (fault != "cancel")
            {
                check(error.Failure == (fault == "crash" ? ImageFailure.WorkerTerminated : ImageFailure.TimedOut),
                    "Active ICO fault retains its typed result: " + fault);
            }
            await publisher.AbandonAsync(reservation, fault == "cancel");
            check(!File.Exists(reservation.TemporaryPath) && !File.Exists(reservation.Record.OutputPath) &&
                SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hash), "Active ICO fault publishes nothing and keeps original: " + fault);
            await worker.ProbeAsync(new(Guid.NewGuid(), path), default);
            check(worker.ProcessId != owned.Id, "ICO starts a fresh worker after active interruption: " + fault);
        }
    }

    private static void WriteSource(string path, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 2] = 255; pixels[i + 3] = 128; }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Icon conversion must keep original files.");
    }
}
