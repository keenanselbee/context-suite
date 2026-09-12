using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.ContractTests;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static class AudioInterruptionContracts
{
    public static async Task RunAsync(string root, string executable, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use a new audio interruption directory.");
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "Authored noise \u00fc.wav");
        var retryPath = Path.Combine(root, "Authored short.wav");
        WriteWave(sourcePath, 120);
        WriteWave(retryPath, 1, noise: false);
        var sourceHash = await Hash(sourcePath);
        var sourceTime = File.GetLastWriteTimeUtc(sourcePath);
        var retryHash = await Hash(retryPath);
        var retryTime = File.GetLastWriteTimeUtc(retryPath);
        var records = Path.Combine(root, "records");
        var workerRoot = Path.Combine(root, "workers");
        var publisher = new OutputPublisher(records, new NoRecycle());
        var clock = new ImageInterruptionContracts.DeadlineClock();
        await using var worker = new WorkerClient(executable, workerRoot, clock);
        var executor = new AudioConversionExecutor(worker, publisher, new LocalTrialStore(Path.Combine(root, "trial.json")));
        var source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sourcePath), AudioFormat.Flac, default);
        var baseline = await executor.ExecuteAsync(Plan(source, AudioFormat.Flac), null, default);
        var baselineOutput = baseline.Results.Single().Publication;
        check(baselineOutput is { Outcome: PublicationOutcome.CopyCreated, OutputPath: not null },
            "Audio interruption: long authored fixture first completes exact-sample FLAC publication");
        var committed = new Dictionary<string, string> { [baselineOutput!.OutputPath!] = await Hash(baselineOutput.OutputPath!) };
        var evidence = new List<object>();
        foreach (var target in new[] { AudioFormat.Flac, AudioFormat.Mp3, AudioFormat.M4a, AudioFormat.Vorbis, AudioFormat.Opus })
        foreach (var fault in new[] { "cancel", "client-timeout", "worker-crash" })
        {
            var scenario = target + "/" + fault;
            source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sourcePath), target, default);
            var encoding = AudioConversionPlan.Create(source.Facts, target);
            var reservation = await publisher.ReserveAsync(new(source.ItemId, sourcePath, encoding.Extension.TrimStart('.'), new("convert", new())));
            using var cancellation = new CancellationTokenSource();
            var operation = worker.ConvertAudioAsync(new(source, reservation.TemporaryPath, target, encoding.RequiredConsent), cancellation.Token);
            var workerId = worker.ProcessId!.Value;
            Process? native = null;
            string? candidate = null;
            long beforeBytes = 0, afterBytes = 0;
            try
            {
                var watch = Stopwatch.StartNew();
                while (!operation.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(25))
                {
                    // An ffmpeg child alone could be the reference decoder. A
                    // growing encoded candidate and CPU activity identify encoding.
                    candidate = Directory.EnumerateFiles(workerRoot, "encoded.candidate", SearchOption.AllDirectories).SingleOrDefault();
                    if (candidate is not null && new FileInfo(candidate).Length > 4096)
                    {
                        native = PdfFailureContracts.FindChild(workerId, Path.Combine(Path.GetDirectoryName(executable)!, "audio-engine", "ffmpeg.exe"));
                        if (native is not null && !native.HasExited)
                        {
                            beforeBytes = new FileInfo(candidate).Length;
                            var cpu = native.TotalProcessorTime;
                            await Task.Delay(75);
                            native.Refresh();
                            afterBytes = File.Exists(candidate) ? new FileInfo(candidate).Length : 0;
                            if (!operation.IsCompleted && !native.HasExited && afterBytes > beforeBytes && native.TotalProcessorTime > cpu) break;
                        }
                        native?.Dispose(); native = null;
                    }
                    await Task.Delay(5);
                }
                if (native is null || native.HasExited || operation.IsCompleted)
                    throw new InvalidOperationException("Did not observe a live encoder growing its candidate before " + scenario);
                var nativeId = native.Id;
                check(afterBytes > beforeBytes && IsLocked(reservation.TemporaryPath),
                    "Audio interruption: encoder CPU, growing candidate and reserved-output lock observed before " + scenario);
                if (fault == "cancel") cancellation.Cancel();
                else if (fault == "client-timeout")
                {
                    check(clock.Current!.Due == TimeSpan.FromSeconds(150), "Audio interruption: production client deadline is 150 seconds for " + target);
                    clock.Current.Expire();
                }
                else
                {
                    using var owned = Process.GetProcessById(workerId);
                    owned.Kill(); // Worker only: its kill-on-close job must stop ffmpeg.
                }
                try { await operation.WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Interrupted audio conversion succeeded."); }
                catch (OperationCanceledException) when (fault == "cancel")
                { check(true, "Audio interruption: cancellation retains its distinct result for " + target); }
                catch (MediaWorkerException error) when (fault != "cancel")
                {
                    check(error.Failure == (fault == "client-timeout" ? ImageFailure.TimedOut : ImageFailure.WorkerTerminated),
                        "Audio interruption: distinct typed failure for " + scenario);
                }
                await native.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                check(native.HasExited && worker.ProcessId is null, "Audio interruption: worker and owned native encoder exit after " + scenario);
                var result = await publisher.AbandonAsync(reservation, fault == "cancel");
                check(result.Outcome == (fault == "cancel" ? PublicationOutcome.Cancelled : PublicationOutcome.Failed) &&
                    !File.Exists(reservation.TemporaryPath) && !File.Exists(reservation.Record.OutputPath) &&
                    !Directory.EnumerateDirectories(workerRoot).Any() && !Directory.EnumerateFiles(records).Any(),
                    "Audio interruption: incomplete output, journal and owned worker scratch are removed after " + scenario);
                var preserved = await Hash(sourcePath) == sourceHash && File.GetLastWriteTimeUtc(sourcePath) == sourceTime &&
                    await Hash(retryPath) == retryHash && File.GetLastWriteTimeUtc(retryPath) == retryTime;
                foreach (var output in committed) preserved &= await Hash(output.Key) == output.Value;
                check(preserved, "Audio interruption: originals and earlier committed copies survive " + scenario);
                var retry = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), retryPath), target, default);
                var resumed = await executor.ExecuteAsync(Plan(retry, target), null, default);
                var resumedOutput = resumed.Results.Single().Publication;
                check(worker.ProcessId != workerId && resumedOutput is { Outcome: PublicationOutcome.CopyCreated, OutputPath: not null },
                    "Audio interruption: a fresh worker publishes a validated same-target retry after " + scenario);
                committed.Add(resumedOutput!.OutputPath!, await Hash(resumedOutput.OutputPath!));
                evidence.Add(new { target = target.ToString(), fault, workerId, nativeId, candidate, beforeBytes, afterBytes, result, resumed });
            }
            finally
            {
                cancellation.Cancel();
                try { await operation; } catch (Exception) { }
                if (!reservation.Finished) await publisher.AbandonAsync(reservation, true);
                native?.Dispose();
            }
        }
        await File.WriteAllTextAsync(Path.Combine(root, "audio-interruptions.json"), JsonSerializer.Serialize(
            new { sourceHash, retryHash, committed, evidence }, new JsonSerializerOptions { WriteIndented = true }));

        ConfirmedAudioConversion Plan(AudioFileSource input, AudioFormat target)
        {
            var batch = AudioConversionBatch.Create(Guid.NewGuid(), [input], target, new("convert", new()));
            return batch.Confirm(batch.RequiredConsent, false, false);
        }
    }

    private static void WriteWave(string path, int seconds, bool noise = true)
    {
        const int rate = 48000, frameBytes = 6;
        var bytes = checked(rate * seconds * frameBytes);
        var header = new byte[44];
        "RIFF"u8.CopyTo(header); BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), bytes + 36);
        "WAVEfmt "u8.CopyTo(header.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(22), 2);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), rate);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), rate * frameBytes);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(32), frameBytes);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(34), 24);
        "data"u8.CopyTo(header.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), bytes);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        output.Write(header);
        if (!noise)
        {
            var frame = new byte[frameBytes];
            for (var index = 0; index < rate * seconds; index++)
            {
                for (var channel = 0; channel < 2; channel++)
                {
                    var sample = (int)Math.Round(0.4 * 8388607 * Math.Sin(2 * Math.PI * (channel == 0 ? 440 : 660) * index / rate));
                    frame[channel * 3] = (byte)sample;
                    frame[channel * 3 + 1] = (byte)(sample >> 8);
                    frame[channel * 3 + 2] = (byte)(sample >> 16);
                }
                output.Write(frame);
            }
            return;
        }
        var random = new Random(933107);
        var buffer = new byte[48000];
        while (bytes > 0)
        {
            random.NextBytes(buffer);
            var count = Math.Min(bytes, buffer.Length);
            output.Write(buffer, 0, count); bytes -= count;
        }
    }

    private static async Task<string> Hash(string path)
    {
        await using var input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input));
    }

    private static bool IsLocked(string path)
    {
        try { using var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return false; }
        catch (IOException error) when ((error.HResult & 0xffff) == 32) { return true; }
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Audio interruption tests never recycle.");
    }
}
