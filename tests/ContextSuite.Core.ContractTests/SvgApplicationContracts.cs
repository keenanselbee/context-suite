using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Svg;

namespace ContextSuite.Core.ContractTests;

internal static class SvgApplicationContracts
{
    public static async Task RunJpegWorkerAsync(string scratch, string executable, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        var first = Path.Combine(scratch, "transient.svg");
        var second = Path.Combine(scratch, "companion.svg");
        const string art = "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='12'><rect width='12' height='12' fill='red' opacity='.5'/></svg>";
        await File.WriteAllTextAsync(first, art);
        await File.WriteAllTextAsync(second, art);
        var original = SHA256.HashData(await File.ReadAllBytesAsync(first));
        var settings = new SuiteSettings { Convert = new(ReplaceOriginals: true), PlayCompletionSound = false };
        var access = new CountingAccess();
        var choices = 0;
        await using var worker = new WorkerClient(executable, Path.Combine(scratch, "worker"));
        await using var view = new MainViewModel(worker, settings,
            new OutputPublisher(Path.Combine(scratch, "records"), new WindowsFileRecycler(), io: new FailOnceIo()), access);
        view.SvgSizeRequested += (sources, target, _) =>
        {
            choices++;
            check(sources.Count == 2 && target == ImageFormat.Jpeg, "SVG JPEG asks once for batch size and background");
            return Task.FromResult<SvgConversionOptions?>(new(48, target, 0x123456));
        };
        view.Admit(new(Guid.NewGuid(), "convert", "jpeg", [first, second]));
        await view.WaitForIdleAsync();
        check(access.Admissions == 1 && choices == 1 && view.Rows[0].Result.State == OperationState.Failed &&
            view.Rows[1].Result.State == OperationState.Succeeded, "SVG JPEG retains successful copy after one publication failure");
        var committed = view.Rows[1].OutputPath;
        var committedHash = SHA256.HashData(await File.ReadAllBytesAsync(committed));
        view.RetryFailed();
        await view.WaitForIdleAsync();
        var result = view.Rows.Last();
        check(result.Result.State == OperationState.Succeeded && choices == 1 && access.Admissions == 2 &&
            result.SvgOptions == new SvgConversionOptions(48, ImageFormat.Jpeg, 0x123456),
            "SVG JPEG retry reuses immutable target, width and matte without prompting again");
        check(Directory.EnumerateFiles(scratch, "*.jpg").Count() == 2 &&
            SHA256.HashData(await File.ReadAllBytesAsync(committed)).SequenceEqual(committedHash) &&
            SHA256.HashData(await File.ReadAllBytesAsync(first)).SequenceEqual(original) &&
            SHA256.HashData(await File.ReadAllBytesAsync(second)).SequenceEqual(original),
            "SVG JPEG retry neither duplicates successful output nor replaces either original");
        using (var input = File.OpenRead(result.OutputPath))
        {
            var frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
            var pixels = new byte[48 * 24 * 3];
            converted.CopyPixels(pixels, 48 * 3, 0);
            var at = (12 * 48 + 36) * 3;
            check(frame.PixelWidth == 48 && frame.PixelHeight == 24 && Math.Abs(pixels[at] - 0x56) <= 3 &&
                Math.Abs(pixels[at + 1] - 0x34) <= 3 && Math.Abs(pixels[at + 2] - 0x12) <= 3,
                "Windows decoder validates SVG JPEG custom size and background");
        }
        var cancelAccess = new CountingAccess();
        await using var cancelled = new MainViewModel(worker, settings,
            new OutputPublisher(Path.Combine(scratch, "cancel-records"), new WindowsFileRecycler()), cancelAccess);
        cancelled.SvgSizeRequested += (_, target, _) =>
        {
            check(target == ImageFormat.Jpeg, "SVG JPEG cancel prompt retains requested target");
            return Task.FromResult<SvgConversionOptions?>(null);
        };
        cancelled.Admit(new(Guid.NewGuid(), "convert", "jpeg", [first]));
        await cancelled.WaitForIdleAsync();
        check(cancelAccess.Admissions == 0 && cancelled.Rows.Single().Result.State == OperationState.Cancelled &&
            Directory.EnumerateFiles(scratch, "*.jpg").Count() == 2,
            "SVG JPEG cancellation before background confirmation consumes no admission and publishes nothing");
    }

    public static async Task RunInterruptionAsync(string scratch, string executable, Action<bool, string> check)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use a fresh SVG interruption directory.");
        Directory.CreateDirectory(scratch);
        var path = Path.Combine(scratch, "large.svg");
        var shapes = string.Concat(Enumerable.Range(0, 1500).Select(index =>
            $"<circle cx=\"{index * 97 % 4096}\" cy=\"{index * 139 % 4096}\" r=\"24\" fill=\"#{index * 37 % 0xffffff:X6}\"/>"));
        await File.WriteAllTextAsync(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"4096\" height=\"4096\">" + shapes + "</svg>");
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var clock = new ImageInterruptionContracts.DeadlineClock();
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new WindowsFileRecycler());
        await using var worker = new WorkerClient(executable, Path.Combine(scratch, "worker"), clock);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var fault = new[] { "cancel", "crash", "timeout" }[attempt];
            var source = await worker.ProbeSvgAsync(new(Guid.NewGuid(), path), default);
            var outputId = Guid.NewGuid();
            var reservation = await publisher.ReserveAsync(new(outputId, path, "png", new("convert", new())));
            using var cancellation = new CancellationTokenSource();
            using var owned = Process.GetProcessById(worker.ProcessId!.Value);
            var cpu = owned.TotalProcessorTime;
            var conversion = worker.ConvertSvgAsync(new(source, new(), outputId, reservation.TemporaryPath), cancellation.Token);
            Process? native = null;
            var observed = Stopwatch.StartNew();
            while (!conversion.IsCompleted && observed.Elapsed < TimeSpan.FromSeconds(10))
            {
                owned.Refresh();
                if (native is null || native.HasExited)
                {
                    native?.Dispose();
                    native = PdfFailureContracts.FindChild(owned.Id,
                        Path.Combine(Path.GetDirectoryName(executable)!, "svg", "ContextSuite.Svg.exe"));
                }
                if (native is not null && !native.HasExited && owned.TotalProcessorTime - cpu >= TimeSpan.FromMilliseconds(30)) break;
                await Task.Delay(5);
            }
            owned.Refresh();
            check(!conversion.IsCompleted && native is { HasExited: false } &&
                owned.TotalProcessorTime - cpu >= TimeSpan.FromMilliseconds(30),
                "owned SVG renderer is live during worker activity before " + fault);
            if (fault == "cancel") cancellation.Cancel();
            else if (fault == "crash") owned.Kill(entireProcessTree: false);
            else
            {
                check(clock.Current!.Due == TimeSpan.FromSeconds(120), "SVG conversion retains the 120-second deadline");
                clock.Current.Expire();
            }
            try { await conversion.WaitAsync(TimeSpan.FromSeconds(15)); throw new InvalidOperationException("Interrupted SVG returned success."); }
            catch (OperationCanceledException) when (fault == "cancel")
            { check(true, "SVG cancellation remains cancellation"); }
            catch (MediaWorkerException error) when (fault != "cancel")
            {
                check(error.Failure == (fault == "crash" ? ImageFailure.WorkerTerminated : ImageFailure.TimedOut),
                    "SVG active fault returns a typed result: " + fault);
            }
            await native!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            check(native.HasExited && worker.ProcessId is null, "exact owned SVG renderer and worker exit after " + fault);
            native.Dispose();
            await publisher.AbandonAsync(reservation, fault == "cancel");
            check(!File.Exists(reservation.TemporaryPath) && !File.Exists(reservation.Record.OutputPath) &&
                SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(hash),
                "SVG active fault keeps source and publishes nothing: " + fault);
            await worker.ProbeSvgAsync(new(Guid.NewGuid(), path), default);
            check(worker.ProcessId != owned.Id, "SVG next request starts a fresh worker after " + fault);
        }
    }

    public static async Task RunWorkerAsync(string scratch, string executable, Action<bool, string> check)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use a fresh SVG workflow directory.");
        Directory.CreateDirectory(scratch);
        var first = Path.Combine(scratch, "first.svg");
        var second = Path.Combine(scratch, "second.svg");
        var raster = Path.Combine(scratch, "raster.bmp");
        const string art = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"12\"><rect width=\"24\" height=\"12\" fill=\"#e93652\"/></svg>";
        await File.WriteAllTextAsync(first, art);
        await File.WriteAllTextAsync(second, art.Replace("#e93652", "#127ac4", StringComparison.Ordinal));
        await File.WriteAllBytesAsync(raster, WindowsBmp());
        var firstHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(first)));
        var rasterHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(raster)));
        await using var worker = new WorkerClient(executable, Path.Combine(scratch, "workers"));
        check(worker.HasSvgConverter, "SVG workflow has the adjacent rendering engine");
        var access = new CountingAccess();
        var settings = new SuiteSettings { PlayCompletionSound = false };
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new WindowsFileRecycler());
        await using var view = new MainViewModel(worker, settings, publisher, access);
        var choices = 0;
        view.SvgSizeRequested += (sources, _, _) =>
        {
            choices++;
            check(sources.Count == 2, "one size decision covers both SVG files");
            // Change one source after probe; its reservation must refuse stale facts.
            File.AppendAllText(second, "\n");
            return Task.FromResult<SvgConversionOptions?>(new(48));
        };
        view.Admit(new(Guid.NewGuid(), "convert", "png", [raster, first, second]));
        await view.WaitForIdleAsync();
        var results = view.Rows.ToDictionary(row => row.Path);
        check(choices == 1 && access.Admissions == 1,
            "mixed raster and SVG PNG conversion uses one size choice and one admission");
        check(results[raster].Result.State == OperationState.Succeeded &&
            results[first].Result.State == OperationState.Succeeded &&
            results[second].Result.State == OperationState.Failed,
            "changed SVG fails alone while raster and unchanged SVG copies finish");
        check(results[raster].OutputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
            results[first].OutputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
            !Directory.EnumerateFiles(scratch, "second*.png").Any() &&
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(first))) == firstHash &&
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(raster))) == rasterHash,
            "only validated PNG copies publish and original bytes remain unchanged");
        check(view.CanRetry, "the failed SVG is eligible for a selective retry");
        view.RetryFailed();
        await view.WaitForIdleAsync();
        check(view.Rows.Last().SvgResumeBlocked && view.Rows.Last().Result.State == OperationState.Failed &&
            Directory.EnumerateFiles(scratch, "first*.png").Count() == 1 &&
            Directory.EnumerateFiles(scratch, "raster*.png").Count() == 1,
            "retry refuses changed source facts without duplicating successful copies");

        var cancelPath = Path.Combine(scratch, "cancel.svg");
        await File.WriteAllTextAsync(cancelPath, art);
        var noChoiceAccess = new CountingAccess();
        await using (var cancelled = new MainViewModel(worker, settings,
            new OutputPublisher(Path.Combine(scratch, "cancel-records"), new WindowsFileRecycler()), noChoiceAccess))
        {
            cancelled.SvgSizeRequested += (_, _, _) => Task.FromResult<SvgConversionOptions?>(null);
            cancelled.Admit(new(Guid.NewGuid(), "convert", "png", [raster, cancelPath]));
            await cancelled.WaitForIdleAsync();
            check(noChoiceAccess.Admissions == 0 && cancelled.Rows.All(row => row.Result.State == OperationState.Cancelled) &&
                !Directory.EnumerateFiles(scratch, "cancel*.png").Any(),
                "cancel before SVG size choice publishes no files from the mixed batch");
        }

        var transient = Path.Combine(scratch, "transient.svg");
        var companion = Path.Combine(scratch, "companion.svg");
        await File.WriteAllTextAsync(transient, art);
        await File.WriteAllTextAsync(companion, art);
        var transientAccess = new CountingAccess();
        var transientIo = new FailOnceIo();
        await using (var retryView = new MainViewModel(worker, settings,
            new OutputPublisher(Path.Combine(scratch, "transient-records"), new WindowsFileRecycler(), io: transientIo), transientAccess))
        {
            var decisions = 0;
            retryView.SvgSizeRequested += (_, _, _) => { decisions++; return Task.FromResult<SvgConversionOptions?>(new(48)); };
            retryView.Admit(new(Guid.NewGuid(), "convert", "png", [transient, companion]));
            await retryView.WaitForIdleAsync();
            check(retryView.Rows[0].Result.State == OperationState.Failed &&
                retryView.Rows[1].Result.State == OperationState.Succeeded && retryView.CanRetry,
                "one temporary publication failure leaves the other SVG copy committed");
            retryView.RetryFailed();
            await retryView.WaitForIdleAsync();
            check(decisions == 1 && retryView.Rows.Last().Result.State == OperationState.Succeeded &&
                Directory.EnumerateFiles(scratch, "transient*.png").Count() == 1 &&
                Directory.EnumerateFiles(scratch, "companion*.png").Count() == 1 && transientAccess.Admissions == 2,
                "unchanged failed SVG retries with captured width and no duplicate companion output");
        }

        var copyPath = Path.Combine(scratch, "copy.svg");
        await File.WriteAllTextAsync(copyPath, art);
        var copyHash = SHA256.HashData(await File.ReadAllBytesAsync(copyPath));
        await using (var copyView = new MainViewModel(worker,
            new SuiteSettings { Convert = new(ReplaceOriginals: true), PlayCompletionSound = false },
            new OutputPublisher(Path.Combine(scratch, "copy-records"), new WindowsFileRecycler()), new CountingAccess()))
        {
            copyView.SvgSizeRequested += (_, _, _) => Task.FromResult<SvgConversionOptions?>(new());
            copyView.Admit(new(Guid.NewGuid(), "convert", "png", [copyPath]));
            await copyView.WaitForIdleAsync();
            var row = copyView.Rows.Single();
            check(row.Result.State == OperationState.Succeeded && File.Exists(row.OutputPath) &&
                !string.Equals(row.OutputPath, copyPath, StringComparison.OrdinalIgnoreCase) &&
                SHA256.HashData(await File.ReadAllBytesAsync(copyPath)).SequenceEqual(copyHash),
                "SVG publishes a copy even when Settings allows replacing originals");
        }
    }

    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "svg-app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "drawing.svg");
        await File.WriteAllTextAsync(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"16\"/>");
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        var facts = new SvgSourceFacts(Guid.NewGuid(), path, hash, new FileInfo(path).Length, 32, 16,
            4096, 16_000_000, new string('A', 64), ImmutableArray.Create("Missing font was replaced with Arial."));
        var settings = new BatchSettings("convert", new ToolSettings(ReplaceOriginals: true));
        var plan = SvgConversionPlan.Create(Guid.NewGuid(), [facts], new(64), settings).Confirm();
        check(plan.Plan.Options.GetSize(facts) == new SvgPixelSize(64, 32),
            "SVG application plan captures a proportional custom size");
        check(settings.SelectOutput(false, false, false, false).Mode == OutputMode.SiblingCopy,
            "SVG conversion is a copy even when Settings allows replacement");

        var row = new FileRow(1, "convert", path, settings, "png");
        check(row.BeginSvg(facts), "SVG row accepts the first worker probe");
        row.RememberSvgOptions(new(64));
        var retry = new FileRow(2, "convert", path, settings, "png");
        retry.ResumeSvgFrom(row);
        check(retry.SvgOptions?.Width == 64 && retry.BeginSvg(facts),
            "SVG retry retains the confirmed width and source facts");
        var changedFont = facts with { FontFingerprint = new string('B', 64) };
        check(!retry.BeginSvg(changedFont) && retry.SvgResumeBlocked && retry.Result.State == OperationState.Failed,
            "SVG retry refuses a changed font state and asks for a fresh command");

        await using var worker = new WorkerClient(Path.Combine(root, "ContextSuite.Worker.exe"));
        check(!worker.HasSvgConverter, "SVG availability requires the adjacent rendering engine");
        await using var view = new MainViewModel(worker, new SuiteSettings { Convert = new(ReplaceOriginals: true) },
            new OutputPublisher(Path.Combine(root, "records"), null!), new LocalTrialStore(Path.Combine(root, "trial.json")));
        view.Admit(new(Guid.NewGuid(), "convert", "png", [path]));
        await view.WaitForIdleAsync();
        check(view.Rows.Single().Result.State == OperationState.Unsupported && worker.ProcessId is null &&
            !Directory.EnumerateFiles(root, "*.png").Any() &&
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))) == hash,
            "SVG direct PNG refuses a missing engine without worker launch or output");
    }

    private static byte[] WindowsBmp()
    {
        const int side = 16;
        var pixels = new byte[side * side * 3];
        for (var i = 0; i < pixels.Length; i += 3) { pixels[i] = 64; pixels[i + 1] = 120; pixels[i + 2] = 220; }
        var image = BitmapSource.Create(side, side, 96, 96, PixelFormats.Bgr24, null, pixels, side * 3);
        image.Freeze();
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private sealed class CountingAccess : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) =>
            Task.FromResult(new OperationAccessStatus(true, "Available"));
        public Task<OperationAdmission> AdmitConversionAsync(ContextSuite.Core.Images.ConfirmedImageBatch confirmed, CancellationToken token)
        {
            Admissions++;
            return Task.FromResult(new OperationAdmission(new(true, "Available"), confirmed.Plan.BatchId));
        }
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedSvgConversion confirmed, CancellationToken token)
        {
            Admissions++;
            return Task.FromResult(new OperationAdmission(new(true, "Available"), confirmed.Plan.BatchId));
        }
        public Task<OperationAdmission> AdmitOptimizationAsync(ContextSuite.Core.Images.ConfirmedPngOptimization confirmed, CancellationToken token) =>
            throw new NotSupportedException();
    }

    private sealed class FailOnceIo : PublicationIo
    {
        private bool _failed;
        public override void Move(string source, string destination)
        {
            if (!_failed && Path.GetFileName(destination).StartsWith("transient", StringComparison.OrdinalIgnoreCase))
            {
                _failed = true;
                throw new IOException("Injected one-time copy publication failure.");
            }
            base.Move(source, destination);
        }
    }
}
