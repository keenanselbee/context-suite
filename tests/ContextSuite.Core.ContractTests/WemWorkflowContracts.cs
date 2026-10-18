using System.Buffers.Binary;
using System.Security.Cryptography;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static class WemWorkflowContracts
{
    public static async Task RunAsync(string scratch, string executable, string fixtures, Action<bool, string> check,
        bool withAudioEngine = false)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use a new WEM workflow evidence directory.");
        Directory.CreateDirectory(scratch);
        var paths = new List<string>();
        foreach (var codec in new[] { "pcm", "vorbis" })
        {
            var files = Directory.GetFiles(Path.Combine(fixtures, "output-" + codec, "Windows"), "*.wem").Order().ToArray();
            if (files.Length != 4) throw new InvalidDataException("Expected four authored Wwise fixtures per codec.");
            foreach (var file in files)
            {
                var path = Path.Combine(scratch, codec + "-" + Path.GetFileName(file));
                File.Copy(file, path); paths.Add(path);
            }
        }
        var records = Path.Combine(scratch, "publications");
        var access = new CountingAccess(new LocalTrialStore(Path.Combine(scratch, "access", "trial.json")));
        var publisher = new OutputPublisher(records, new ForbiddenRecycle());
        await using var worker = new WorkerClient(executable, Path.Combine(scratch, "workers"));
        check(worker.HasWemDecoder && worker.HasAudioConverter == withAudioEngine,
            withAudioEngine ? "WEM: combined worker has the decoder and audio engine" : "WEM: isolated worker has decoder without FFmpeg");
        var sources = new List<AudioFileSource>();
        foreach (var path in paths)
        {
            var source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), path), AudioFormat.Wave, default);
            check(source.Facts.Container == "wem" && source.Sha256 == await Hash(path), "WEM: typed content probe and source fingerprint");
            sources.Add(source);
        }
        var workerId = worker.ProcessId;
        var executor = new AudioConversionExecutor(worker, publisher, access);
        ConfirmedAudioConversion Plan(IEnumerable<AudioFileSource> selection) =>
            AudioConversionBatch.Create(Guid.NewGuid(), selection, AudioFormat.Wave, new("convert", new())).Confirm(0, false, false);
        var collision = Path.Combine(scratch, OutputNames.Create(paths[0], "convert", ".wav"));
        await File.WriteAllTextAsync(collision, "Existing output must survive.");
        var collisionHash = await Hash(collision);
        var batch = await executor.ExecuteAsync(Plan(sources), null, default);
        check(batch.Results.Count == 8 && access.Admissions == 1, "WEM: all eight items share one admitted batch");
        for (var i = 0; i < sources.Count; i++)
        {
            var row = batch.Results[i];
            check(row.Publication is { Outcome: PublicationOutcome.CopyCreated, OutputPath: not null }, "WEM: validated copy published " + i);
            if (row.Publication?.OutputPath is not { } output) throw new InvalidDataException(row.Message);
            using var original = File.OpenRead(sources[i].Path);
            var header = await WemHeader.ReadAsync(original);
            using var wave = File.OpenRead(output);
            var inventory = await WaveMetadata.ReadAsync(wave, default);
            check(inventory.SampleRate == header.SampleRate && inventory.Channels == header.Channels &&
                inventory.SampleFrames == header.SampleCount && inventory.SampleBits == (header.Codec == WemCodec.Pcm16 ? 16 : 32) &&
                inventory.FloatingPoint == (header.Codec == WemCodec.Vorbis), "WEM: published WAV dimensions and precision " + i);
            var chunks = Chunks(await File.ReadAllBytesAsync(output));
            if (header.Loop is { } loop)
            {
                var smpl = chunks["smpl"];
                check(BinaryPrimitives.ReadUInt32LittleEndian(smpl.AsSpan(28)) == 1 &&
                    BinaryPrimitives.ReadUInt32LittleEndian(smpl.AsSpan(44)) == loop.StartSample &&
                    1L + BinaryPrimitives.ReadUInt32LittleEndian(smpl.AsSpan(48)) == loop.EndSampleExclusive,
                    "WEM: forward loop boundaries preserved " + i);
            }
            if (header.Codec == WemCodec.Pcm16)
            {
                original.Position = header.DataOffset;
                var samples = new byte[checked((int)header.DataSize)];
                await original.ReadExactlyAsync(samples);
                check(samples.SequenceEqual(chunks["data"]), "WEM: PCM samples remain byte-exact " + i);
            }
            check(await Hash(sources[i].Path) == sources[i].Sha256 && worker.ProcessId == workerId,
                "WEM: source retained and sequential worker reused " + i);
        }
        check(await Hash(collision) == collisionHash && batch.Results[0].Publication!.OutputPath != collision,
            "WEM: output collision never overwrites an existing file");

        using var cancel = new CancellationTokenSource();
        var cancelled = await executor.ExecuteAsync(Plan(sources.Take(2)), (_, row) =>
        { if (row.State == OperationState.Succeeded) cancel.Cancel(); }, cancel.Token);
        check(cancelled.Results[0].State == OperationState.Succeeded && cancelled.Results[1].State == OperationState.Cancelled,
            "WEM: cancellation keeps committed output and skips the next item");
        var changedPath = Path.Combine(scratch, "changed.wem"); File.Copy(paths[0], changedPath);
        var changed = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), changedPath), AudioFormat.Wave, default);
        await File.AppendAllTextAsync(changedPath, "changed"); var changedHash = await Hash(changedPath);
        var failed = await executor.ExecuteAsync(Plan([changed, sources[0]]), null, default);
        check(failed.Results[0].State == OperationState.Failed && failed.Results[1].State == OperationState.Succeeded &&
            await Hash(changedPath) == changedHash, "WEM: source change refuses publication and the next item succeeds");

        var malformed = Path.Combine(scratch, "malformed.wem"); await File.WriteAllTextAsync(malformed, "Not a WEM file.");
        var badHash = await Hash(malformed);
        var before = access.Admissions;
        await using var view = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access);
        view.AudioConversionRequested += (_, _) => throw new InvalidOperationException("WEM decode must not need a quality prompt.");
        check(view.Admit(new(Guid.NewGuid(), "convert", "wav", [malformed, paths[0]])).Accepted,
            "WEM: direct menu request enters coordinated workflow");
        await view.WaitForIdleAsync();
        check(view.Rows.Count == 2 && view.Rows[0].Result.State == OperationState.Failed &&
            view.Rows[1].Result.Publication is { Outcome: PublicationOutcome.CopyCreated } && access.Admissions == before + 1 &&
            await Hash(malformed) == badHash && worker.ProcessId == workerId,
            "WEM: malformed mixed selection retains source and completes supported item through the selected worker");
        var reserved = Path.Combine(scratch, $".context-suite-{sources[0].ItemId:N}.tmp");
        await File.WriteAllTextAsync(reserved, "reservation canary");
        try { await worker.ConvertAudioAsync(new(sources[0], reserved, AudioFormat.Wave, 0), default); check(false, "WEM occupied reservation"); }
        catch (MediaWorkerException)
        { check(await File.ReadAllTextAsync(reserved) == "reservation canary", "WEM: occupied reservation refuses writes"); }
        File.Delete(reserved);
        check(!Directory.GetFiles(scratch, ".context-suite-*.tmp", SearchOption.AllDirectories).Any() && Directory.GetFiles(records).Length == 0,
            "WEM: publication reservations and journals are cleared after completion/refusal");
    }

    private static Dictionary<string, byte[]> Chunks(byte[] bytes)
    {
        var chunks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (var offset = 12; offset < bytes.Length;)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4)));
            chunks.Add(System.Text.Encoding.ASCII.GetString(bytes, offset, 4), bytes.AsSpan(offset + 8, size).ToArray());
            offset = checked(offset + 8 + size + (size & 1));
        }
        return chunks;
    }

    private static async Task<string> Hash(string path)
    {
        using var file = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(file));
    }

    private sealed class CountingAccess(IOperationAccess inner) : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => inner.ReadAccessAsync(token);
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException();
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new InvalidOperationException();
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedAudioConversion confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitConversionAsync(confirmed, token); }
    }

    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("WEM workflow verification must never recycle.");
    }
}
