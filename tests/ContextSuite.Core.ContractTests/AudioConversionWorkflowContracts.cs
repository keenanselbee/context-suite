using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static class AudioConversionWorkflowContracts
{
    public static async Task RunAsync(string scratch, string executable, string fixtures, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        var formats = new[] { (AudioFormat.Wave, "wav"), (AudioFormat.Flac, "flac"), (AudioFormat.Mp3, "mp3"),
            (AudioFormat.M4a, "m4a"), (AudioFormat.Vorbis, "ogg"), (AudioFormat.Opus, "opus") };
        var sources = new Dictionary<AudioFormat, string>(); var hashes = new Dictionary<string, string>();
        foreach (var (format, extension) in formats)
        {
            var path = Path.Combine(scratch, "Authored \u00fc." + extension);
            File.Copy(Path.Combine(fixtures, "source." + extension), path);
            sources[format] = path; hashes[path] = await Hash(path);
        }
        var trialPath = Path.Combine(scratch, "access", "trial.json"); var clock = new Clock();
        var access = new CountingAccess(new LocalTrialStore(trialPath, clock));
        var records = Path.Combine(scratch, "publications"); var publisher = new OutputPublisher(records, new ForbiddenRecycle());
        await using var worker = new WorkerClient(executable, Path.Combine(scratch, "workers"));
        var executor = new AudioConversionExecutor(worker, publisher, access);
        const AudioConversionConsent all = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        check(worker.HasAudioConverter, "Audio conversion: isolated encoder is present");
        foreach (var (format, _) in formats)
        {
            var source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sources[format]), format, default);
            var plan = AudioConversionBatch.Create(Guid.NewGuid(), [source], format, new("convert", new()));
            check(!plan.HasExecutableItems && plan.Items[0].Encoding!.AlreadyTarget && !File.Exists(trialPath),
                "Audio conversion: same-format " + format + " stays unchanged without trial admission");
        }
        var workerId = worker.ProcessId; var reports = new List<object>();
        foreach (var (input, _) in formats)
        foreach (var (target, extension) in formats.Where(pair => pair.Item1 != input))
        {
            var source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sources[input]), target, default);
            var plan = AudioConversionBatch.Create(Guid.NewGuid(), [source], target, new("convert", new()));
            var result = await executor.ExecuteAsync(plan.Confirm(all, false, false), null, default);
            var row = result.Results.Single();
            check(result.Admission.IsAllowed && row.Publication?.Outcome == PublicationOutcome.CopyCreated && row.EngineIdentity is not null &&
                Path.GetExtension(row.Publication.OutputPath) == "." + extension && await Hash(source.Path) == source.Sha256,
                "Audio conversion publication: " + input + " to " + target);
            var output = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), row.Publication!.OutputPath!), target, default);
            if (!AudioConversionPlan.Create(output.Facts, target).AlreadyTarget || worker.ProcessId != workerId)
                throw new InvalidDataException("Published audio has the wrong format or worker continuity.");
            reports.Add(new { input, target, result, output.Facts });
        }
        check(access.Admissions == 30, "Audio conversion: exactly one admission per cross-format batch");
        var collision = Path.Combine(scratch, OutputNames.Create(sources[AudioFormat.Wave], "convert", "flac")); var collisionHash = await Hash(collision);
        var a = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sources[AudioFormat.Wave]), AudioFormat.Flac, default);
        var b = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sources[AudioFormat.Wave]), AudioFormat.Flac, default);
        var confirmed = AudioConversionBatch.Create(Guid.NewGuid(), [a, b], AudioFormat.Flac, new("convert", new())).Confirm(0, false, false);
        var beforeAdmission = access.Admissions;
        var acrossExpiry = await executor.ExecuteAsync(confirmed, (item, row) =>
        { if (item.Source.ItemId == a.ItemId && row.State == OperationState.Succeeded) clock.Now = clock.Now.AddDays(8); }, default);
        check(acrossExpiry.Results.All(row => row.Publication?.Outcome == PublicationOutcome.CopyCreated) && access.Admissions == beforeAdmission + 1,
            "Audio conversion: admitted batch finishes both files across expiry");
        check(acrossExpiry.Results[0].Publication!.OutputPath != collision && await Hash(collision) == collisionHash,
            "Audio conversion: collisions retain existing output");
        var denied = await executor.ExecuteAsync(confirmed, null, default);
        check(!denied.Admission.IsAllowed && denied.Results.All(row => row.State == OperationState.Failed) && Directory.GetFiles(records).Length == 0,
            "Audio conversion: expired access starts no publication");
        var freshAccess = new CountingAccess(new LocalTrialStore(Path.Combine(scratch, "fresh-access", "trial.json")));
        var fresh = new AudioConversionExecutor(worker, publisher, freshAccess);
        var mp3Bytes = await File.ReadAllBytesAsync(sources[AudioFormat.Mp3]);
        using var mp3Input = new MemoryStream(mp3Bytes);
        var mp3Inventory = await Mp3Metadata.ReadAsync(mp3Input, default);
        var legacyTag = new byte[128]; "TAG"u8.CopyTo(legacyTag); "Legacy album"u8.CopyTo(legacyTag.AsSpan(63));
        legacyTag[126] = 7; legacyTag[127] = 17;
        var legacyPath = Path.Combine(scratch, "Legacy.mp3");
        await File.WriteAllBytesAsync(legacyPath, mp3Bytes[checked((int)mp3Inventory.AudioOffset)..].Concat(legacyTag).ToArray());
        hashes[legacyPath] = await Hash(legacyPath);
        var legacy = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), legacyPath), AudioFormat.Flac, default);
        var legacyResult = await fresh.ExecuteAsync(AudioConversionBatch.Create(Guid.NewGuid(), [legacy], AudioFormat.Flac, new("convert", new())).Confirm(all, false, false), null, default);
        if (legacyResult.Results.Single().Publication is not { Outcome: PublicationOutcome.CopyCreated, OutputPath: { } legacyOutput })
            throw new InvalidDataException("Legacy MP3 did not publish a validated copy.");
        var legacyFacts = (await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), legacyOutput), AudioFormat.Flac, default)).Facts;
        check(legacyFacts.Tags["album"] == "Legacy album" && legacyFacts.Tags["genre"] == "Rock" && legacyFacts.Tags["track"] == "7",
            "Audio conversion: legacy MP3 album, track and genre survive actual worker publication");
        // The native probe prefers v2 values; full conversion must still catch
        // a contradictory v1 trailer before publishing anything.
        var conflictPath = Path.Combine(scratch, "Conflicting.mp3");
        "Contradictory album"u8.CopyTo(legacyTag.AsSpan(63));
        await File.WriteAllBytesAsync(conflictPath, mp3Bytes.Concat(legacyTag).ToArray()); hashes[conflictPath] = await Hash(conflictPath);
        var conflict = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), conflictPath), AudioFormat.Flac, default);
        var conflictResult = await fresh.ExecuteAsync(AudioConversionBatch.Create(Guid.NewGuid(), [conflict, legacy], AudioFormat.Flac, new("convert", new())).Confirm(all, false, false), null, default);
        check(conflictResult.Results[0].State == OperationState.Unsupported &&
            conflictResult.Results[0].Publication is { Outcome: PublicationOutcome.Failed, OutputPath: null } && await Hash(conflictPath) == hashes[conflictPath] &&
            conflictResult.Results[1].State == OperationState.Succeeded && worker.ProcessId == workerId,
            "Audio conversion: conflicting legacy tags retain original and the next file still completes");
        reports.Add(new { legacyResult, legacyFacts, conflictResult });
        var changedPath = Path.Combine(scratch, "Changed.wav"); File.Copy(sources[AudioFormat.Wave], changedPath);
        var changed = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), changedPath), AudioFormat.Flac, default);
        await File.AppendAllTextAsync(changedPath, "changed"); var changedHash = await Hash(changedPath);
        var failed = await fresh.ExecuteAsync(AudioConversionBatch.Create(Guid.NewGuid(), [changed], AudioFormat.Flac, new("convert", new())).Confirm(0, false, false), null, default);
        check(failed.Results.Single().State == OperationState.Failed && await Hash(changedPath) == changedHash,
            "Audio conversion: source change abandons reservation and keeps changed original");

        var badPath = Path.Combine(scratch, "Unmapped.wav"); var wave = await File.ReadAllBytesAsync(sources[AudioFormat.Wave]);
        var unmapped = wave.Concat("zzzz\0\0\0\0"u8.ToArray()).ToArray(); BinaryPrimitives.WriteInt32LittleEndian(unmapped.AsSpan(4), unmapped.Length - 8);
        await File.WriteAllBytesAsync(badPath, unmapped); var badHash = await Hash(badPath);
        var bad = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), badPath), AudioFormat.Flac, default);
        var good = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sources[AudioFormat.Wave]), AudioFormat.Flac, default);
        var mixed = await fresh.ExecuteAsync(AudioConversionBatch.Create(Guid.NewGuid(), [bad, good], AudioFormat.Flac, new("convert", new())).Confirm(0, false, false), null, default);
        check(mixed.Results[0].State == OperationState.Unsupported && mixed.Results[1].State == OperationState.Succeeded &&
            worker.ProcessId == workerId && await Hash(badPath) == badHash,
            "Audio conversion: unsupported metadata keeps original and next file uses the same worker");
        var m4aPath = Path.Combine(scratch, "External.m4a"); var m4a = await File.ReadAllBytesAsync(sources[AudioFormat.M4a]);
        var url = m4a.AsSpan().IndexOf("url "u8); if (url < 0) throw new InvalidDataException("Missing local reference fixture.");
        m4a[url + 7] = 0; await File.WriteAllBytesAsync(m4aPath, m4a);
        try { await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), m4aPath), AudioFormat.Flac, default); check(false, "Audio external reference"); }
        catch (MediaWorkerException ex) { check(ex.Failure == ImageFailure.UnsupportedInput && worker.ProcessId == workerId,
            "Audio conversion: external M4A reference gives a stable unsupported result"); }
        a = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sources[AudioFormat.Wave]), AudioFormat.Flac, default);
        b = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sources[AudioFormat.Flac]), AudioFormat.Flac, default);
        var unchanged = await fresh.ExecuteAsync(AudioConversionBatch.Create(Guid.NewGuid(), [a, b], AudioFormat.Flac, new("convert", new())).Confirm(0, false, false), null, default);
        check(unchanged.Results[0].State == OperationState.Succeeded && unchanged.Results[1].State == OperationState.Unchanged && unchanged.Results[1].Publication is null,
            "Audio conversion: same-format item in mixed batch produces no duplicate");
        a = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sources[AudioFormat.Wave]), AudioFormat.Flac, default);
        b = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sources[AudioFormat.Wave]), AudioFormat.Flac, default);
        using var cancel = new CancellationTokenSource();
        var cancelled = await fresh.ExecuteAsync(AudioConversionBatch.Create(Guid.NewGuid(), [a, b], AudioFormat.Flac, new("convert", new())).Confirm(0, false, false), (_, row) =>
        { if (row.State == OperationState.Succeeded) cancel.Cancel(); }, cancel.Token);
        check(cancelled.Results[0].State == OperationState.Succeeded && cancelled.Results[1].State == OperationState.Cancelled,
            "Audio conversion: cancellation retains completed output and skips remaining file");
        var folder = Path.Combine(scratch, "Output folder"); Directory.CreateDirectory(folder);
        var alternate = await fresh.ExecuteAsync(AudioConversionBatch.Create(Guid.NewGuid(), [a], AudioFormat.Flac, new("convert", new(true, folder))).Confirm(0, false, false), null, default);
        check(alternate.Results.Single().Publication is { Outcome: PublicationOutcome.CopyCreated } saved && Path.GetDirectoryName(saved.OutputPath) == folder,
            "Audio conversion: alternate output folder keeps original even with saved overwrite preference");
        var reserved = Path.Combine(scratch, $".context-suite-{a.ItemId:N}.tmp");
        await File.WriteAllTextAsync(reserved, "reservation canary");
        try { await worker.ConvertAudioAsync(new(a, reserved, AudioFormat.Flac, 0), default); check(false, "Audio nonempty reservation"); }
        catch (MediaWorkerException) { check(await File.ReadAllTextAsync(reserved) == "reservation canary", "Audio conversion: nonempty reservation rejects all writes"); }
        File.Delete(reserved);
        var linkTarget = Path.Combine(scratch, "empty-link-target.tmp"); await File.WriteAllBytesAsync(linkTarget, []);
        if (!CreateHardLinkW(reserved, linkTarget, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            try { await worker.ConvertAudioAsync(new(a, reserved, AudioFormat.Flac, 0), default); check(false, "Audio linked reservation"); }
            catch (MediaWorkerException) { check(new FileInfo(linkTarget).Length == 0, "Audio conversion: hard-linked reservation rejects all writes"); }
        }
        finally { File.Delete(reserved); File.Delete(linkTarget); }
        foreach (var pair in hashes) if (await Hash(pair.Key) != pair.Value) throw new IOException("Original audio fixture changed.");
        check(Directory.GetFiles(records).Length == 0 && !Directory.GetFiles(scratch, ".context-suite-*.tmp", SearchOption.AllDirectories).Any(),
            "Audio conversion: all originals, reservations and publication records finish safely");
        await File.WriteAllTextAsync(Path.Combine(scratch, "audio-conversion-workflow.json"), JsonSerializer.Serialize(new { reports, acrossExpiry, denied, failed, mixed, unchanged, cancelled, alternate },
            new JsonSerializerOptions { WriteIndented = true }));
    }
    private static async Task<string> Hash(string path)
    {
        using var file = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(file));
    }
    private sealed class CountingAccess(IOperationAccess inner) : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => inner.ReadAccessAsync(token);
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected image admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected optimization admission.");
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedAudioConversion confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitConversionAsync(confirmed, token); }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => 0;
    }
    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Isolated audio conversion must never recycle.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string path, string existing, IntPtr security);
}
