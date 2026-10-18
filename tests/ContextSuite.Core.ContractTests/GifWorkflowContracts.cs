using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Gif;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Transport;

namespace ContextSuite.Core.ContractTests;

internal static class GifWorkflowContracts
{
    internal static void Models(string root, Action<bool, string> check)
    {
        var source = new GifSourceFacts(Guid.NewGuid(), Path.Combine(root, "animation.gif"), new string('A', 64), 100,
            4, 2, 4, [2, 3, 4, 4], 0);
        var plan = GifConversionPlan.Create(Guid.NewGuid(), [source], new("convert", new(ReplaceOriginals: true))).Confirm();
        check(plan.Plan.Settings.SelectOutput(false, false, false, false).Mode == OutputMode.SiblingCopy,
            "GIF all-frame plan remains copy-only with overwrite settings");
        check(OutputNames.Create(source.Path, "convert", "png", frameNumber: 2) == "animation - Frame 0002.png",
            "GIF numbered output preserves frame order in filename sorting");
        Reject(() => OutputNames.Create(source.Path, "convert", "jpg", frameNumber: 1), "GIF JPEG target");
        Reject(() => OutputNames.Create(source.Path, "convert", "png", frameNumber: 1, replaceSource: true), "GIF replacement");
        Reject(() => (source with { FrameCount = 4097 }).Validate(), "excessive frame count");
        Reject(() => (source with { DelayCentiseconds = [2] }).Validate(), "incomplete timing facts");
        Reject(() => (source with { Width = 40000 }).Validate(), "excessive canvas");
        Reject(() => GifConversionPlan.Create(Guid.NewGuid(), [source, source], plan.Plan.Settings), "repeated source");
        var probe = new WorkerCommand(1, Guid.NewGuid(), "gif-probe", GifProbe: new(source.ItemId, source.Path));
        probe.Validate();
        new WorkerCommand(1, Guid.NewGuid(), "gif-release").Validate();
        Reject(() => (probe with { Command = "svg-probe" }).Validate(), "GIF data on SVG command");
        Reject(() => (probe with { SvgProbe = new(source.ItemId, source.Path) }).Validate(), "mixed GIF/SVG payload");
        Reject(() => (probe with { Command = "gif-release" }).Validate(), "source on release command");
        var id = Guid.NewGuid();
        var work = new GifWork(source, 0, id, Path.Combine(root, $".context-suite-{id:N}.tmp"));
        work.Validate();
        Reject(() => (work with { FrameIndex = 4 }).Validate(), "frame outside the source");
        Reject(() => (work with { TemporaryPath = source.Path }).Validate(), "output overwrites source");
        var row = new FileRow(1, "convert", source.Path, plan.Plan.Settings, "png");
        row.BeginGif(source);
        var recovery = Path.Combine(root, "earlier-recovery.json");
        row.ApplyGifFrame(0, new(source.Path, OperationState.Failed, "Publication interrupted",
            new(source.Path, PublicationOutcome.Failed, "Publication interrupted", RecoveryRecordPath: recovery)));
        var retry = new FileRow(2, "convert", source.Path, plan.Plan.Settings, "png");
        retry.ResumeGifFrom(row);
        retry.BeginGif(source with { ItemId = retry.ItemId });
        check(retry.ResultDetails.Contains(recovery, StringComparison.Ordinal), "GIF retry retains earlier recovery record links");
        void Reject(Action action, string label)
        {
            try { action(); }
            catch (InvalidDataException) { check(true, "GIF model refuses " + label); return; }
            throw new InvalidOperationException("GIF model accepted " + label);
        }
    }

    internal static async Task RunAsync(string root, string executable, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use a fresh GIF workflow directory.");
        Directory.CreateDirectory(root);
        Models(root, check);
        var animated = Path.Combine(root, "animated.gif");
        var still = Path.Combine(root, "still.GIF");
        File.WriteAllBytes(animated, Fixture());
        File.WriteAllBytes(still, Fixture(single: true));
        var original = SHA256.HashData(File.ReadAllBytes(animated));
        var collision = Path.Combine(root, "animated - Frames");
        Directory.CreateDirectory(collision);
        File.WriteAllText(Path.Combine(collision, "existing.txt"), "keep");
        var settings = new SuiteSettings { Convert = new(ReplaceOriginals: true), PlayCompletionSound = false };
        var access = new CountingAccess();
        await using var worker = new WorkerClient(executable, Path.Combine(root, "worker"));
        await using var view = new MainViewModel(worker, settings,
            new OutputPublisher(Path.Combine(root, "records"), new NoRecycle(), io: new FailFrameOnce()), access);
        view.ConversionRequested += (_, _) => throw new InvalidOperationException("GIF has no first-frame picker or routine planner.");
        view.Admit(new(Guid.NewGuid(), "convert", "png", [animated, still]));
        await view.WaitForIdleAsync();
        var row = view.Rows.First();
        var folder = row.GifFolder!.Path;
        check(access.Admissions == 1 && folder == Path.Combine(root, "animated - Frames (2)") &&
            File.ReadAllText(Path.Combine(collision, "existing.txt")) == "keep",
            "GIF atomically reserves a separate collision-safe frame folder with one batch admission");
        check(row.SavedGifFrames > 0 && row.SavedGifFrames < 4 && row.Result.PartialOutput && view.CanRetry,
            "GIF partial publication retains completed frames and offers retry");
        check(view.Rows[1].Result.State == OperationState.Succeeded && view.Rows[1].GifFolder is null &&
            File.Exists(Path.Combine(root, "still - Converted.png")), "single-frame GIF publishes one PNG without a frame folder");
        var completed = Directory.GetFiles(folder, "*.png").ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        view.RetryFailed();
        await view.WaitForIdleAsync();
        row = view.Rows.Last();
        check(row.Result.State == OperationState.Succeeded && row.SavedGifFrames == 4 && row.GifFolder!.Path == folder &&
            Directory.GetFiles(folder, "*.png").Length == 4 && Directory.GetDirectories(root, "animated - Frames*").Length == 2 &&
            completed.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)),
            "GIF retry reuses its folder and adds only unfinished frame files");
        check(access.Admissions == 2 && SHA256.HashData(File.ReadAllBytes(animated)).SequenceEqual(original),
            "GIF retry has one fresh admission and preserves the original despite overwrite settings");
        var frames = Directory.GetFiles(folder, "*.png").OrderBy(path => path, StringComparer.Ordinal).Select(Decode).ToArray();
        check(frames[0].Length == 32 && frames[0][2] == 255 && frames[1][4] == 255 && frames[1][6] == 0,
            "GIF frames contain full canvases and composited local frame offsets");
        check(frames[2][7] == 0 && frames[2][11] == 0 && frames[2][17] == 255 && frames[2].SequenceEqual(frames[3]),
            "GIF disposal clears the prior rectangle and duplicate displayed frames remain in order");
        check(row.Result.Message.Contains("playback timing", StringComparison.Ordinal) && !Directory.EnumerateFiles(folder, "*.json").Any(),
            "GIF result explains timing loss without an unrequested timing manifest");

        var changedPath = Path.Combine(root, "changed.gif");
        File.WriteAllBytes(changedPath, Fixture());
        await using var changed = new MainViewModel(worker, settings,
            new OutputPublisher(Path.Combine(root, "changed-records"), new NoRecycle(), io: new FailFrameOnce()), new CountingAccess());
        changed.Admit(new(Guid.NewGuid(), "convert", "png", [changedPath]));
        await changed.WaitForIdleAsync();
        var changedFolder = changed.Rows[0].GifFolder!.Path;
        var savedCount = Directory.GetFiles(changedFolder, "*.png").Length;
        File.WriteAllBytes(changedPath, Fixture(single: true));
        changed.RetryFailed();
        await changed.WaitForIdleAsync();
        check(changed.Rows.Last().GifResumeBlocked && Directory.GetFiles(changedFolder, "*.png").Length == savedCount &&
            Directory.GetDirectories(root, "changed - Frames*").Length == 1,
            "GIF retry refuses changed sources while retaining the existing frame folder");

        foreach (var tamper in new[] { false, true })
        {
            var label = tamper ? "altered-frame" : "deleted-frame";
            var input = Path.Combine(root, label + ".gif");
            File.WriteAllBytes(input, Fixture());
            await using var integrity = new MainViewModel(worker, settings,
                new OutputPublisher(Path.Combine(root, label + "-records"), new NoRecycle(), io: new FailFrameOnce()), new CountingAccess());
            integrity.Admit(new(Guid.NewGuid(), "convert", "png", [input]));
            await integrity.WaitForIdleAsync();
            var firstAttempt = integrity.Rows[0];
            check(firstAttempt.SavedGifFrames == 3 && integrity.CanRetry,
                "GIF " + label + ": first attempt saved three frames around the injected publication fault");
            var framePath = firstAttempt.GifFrames[0].Publication!.OutputPath!;
            var frameBytes = File.ReadAllBytes(framePath);
            var otherFrames = Directory.GetFiles(firstAttempt.GifFolder!.Path, "*.png")
                .Where(path => path != framePath).ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
            if (tamper)
            {
                frameBytes[^1] ^= 1;
                File.WriteAllBytes(framePath, frameBytes);
            }
            else File.Delete(framePath);
            integrity.RetryFailed();
            await integrity.WaitForIdleAsync();
            var lastAttempt = integrity.Rows.Last();
            check(lastAttempt.GifResumeBlocked && !integrity.CanRetry && lastAttempt.Result.State == OperationState.Failed &&
                lastAttempt.SavedGifFrames == 2 && lastAttempt.GifFrames[0].Publication?.IsCommitted == false &&
                Directory.GetFiles(firstAttempt.GifFolder!.Path, "*.png").Length == (tamper ? 3 : 2) &&
                otherFrames.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)) &&
                (!tamper || File.ReadAllBytes(framePath).SequenceEqual(frameBytes)) &&
                SHA256.HashData(File.ReadAllBytes(input)).SequenceEqual(original),
                "GIF " + label + ": retry rejects a missing or same-size altered frame without publishing more output");
        }

        await InterruptedAsync(false);
        await InterruptedAsync(true);

        var replacedPath = Path.Combine(root, "replaced-folder.gif");
        File.WriteAllBytes(replacedPath, Fixture());
        await using var replaced = new MainViewModel(worker, settings,
            new OutputPublisher(Path.Combine(root, "replaced-records"), new NoRecycle(), io: new FailFrameOnce()), new CountingAccess());
        replaced.Admit(new(Guid.NewGuid(), "convert", "png", [replacedPath]));
        await replaced.WaitForIdleAsync();
        var replacedFolder = replaced.Rows[0].GifFolder!.Path;
        var retainedFolder = replacedFolder + " retained";
        Directory.Move(replacedFolder, retainedFolder);
        Directory.CreateDirectory(replacedFolder);
        File.WriteAllText(Path.Combine(replacedFolder, "unrelated.txt"), "keep");
        replaced.RetryFailed();
        await replaced.WaitForIdleAsync();
        check(replaced.Rows.Last().GifResumeBlocked && !replaced.CanRetry &&
            Directory.GetFiles(replacedFolder).Length == 1 && Directory.GetFiles(retainedFolder, "*.png").Length == 3,
            "GIF retry refuses a substituted folder without touching unrelated or previously saved files");

        var bitmapPath = Path.Combine(root, "mixed.bmp");
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgr24, null,
            new byte[] { 0, 0, 255, 255, 0, 0 }, 6)));
        using (var bitmap = File.Create(bitmapPath)) encoder.Save(bitmap);
        var mixedAccess = new CountingAccess();
        await using var mixed = new MainViewModel(worker, new SuiteSettings { PlayCompletionSound = false },
            new OutputPublisher(Path.Combine(root, "mixed-records"), new NoRecycle()), mixedAccess);
        mixed.Admit(new(Guid.NewGuid(), "convert", "png", [still, bitmapPath]));
        await mixed.WaitForIdleAsync();
        check(mixedAccess.Admissions == 1 && mixed.Rows.All(item => item.Result.State == OperationState.Succeeded),
            "GIF and ordinary image conversion share one access admission in a mixed batch");

        async Task InterruptedAsync(bool terminateWorker)
        {
            var label = terminateWorker ? "worker-exit" : "cancelled";
            var input = Path.Combine(root, label + ".gif");
            File.WriteAllBytes(input, Fixture());
            var snapshot = new BatchSettings("convert", settings.Convert);
            var interruptedRow = new FileRow(1, "convert", input, snapshot, "png");
            var facts = await worker.ProbeGifAsync(new(interruptedRow.ItemId, input), CancellationToken.None);
            interruptedRow.BeginGif(facts);
            var batch = GifConversionPlan.Create(Guid.NewGuid(), [facts], snapshot).Confirm();
            var publisher = new OutputPublisher(Path.Combine(root, label + "-records"), new NoRecycle());
            var executor = new GifConversionExecutor(worker, publisher);
            using var cancellation = new CancellationTokenSource();
            var injected = false;
            await executor.ExecuteAdmittedAsync(batch, new(new(true, "Available"), batch.Plan.BatchId),
                new Dictionary<Guid, FileRow> { [interruptedRow.ItemId] = interruptedRow }, current =>
                {
                    if (injected || current.SavedGifFrames != 1) return;
                    injected = true;
                    if (terminateWorker)
                    {
                        using var process = System.Diagnostics.Process.GetProcessById(worker.ProcessId!.Value);
                        process.Kill(true);
                    }
                    else cancellation.Cancel();
                }, cancellation.Token);
            var outputFolder = interruptedRow.GifFolder!.Path;
            var saved = Directory.GetFiles(outputFolder, "*.png").Single();
            var savedHash = SHA256.HashData(File.ReadAllBytes(saved));
            check(interruptedRow.SavedGifFrames == 1 && interruptedRow.Result.PartialOutput &&
                interruptedRow.Result.State == (terminateWorker ? OperationState.Failed : OperationState.Cancelled) &&
                !Directory.EnumerateFiles(outputFolder, "*.tmp").Any(),
                "GIF " + label + " retains committed frames and abandons unfinished reservations");
            using (File.Open(input, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            check(true, "GIF " + label + " releases its worker source lease");
            var resumed = new FileRow(2, "convert", input, snapshot, "png");
            resumed.ResumeGifFrom(interruptedRow);
            var resumedFacts = await worker.ProbeGifAsync(new(resumed.ItemId, input), CancellationToken.None);
            resumed.BeginGif(resumedFacts);
            var resumedPlan = GifConversionPlan.Create(Guid.NewGuid(), [resumedFacts], snapshot).Confirm();
            await executor.ExecuteAdmittedAsync(resumedPlan, new(new(true, "Available"), resumedPlan.Plan.BatchId),
                new Dictionary<Guid, FileRow> { [resumed.ItemId] = resumed }, null, CancellationToken.None);
            check(resumed.Result.State == OperationState.Succeeded && resumed.SavedGifFrames == 4 &&
                resumed.GifFolder!.Path == outputFolder && Directory.GetFiles(outputFolder, "*.png").Length == 4 &&
                SHA256.HashData(File.ReadAllBytes(saved)).SequenceEqual(savedHash),
                "GIF " + label + " retries only unfinished frames in its original folder");
        }
    }

    internal static byte[] Fixture(bool single = false)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write("GIF89a"u8); writer.Write((ushort)4); writer.Write((ushort)2);
        writer.Write(new byte[] { 0x81, 0, 0, 0, 0, 0, 255, 0, 0, 0, 0, 255, 0, 255, 0 });
        Frame(0, 0, 4, 2, 1, 1, false);
        if (!single)
        {
            Frame(1, 0, 2, 1, 2, 2, true);
            Frame(0, 1, 1, 1, 3, 1, false);
            Frame(0, 1, 1, 1, 3, 1, false);
        }
        writer.Write((byte)0x3b);
        return output.ToArray();
        void Frame(ushort x, ushort y, ushort width, ushort height, byte color, byte disposal, bool transparent)
        {
            writer.Write(new byte[] { 0x21, 0xf9, 4, (byte)(disposal * 4 + (transparent ? 1 : 0)), 2, 0, 0, 0, 0x2c });
            writer.Write(x); writer.Write(y); writer.Write(width); writer.Write(height); writer.Write((byte)0); writer.Write((byte)2);
            var codes = Enumerable.Range(0, width * height).SelectMany(_ => new[] { 4, (int)color }).Append(5).ToArray();
            var data = new byte[(codes.Length * 3 + 7) / 8];
            for (var i = 0; i < codes.Length; i++)
            for (var bit = 0; bit < 3; bit++)
                data[(i * 3 + bit) / 8] |= (byte)(((codes[i] >> bit) & 1) << ((i * 3 + bit) % 8));
            writer.Write((byte)data.Length); writer.Write(data); writer.Write((byte)0);
        }
    }

    private static byte[] Decode(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        if (frame.PixelWidth != 4 || frame.PixelHeight != 2) throw new InvalidDataException("GIF output is not a full displayed canvas.");
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[32]; converted.CopyPixels(pixels, 16, 0); return pixels;
    }
    private sealed class FailFrameOnce : PublicationIo
    {
        private bool _failed;
        public override void Move(string source, string destination)
        {
            if (!_failed && destination.EndsWith("Frame 0002.png", StringComparison.Ordinal))
            { _failed = true; throw new IOException("Injected frame publication failure."); }
            base.Move(source, destination);
        }
    }
    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("GIF extraction must not recycle originals.");
    }
    private sealed class CountingAccess : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => Task.FromResult(new OperationAccessStatus(true, "Available"));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedGifConversion confirmed, CancellationToken token)
        {
            Admissions++; return Task.FromResult(new OperationAdmission(new(true, "Available"), confirmed.Plan.BatchId));
        }
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token)
        {
            Admissions++; return Task.FromResult(new OperationAdmission(new(true, "Available"), confirmed.Plan.BatchId));
        }
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new NotSupportedException();
    }
}
