using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static class AudioListeningContracts
{
    internal static async Task RunAsync(string evidence, string executable)
    {
        evidence = Path.GetFullPath(evidence);
        if (!evidence.Contains("\\.codex-temp\\audio-review\\", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(evidence) != "pack" || Directory.Exists(Path.Combine(Path.GetDirectoryName(evidence)!, "work")))
            throw new ArgumentException("Use a fresh owned audio review pack.");
        var work = Path.Combine(Path.GetDirectoryName(evidence)!, "work"); Directory.CreateDirectory(work);
        var sources = Path.Combine(evidence, "sources");
        AudioListeningFixtures.Create(Path.Combine(sources, "music.wav"), false);
        AudioListeningFixtures.Create(Path.Combine(sources, "transients.wav"), true);
        var names = new[] { "music", "transients", "speech" };
        var originals = names.ToDictionary(name => name, name => new
        {
            Path = Path.Combine(sources, name + ".wav"),
            Sha256 = Hash(Path.Combine(sources, name + ".wav")),
            Written = File.GetLastWriteTimeUtc(Path.Combine(sources, name + ".wav"))
        });
        var records = Path.Combine(work, "publications");
        var workerRoot = Path.Combine(work, "workers");
        var rows = new List<object>();
        var sourceRows = new List<object>();
        var publications = 0;
        await using (var worker = new WorkerClient(executable, workerRoot))
        {
            if (!worker.HasAudioConverter) throw new IOException("The current isolated audio worker is required.");
            var publisher = new OutputPublisher(records, new ForbiddenRecycle());
            var access = new LocalTrialStore(Path.Combine(work, "access", "trial.json"));
            var executor = new AudioConversionExecutor(worker, publisher, access);
            foreach (var name in names)
            {
                var original = originals[name];
                using var input = File.OpenRead(original.Path);
                var source = await WaveMetadata.ReadAsync(input, default);
                if (source.SampleFrames <= 0 || source.FloatingPoint ||
                    source.SampleRate != (name == "music" ? 44100 : 48000) || source.Channels != (name == "speech" ? 1 : 2))
                    throw new InvalidDataException("Unexpected generated audio source.");
                sourceRows.Add(new { Name = name, original.Path, original.Sha256, source.SampleRate, source.Channels,
                    source.SampleBits, source.SampleFrames, Seconds = (double)source.SampleFrames / source.SampleRate });
                foreach (var target in new[] { AudioFormat.Flac, AudioFormat.Mp3, AudioFormat.M4a, AudioFormat.Vorbis, AudioFormat.Opus })
                {
                    var encodedDirectory = Path.Combine(evidence, name, "encoded", target.ToString());
                    var decodedDirectory = Path.Combine(evidence, name, "decoded", target.ToString());
                    var encoded = await Convert(original.Path, target, encodedDirectory);
                    var encodedHash = Hash(encoded.Path);
                    var decoded = await Convert(encoded.Path, AudioFormat.Wave, decodedDirectory);
                    using var output = File.OpenRead(decoded.Path);
                    var wave = await WaveMetadata.ReadAsync(output, default);
                    var expectedFrames = checked((long)Math.Ceiling((decimal)source.SampleFrames * encoded.Plan.SampleRate / source.SampleRate));
                    if (wave.SampleRate != encoded.Plan.SampleRate || wave.Channels != source.Channels ||
                        wave.SampleFrames != expectedFrames || encodedHash != Hash(encoded.Path))
                        throw new InvalidDataException("Decoded comparison changed the recording extent or encoded source.");
                    var exact = target == AudioFormat.Flac;
                    if (exact && (wave.SampleBits != source.SampleBits || wave.FloatingPoint || PayloadHash(original.Path) != PayloadHash(decoded.Path)))
                        throw new InvalidDataException("Lossless review comparison differs from its original PCM samples.");
                    rows.Add(new { Clip = name, Target = target.ToString(), Encoded = encoded.Path, EncodedSha256 = encodedHash,
                        DecodedWave = decoded.Path, DecodedSha256 = Hash(decoded.Path), encoded.Plan,
                        wave.SampleRate, wave.Channels, wave.SampleBits, wave.SampleFrames, wave.FloatingPoint,
                        ExactPcm = exact, HumanListening = "Not performed", PlayerCompatibility = "Not performed" });
                    Console.WriteLine("Prepared validated listening comparison: " + name + " / " + target);
                }
            }
            async Task<(string Path, AudioConversionPlan Plan)> Convert(string path, AudioFormat target, string folder)
            {
                Directory.CreateDirectory(folder);
                var source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), path), target, default);
                var plan = AudioConversionBatch.Create(Guid.NewGuid(), [source], target,
                    new("convert", new(OutputDirectory: folder), PlayCompletionSound: false));
                if (!plan.HasExecutableItems || !plan.Items.Single().CanExecute)
                    throw new InvalidDataException("Generated source is not admitted: " + plan.Items.Single().BlockReason);
                var encoding = plan.Items.Single().Encoding!;
                var result = await executor.ExecuteAsync(plan.Confirm(encoding.RequiredConsent, false, false), null, default);
                var row = result.Results.Single();
                if (!result.Admission.IsAllowed || row.Publication is not { Outcome: PublicationOutcome.CopyCreated, OutputPath: { } output } ||
                    row.EngineIdentity is null || Path.GetDirectoryName(output) != folder)
                    throw new InvalidDataException("Listening pack requires a validated publication: " + row.Message);
                publications++;
                return (output, encoding);
            }
        }
        foreach (var original in originals.Values)
            if (Hash(original.Path) != original.Sha256 || File.GetLastWriteTimeUtc(original.Path) != original.Written)
                throw new IOException("Review preparation changed an original.");
        if (publications != 30 || rows.Count != 15 || Directory.EnumerateFiles(records).Any() ||
            Directory.EnumerateDirectories(workerRoot).Any() ||
            Directory.EnumerateFiles(evidence, ".context-suite-*.tmp", SearchOption.AllDirectories).Any())
            throw new IOException("Incomplete review pack or retained work.");
        await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new
        {
            AutomatedPassed = true, HumanListening = "Not performed", PlayerCompatibility = "Not performed",
            Publications = publications, Sources = sourceRows, Comparisons = rows,
            Scope = "Generated clips and production worker/publication paths; not a completed listening, player or visible-UI acceptance."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Prepared 15 encoded clips and 15 decoded WAV comparisons through 30 validated publications. Human review remains open.");
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static string PayloadHash(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length is < 44 or > 64 * 1024 * 1024 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8)) throw new InvalidDataException("Expected an authored bounded WAVE.");
        for (var offset = 12; offset <= bytes.Length - 8;)
        {
            var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4));
            if (count > bytes.Length - offset - 8) throw new InvalidDataException("Truncated WAVE comparison.");
            if (bytes.AsSpan(offset, 4).SequenceEqual("data"u8))
                return Convert.ToHexString(SHA256.HashData(bytes.AsSpan(offset + 8, (int)count)));
            offset = checked(offset + 8 + (int)count + (int)(count & 1));
        }
        throw new InvalidDataException("Missing WAVE samples.");
    }

    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Audio review fixtures must remain copies.");
    }
}
