using System.Buffers.Binary;
using System.IO.Compression;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Transport;

namespace ContextSuite.Core.ContractTests;

internal static class ImageWorkerContracts
{
    public static async Task RunAsync(string scratch, string executable, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "image-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "colors.png");
        WritePng(path);
        var original = SHA256.HashData(File.ReadAllBytes(path));
        await using var worker = new WorkerClient(executable, Path.Combine(root, "scratch"));
        var source = await worker.ProbeAsync(new(Guid.NewGuid(), path), CancellationToken.None);
        check(source.Format == ImageFormat.Png && source.Width == 3 && source.Height == 2 && !source.HasTransparency,
            "image IPC: native probe of independent PNG fixture");
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => worker.ProbeAsync(new(Guid.NewGuid(), path), CancellationToken.None)));
        check(concurrent.Select(s => s.ItemId).Distinct().Count() == 3, "image IPC: concurrent callers serialize without crossed replies");
        var preview = await worker.PreviewAsync(new(source), CancellationToken.None);
        check(preview.Width == 3 && preview.Height == 2 && preview.BgraPixels[2] == 255 && preview.BgraPixels[0] == 0,
            "image IPC: bounded raw preview pixels roundtrip");
        var alphaPath = Path.Combine(root, "transparent.png");
        WritePng(alphaPath, true);
        await ConversionViewModelContracts.RunAsync(root, worker, await worker.ProbeAsync(new(Guid.NewGuid(), alphaPath), CancellationToken.None), check);
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle());
        foreach (var target in new[] { ImageFormat.Jpeg, ImageFormat.WebP, ImageFormat.Bmp, ImageFormat.Tga })
        {
            var options = new ImageConversionOptions(target);
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [source], options, new("convert", new()));
            var confirmed = plan.Confirm(true, false, false);
            var reservation = await publisher.ReserveAsync(new(source.ItemId, path, options.Extension, plan.Settings));
            var result = await worker.ConvertAsync(new(confirmed.Plan.Items[0], options, reservation.TemporaryPath), CancellationToken.None);
            check(!File.Exists(reservation.Record.OutputPath), "image IPC: worker cannot publish " + target);
            var published = await publisher.PublishAsync(reservation, result.Validation);
            check(published.Outcome == PublicationOutcome.CopyCreated && File.Exists(published.OutputPath) &&
                SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(original), "image IPC: app publishes validated " + target + " and preserves original");
            var output = await worker.ProbeAsync(new(Guid.NewGuid(), published.OutputPath!), CancellationToken.None);
            check(output.Format == target && output.Width == 3 && output.Height == 2, "image IPC: published output independently re-probed " + target);
            source = source with { ItemId = Guid.NewGuid() };
        }
        var process = worker.ProcessId;
        var corrupt = Path.Combine(root, "corrupt.png");
        File.WriteAllText(corrupt, "not a supported image");
        try { await worker.ProbeAsync(new(Guid.NewGuid(), corrupt), CancellationToken.None); throw new InvalidOperationException("Corrupt file was accepted"); }
        catch (MediaWorkerException error) { check(error.Failure == ImageFailure.UnsupportedInput, "image IPC: unknown signature returns typed unsupported failure"); }
        var truncated = Path.Combine(root, "truncated.png");
        File.WriteAllBytes(truncated, []);
        try { await worker.ProbeAsync(new(Guid.NewGuid(), truncated), CancellationToken.None); throw new InvalidOperationException("Empty file was accepted"); }
        catch (MediaWorkerException error) { check(error.Failure == ImageFailure.InvalidInput, "image IPC: empty input is invalid, not an engine crash"); }
        try { await worker.ProbeAsync(new(Guid.NewGuid(), Path.Combine(root, "missing.png")), CancellationToken.None); throw new InvalidOperationException("Missing file was accepted"); }
        catch (MediaWorkerException error) { check(error.Failure == ImageFailure.FileAccess, "image IPC: missing source has a file-access failure"); }
        await worker.ProbeAsync(new(Guid.NewGuid(), path), CancellationToken.None);
        check(worker.ProcessId == process, "image IPC: later item succeeds in same worker after damaged input");
        var stale = source with { Sha256 = new string('0', 64) };
        var invalidPlan = ImageConversionPlanner.Create(Guid.NewGuid(), [stale], new(ImageFormat.WebP), new("convert", new())).Items[0];
        var failed = await publisher.ReserveAsync(new(stale.ItemId, path, "webp", new("convert", new())));
        try { await worker.ConvertAsync(new(invalidPlan, new(ImageFormat.WebP), failed.TemporaryPath), CancellationToken.None); throw new InvalidOperationException("Stale plan was accepted"); }
        catch (MediaWorkerException error) { check(error.Failure == ImageFailure.SourceChanged, "image IPC: stale source digest has a distinct source-changed failure"); }
        var abandoned = await publisher.AbandonAsync(failed, false);
        check(abandoned.Outcome == PublicationOutcome.Failed && !File.Exists(failed.TemporaryPath) && !File.Exists(failed.Record.OutputPath),
            "image IPC: failed worker output abandoned without publication");

        var faultSources = Enumerable.Range(0, 3).Select(_ => source with { ItemId = Guid.NewGuid() }).ToArray();
        var faultPlan = ImageConversionPlanner.Create(Guid.NewGuid(), faultSources, new(ImageFormat.WebP), new("convert", new()));
        var faultTrial = new LocalTrialStore(Path.Combine(root, "fault-trial", "trial.json"), new TestClock());
        var faultExecutor = new ImageBatchExecutor(worker, publisher, faultTrial);
        var previousOutputs = Directory.GetFiles(root, "* - Converted*.*").ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
        var killed = false;
        var crashed = await faultExecutor.ExecuteAsync(faultPlan.Confirm(true, false, false), (item, result) =>
        {
            if (!killed && result.Message == "Encoding and validating output")
            {
                // Controlled boundary fault: plant partial bytes in this reservation and kill only
                // the owned real worker immediately before dispatch. No production fault switches.
                File.WriteAllText(Path.Combine(root, $".context-suite-{item.Source.ItemId:N}.tmp"), "incomplete image output");
                using var owned = Process.GetProcessById(worker.ProcessId!.Value);
                owned.Kill(true);
                owned.WaitForExit(5000);
                killed = true;
            }
        }, CancellationToken.None);
        check(killed && crashed.Results[0].State == OperationState.Failed && crashed.Results.Skip(1).All(r => r.State == OperationState.Succeeded),
            "image orchestration: dead worker fails current item once, pending items restart and convert");
        check(!File.Exists(Path.Combine(root, $".context-suite-{faultSources[0].ItemId:N}.tmp")) &&
            previousOutputs.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)),
            "image orchestration: partial output abandoned after worker death; earlier published files unchanged");

        using var dispatchCancellation = new CancellationTokenSource();
        var cancelSources = faultSources.Select(s => s with { ItemId = Guid.NewGuid() }).ToArray();
        var cancelPlan = ImageConversionPlanner.Create(Guid.NewGuid(), cancelSources, new(ImageFormat.WebP), new("convert", new()));
        var cancelledAtDispatch = await faultExecutor.ExecuteAsync(cancelPlan.Confirm(true, false, false), (item, result) =>
        {
            if (item.Source.ItemId == cancelSources[1].ItemId && result.Message == "Encoding and validating output")
            {
                File.WriteAllText(Path.Combine(root, $".context-suite-{item.Source.ItemId:N}.tmp"), "incomplete cancelled output");
                dispatchCancellation.Cancel();
            }
        }, dispatchCancellation.Token);
        check(cancelledAtDispatch.Results[0].State == OperationState.Succeeded &&
            cancelledAtDispatch.Results.Skip(1).All(r => r.State == OperationState.Cancelled) &&
            !File.Exists(Path.Combine(root, $".context-suite-{cancelSources[1].ItemId:N}.tmp")),
            "image orchestration: cancellation at dispatch retains success, abandons partial output and cancels pending items");
        check(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(original), "image orchestration: fault-injected batches preserve source bytes");

        var clock = new TestClock();
        var trialPath = Path.Combine(root, "isolated-trial", "trial.json");
        var trial = new LocalTrialStore(trialPath, clock);
        var executor = new ImageBatchExecutor(worker, publisher, trial);
        var batchPlan = ImageConversionPlanner.Create(Guid.NewGuid(),
            [source with { ItemId = Guid.NewGuid() }, source with { ItemId = Guid.NewGuid() }],
            new(ImageFormat.WebP, WebPLossless: true), new("convert", new()));
        check(!File.Exists(trialPath), "image orchestration: probing/planning/preview do not create trial data");
        var execution = await executor.ExecuteAsync(batchPlan.Confirm(true, false, false), (_, result) =>
        {
            if (result.State == OperationState.Succeeded) clock.Utc += TimeSpan.FromHours(73);
        }, CancellationToken.None);
        check(execution.Admission.IsAllowed && execution.Results.All(r => r.State == OperationState.Succeeded) && File.Exists(trialPath),
            "image orchestration: first confirmation persists isolated trial, admitted batch finishes after expiry");
        var beforeExpired = Directory.GetFiles(root).Length;
        var expired = await executor.ExecuteAsync(batchPlan.Confirm(true, false, false), null, CancellationToken.None);
        check(!expired.Admission.IsAllowed && expired.Results.All(r => r.State == OperationState.Failed) && Directory.GetFiles(root).Length == beforeExpired,
            "image orchestration: expired admission creates no output or reservation");
        var cancelledTrial = Path.Combine(root, "cancelled-trial", "trial.json");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await new ImageBatchExecutor(worker, publisher, new LocalTrialStore(cancelledTrial, clock))
                .ExecuteAsync(batchPlan.Confirm(true, false, false), null, cancelled.Token);
            throw new InvalidOperationException("Cancelled admission executed");
        }
        catch (OperationCanceledException) { check(!File.Exists(cancelledTrial), "image orchestration: cancellation before admission does not start trial"); }
    }

    // Independent test fixture encoder. No private engine or image library is linked into the public tests.
    internal static void WritePng(string path, bool transparent = false, CompressionLevel compression = CompressionLevel.SmallestSize, bool fdEC = false)
    {
        using var stream = File.Create(path);
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 3);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 2);
        header[8] = 8; header[9] = 6;
        Chunk("IHDR", header);
        if (fdEC) Chunk("fdEC", [0x52, 0x24, 0x93, 0xE3, 0]);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, compression, true))
        {
            byte[] pixels = [0, 255,0,0,255, 0,255,0,255, 0,0,255,255, 0, 255,255,255,255, 0,0,0,255, 255,0,255,255];
            if (transparent) pixels[4] = 128;
            zlib.Write(pixels);
        }
        Chunk("IDAT", compressed.ToArray());
        Chunk("IEND", []);
        void Chunk(string name, byte[] data)
        {
            var type = Encoding.ASCII.GetBytes(name);
            Span<byte> value = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(value, (uint)data.Length);
            stream.Write(value); stream.Write(type); stream.Write(data);
            uint crc = uint.MaxValue;
            foreach (var item in type.Concat(data))
            {
                crc ^= item;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0);
            }
            BinaryPrimitives.WriteUInt32BigEndian(value, ~crc);
            stream.Write(value);
        }
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Copy-only image integration must never recycle.");
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => 0;
    }
}
