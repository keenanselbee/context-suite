using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static class ExpandedAudioWorkerContracts
{
    internal sealed record Fixture(string Name, string Path, string Sha256, string Codec, int Rate, int Channels,
        long Frames, string ReferencePath, string ReferenceHash, string ReferenceEncoding, double? MaximumError, double? RmsError,
        string[]? RefusedTargets = null);

    public static async Task RunAsync(string scratch, string executable, string manifest)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use new worker evidence.");
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(await File.ReadAllTextAsync(manifest)) ?? throw new Exception("Missing manifest.");
        if (fixtures.Length != 144 || fixtures.Select(item => item.Name).Distinct().Count() != fixtures.Length)
            throw new Exception("Expected 95 WMA, 27 boundary inputs and 22 representative AIFF cases.");
        Directory.CreateDirectory(scratch);
        var inputs = Path.Combine(scratch, "inputs"); Directory.CreateDirectory(inputs);
        var outputs = Path.Combine(scratch, "outputs"); Directory.CreateDirectory(outputs);
        var records = Path.Combine(scratch, "publications");
        var publisher = new OutputPublisher(records, new ForbiddenRecycle());
        await using var worker = new WorkerClient(executable, Path.Combine(scratch, "workers"));
        var access = new LocalTrialStore(Path.Combine(scratch, "access", "trial.json"));
        var executor = new AudioConversionExecutor(worker, publisher, access);
        var checks = new List<string>(); var results = new List<object>(); var failures = new List<string>();
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        foreach (var fixture in fixtures)
        {
            if (Path.GetFileName(fixture.Name) != fixture.Name || await Hash(fixture.Path) != fixture.Sha256 ||
                await Hash(fixture.ReferencePath) != fixture.ReferenceHash) throw new Exception("Fixture identity changed.");
            var path = Path.Combine(inputs, fixture.Name + Path.GetExtension(fixture.Path)); File.Copy(fixture.Path, path);
            foreach (var target in Enum.GetValues<AudioFormat>().Where(format => format != AudioFormat.Wem))
            {
                var expectedRefusal = target == AudioFormat.Mp3 && (fixture.Channels > 2 || fixture.Rate > 48000);
                try
                {
                    var source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), path), target, default);
                    var audio = source.Facts.Streams.Single(stream => stream.Kind == "audio");
                    if (audio.Codec != fixture.Codec || audio.SampleRate != fixture.Rate || audio.Channels != fixture.Channels)
                        throw new Exception("Worker probe disagrees with the authored profile.");
                    var plan = AudioConversionBatch.Create(Guid.NewGuid(), [source], target, new("convert", new(OutputDirectory: outputs)));
                    if (expectedRefusal)
                    {
                        if (plan.HasExecutableItems) throw new Exception("Unsupported MP3 layout/rate was admitted.");
                        checks.Add(fixture.Name + ": MP3 layout/rate refused before publication");
                        continue;
                    }
                    if (!plan.HasExecutableItems) throw new Exception("Required conversion blocked: " + plan.Items[0].BlockReason);
                    var outputCount = Directory.EnumerateFileSystemEntries(outputs).Count();
                    var result = await executor.ExecuteAsync(plan.Confirm(consent, false, false), null, default);
                    var row = result.Results.Single();
                    if (fixture.RefusedTargets?.Contains(target.ToString()) == true)
                    {
                        if (row.State != OperationState.Failed || row.Publication is not { Outcome: PublicationOutcome.Failed } ||
                            !row.Message.Contains("preserve the sound", StringComparison.Ordinal) || Directory.EnumerateFileSystemEntries(outputs).Count() != outputCount)
                            throw new Exception("Extreme sample fixture bypassed its verified signal-error refusal or left output: " + row.Message);
                        checks.Add(fixture.Name + ": excessive lossy signal error refuses publication to " + target);
                        continue;
                    }
                    if (!result.Admission.IsAllowed || row.State != OperationState.Succeeded ||
                        row.Publication is not { Outcome: PublicationOutcome.CopyCreated, OutputPath: { } output })
                        throw new Exception("Worker publication failed: " + row.Message);
                    if (row.EngineIdentity is null || fixture.Codec == "wmavoice" && !row.EngineIdentity.Contains("Windows Voice", StringComparison.Ordinal))
                        throw new Exception("Missing selected decoder provenance.");
                    var published = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), output), target, default);
                    if (!AudioConversionPlan.Create(published.Facts, target).AlreadyTarget) throw new Exception("Published target differs.");
                    object? comparison = null;
                    if (target == AudioFormat.Wave)
                        comparison = await CompareWaveAsync(output, fixture);
                    results.Add(new { fixture.Name, target = target.ToString(), row, comparison, outputSha256 = await Hash(output) });
                    checks.Add(fixture.Name + ": actual worker validated publication to " + target);
                }
                catch (Exception error)
                {
                    failures.Add(fixture.Name + " / " + target + ": " + error.Message);
                    Console.WriteLine("FAIL: " + failures[^1]);
                }
                if (await Hash(path) != fixture.Sha256 || await Hash(fixture.Path) != fixture.Sha256)
                    throw new Exception("Original input changed.");
            }
            if (await Hash(path) != fixture.Sha256 || await Hash(fixture.Path) != fixture.Sha256)
                throw new Exception("Original input changed during the final target decision.");
            checks.Add(fixture.Name + ": authored input and working copy unchanged");
            await File.WriteAllTextAsync(Path.Combine(scratch, "results.json"), JsonSerializer.Serialize(new { checks, results, failures },
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(fixture.Name + ": completed six target decisions");
        }
        if (failures.Count != 0) throw new Exception($"Expanded worker acceptance has {failures.Count} failed cells; see results.json.");
        Console.WriteLine($"Passed {checks.Count} expanded audio worker checks with {results.Count} publications. Evidence: {scratch}");
    }

    internal static async Task<object> CompareWaveAsync(string path, Fixture fixture)
    {
        using var source = File.OpenRead(path);
        var inventory = await WaveMetadata.ReadAsync(source, default, true);
        if (inventory.SampleFrames != fixture.Frames || inventory.SampleRate != fixture.Rate || inventory.Channels != fixture.Channels)
            throw new Exception("Published WAV extent differs from its independent source/reference.");
        var bytes = await File.ReadAllBytesAsync(path); byte[]? samples = null;
        for (var offset = 12; offset + 8 <= bytes.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4)));
            if (bytes.AsSpan(offset, 4).SequenceEqual("data"u8)) { samples = bytes.AsSpan(offset + 8, length).ToArray(); break; }
            offset = checked(offset + 8 + length + (length & 1));
        }
        if (samples is null) throw new Exception("Missing WAV samples.");
        var reference = await File.ReadAllBytesAsync(fixture.ReferencePath);
        var expectedUnit = fixture.ReferenceEncoding switch { "f32" => 4, "f64" => 8, "s16" => 2, "s24" => 3, _ => throw new Exception("Unknown reference encoding.") };
        var actualUnit = inventory.SampleBits / 8;
        var count = fixture.Frames * fixture.Channels;
        if (reference.Length != count * expectedUnit || samples.Length != count * actualUnit) throw new Exception("Reference extent differs.");
        double peak = 0, square = 0;
        var channelPeaks = new double[fixture.Channels];
        var channelSquares = new double[fixture.Channels];
        for (var index = 0; index < count; index++)
        {
            var expected = Read(reference.AsSpan(checked((int)(index * expectedUnit))), expectedUnit, fixture.ReferenceEncoding[0] == 'f');
            var actual = Read(samples.AsSpan(checked((int)(index * actualUnit))), actualUnit, inventory.FloatingPoint);
            var error = Math.Abs(expected - actual);
            if (!double.IsFinite(error)) throw new Exception("Nonfinite published/reference audio.");
            peak = Math.Max(peak, error); square += error * error;
            var channel = index % fixture.Channels;
            channelPeaks[channel] = Math.Max(channelPeaks[channel], error);
            channelSquares[channel] += error * error;
        }
        var rms = Math.Sqrt(square / count);
        var channelRms = channelSquares.Select(value => Math.Sqrt(value / fixture.Frames)).ToArray();
        if (fixture.MaximumError is { } maximum && channelPeaks.Any(value => value > maximum) ||
            fixture.RmsError is { } maximumRms && channelRms.Any(value => value > maximumRms))
            throw new Exception($"Independent WAV sample comparison failed: peak {peak}, RMS {rms}.");
        return new { peak, rms, channelPeaks, channelRms, frames = fixture.Frames, agreementAccepted = fixture.MaximumError is not null };
    }

    private static double Read(ReadOnlySpan<byte> bytes, int width, bool floating) => (width, floating) switch
    {
        (4, true) => BinaryPrimitives.ReadSingleLittleEndian(bytes), (8, true) => BinaryPrimitives.ReadDoubleLittleEndian(bytes),
        (1, false) => (bytes[0] - 128) / 128.0, (2, false) => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768.0,
        (3, false) => ((bytes[0] | bytes[1] << 8 | bytes[2] << 16) << 8 >> 8) / 8388608.0,
        (4, false) => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648.0,
        _ => throw new Exception("Unexpected PCM width.")
    };

    private static async Task<string> Hash(string path)
    {
        using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new Exception("Copy-only worker tests must not recycle.");
    }
}
