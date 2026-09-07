using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

namespace ContextSuite.Core.ContractTests;

internal static class ImageInterruptionContracts
{
    public static async Task RunAsync(string scratch, string executable, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "native-interruption-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "noise.png");
        WriteNoisePng(sourcePath);
        var originalHash = SHA256.HashData(File.ReadAllBytes(sourcePath));
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle());
        var clock = new DeadlineClock();
        await using var worker = new WorkerClient(executable, Path.Combine(root, "scratch"), clock);
        foreach (var fault in new[] { "cancel", "crash", "timeout" })
        {
            var source = await worker.ProbeAsync(new(Guid.NewGuid(), sourcePath), CancellationToken.None);
            var options = new ImageConversionOptions(ImageFormat.WebP, Quality: 100);
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [source], options, new("convert", new())).Items[0];
            var reservation = await publisher.ReserveAsync(new(source.ItemId, source.Path, "webp", new("convert", new())));
            using var cancellation = new CancellationTokenSource();
            var conversion = worker.ConvertAsync(new(plan, options, reservation.TemporaryPath), cancellation.Token);
            var wait = Stopwatch.StartNew();
            var encoding = false;
            while (!conversion.IsCompleted && wait.Elapsed < TimeSpan.FromSeconds(20))
            {
                try
                {
                    using var probe = File.Open(reservation.TemporaryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                }
                catch (IOException error) when ((error.HResult & 0xffff) == 32) { encoding = true; break; }
                await Task.Delay(10);
            }
            if (!encoding)
            {
                cancellation.Cancel();
                try { await conversion; } catch (Exception) { }
                await publisher.AbandonAsync(reservation, true);
                throw new InvalidOperationException("Did not observe the real encoder holding its reserved output: " + fault);
            }
            var processId = worker.ProcessId!.Value;
            check(!conversion.IsCompleted, "native interruption: encoder owns reserved output while request is active: " + fault);
            using (var activeEncoder = Process.GetProcessById(processId))
            {
                var cpu = activeEncoder.TotalProcessorTime;
                await Task.Delay(75);
                activeEncoder.Refresh();
                var stillLocked = false;
                try { using var probe = File.Open(reservation.TemporaryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
                catch (IOException error) when ((error.HResult & 0xffff) == 32) { stillLocked = true; }
                check(!conversion.IsCompleted && stillLocked && activeEncoder.TotalProcessorTime > cpu,
                    "native interruption: encoder consumes CPU while holding output, before fault: " + fault);
            }
            if (fault == "cancel") cancellation.Cancel();
            else if (fault == "timeout")
            {
                check(clock.Current!.Due == TimeSpan.FromSeconds(120), "native interruption: production conversion deadline is 120 seconds");
                clock.Current.Expire(); // Controlled clock, real worker and native encoder; no shipping fault switch.
            }
            else
            {
                using var owned = Process.GetProcessById(processId);
                owned.Kill(true);
            }
            try { await conversion.WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Interrupted encoder returned success"); }
            catch (OperationCanceledException) when (fault == "cancel") { check(true, "native interruption: user cancellation remains cancellation"); }
            catch (MediaWorkerException error) when (fault != "cancel")
            {
                check(error.Failure == (fault == "timeout" ? ImageFailure.TimedOut : ImageFailure.WorkerTerminated),
                    "native interruption: distinct typed failure: " + fault);
            }
            var result = await publisher.AbandonAsync(reservation, fault == "cancel");
            check(result.Outcome == (fault == "cancel" ? PublicationOutcome.Cancelled : PublicationOutcome.Failed) &&
                !File.Exists(reservation.TemporaryPath) && !File.Exists(reservation.Record.OutputPath) &&
                SHA256.HashData(File.ReadAllBytes(sourcePath)).SequenceEqual(originalHash),
                "native interruption: no incomplete publication or changed source: " + fault);
            await worker.ProbeAsync(new(Guid.NewGuid(), sourcePath), CancellationToken.None);
            check(worker.ProcessId != processId, "native interruption: subsequent request starts a fresh worker: " + fault);
        }
    }

    private static void WriteNoisePng(string path)
    {
        using var file = File.Create(path);
        file.Write(new byte[] { 137,80,78,71,13,10,26,10 });
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 4096);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 4096);
        header[8] = 8; header[9] = 2;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            var random = new Random(7429);
            var row = new byte[4096 * 3 + 1];
            for (var y = 0; y < 4096; y++) { random.NextBytes(row); row[0] = 0; zlib.Write(row); }
        }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []);
        void Chunk(string name, byte[] bytes)
        {
            var type = Encoding.ASCII.GetBytes(name);
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(number, (uint)bytes.Length);
            file.Write(number); file.Write(type); file.Write(bytes);
            uint crc = uint.MaxValue;
            foreach (var value in type.Concat(bytes))
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
            }
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); file.Write(number);
        }
    }

    private sealed class DeadlineClock : TimeProvider
    {
        public DeadlineTimer? Current { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Current = new(callback, state, dueTime);
            return Current;
        }
    }

    private sealed class DeadlineTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
    {
        private int _disposed;
        public TimeSpan Due { get; private set; } = due;
        public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; return _disposed == 0; }
        public void Expire() { if (Interlocked.Exchange(ref _disposed, 1) == 0) callback(state); }
        public void Dispose() { Interlocked.Exchange(ref _disposed, 1); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Interruption tests must never recycle.");
    }
}
