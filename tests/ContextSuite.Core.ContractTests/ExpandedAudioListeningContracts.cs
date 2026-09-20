using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static class ExpandedAudioListeningContracts
{
    internal static async Task RunAsync(string pack, string executable, string manifest)
    {
        pack = Path.GetFullPath(pack);
        if (!pack.Contains("\\.codex-temp\\expanded-audio-review\\", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(pack) != "pack") throw new ArgumentException("Use an owned expanded listening pack.");
        var fixtures = JsonSerializer.Deserialize<ExpandedAudioWorkerContracts.Fixture[]>(await File.ReadAllTextAsync(manifest))!;
        if (!fixtures.Select(item => item.Name).Order().SequenceEqual(new[] { "aiff", "lossless", "pro", "standard", "voice" }) ||
            fixtures.Any(item => item.RefusedTargets is not null || item.MaximumError is null || item.RmsError is null ||
                item.Frames < item.Rate * 10 || item.Frames > item.Rate * 31 || item.Channels is < 1 or > 2))
            throw new InvalidDataException("Use five bounded positive listening fixtures with independent sample limits.");
        var work = Path.Combine(Path.GetDirectoryName(pack)!, "work");
        if (Directory.Exists(work)) throw new IOException("Use fresh listening evidence.");
        Directory.CreateDirectory(work);
        var records = Path.Combine(work, "publications");
        var scratch = Path.Combine(work, "workers");
        var sourceRows = new List<object>(); var rows = new List<object>();
        var originals = fixtures.SelectMany(item => new[] { item.Path, item.ReferencePath, item.ReferencePath + ".wav" })
            .Distinct().ToDictionary(path => path, path => (Hash: Hash(path), Written: File.GetLastWriteTimeUtc(path)));
        var publications = 0;
        await using (var worker = new WorkerClient(executable, scratch))
        {
            var executor = new AudioConversionExecutor(worker, new OutputPublisher(records, new ForbiddenRecycle()),
                new LocalTrialStore(Path.Combine(work, "trial.json")));
            foreach (var fixture in fixtures)
            {
                if (Hash(fixture.Path) != fixture.Sha256 || Hash(fixture.ReferencePath) != fixture.ReferenceHash)
                    throw new InvalidDataException("Listening fixture identity changed.");
                var reference = fixture.ReferencePath + ".wav";
                await ExpandedAudioWorkerContracts.CompareWaveAsync(reference, fixture);
                using var input = File.OpenRead(reference);
                var source = await WaveMetadata.ReadAsync(input, default);
                sourceRows.Add(new { Name = fixture.Name, Path = reference, Sha256 = Hash(reference),
                    InputPath = fixture.Path, InputSha256 = fixture.Sha256, source.SampleRate, source.Channels,
                    source.SampleBits, source.FloatingPoint, source.SampleFrames, Seconds = (double)source.SampleFrames / source.SampleRate });
                foreach (var target in new[] { AudioFormat.Wave, AudioFormat.Opus })
                {
                    var encoded = await Convert(fixture.Path, target, Path.Combine(pack, fixture.Name, "encoded", target.ToString()));
                    var encodedHash = Hash(encoded.Path);
                    var decoded = target == AudioFormat.Wave ? encoded :
                        await Convert(encoded.Path, AudioFormat.Wave, Path.Combine(pack, fixture.Name, "decoded", target.ToString()));
                    using var output = File.OpenRead(decoded.Path);
                    var wave = await WaveMetadata.ReadAsync(output, default);
                    var expectedFrames = (long)Math.Ceiling((decimal)fixture.Frames * encoded.Plan.SampleRate / fixture.Rate);
                    if (wave.SampleRate != encoded.Plan.SampleRate || wave.Channels != fixture.Channels ||
                        wave.SampleFrames != expectedFrames || Hash(encoded.Path) != encodedHash)
                        throw new InvalidDataException("Listening output changed extent or an encoded input.");
                    object? comparison = target == AudioFormat.Wave ?
                        await ExpandedAudioWorkerContracts.CompareWaveAsync(decoded.Path, fixture) : null;
                    rows.Add(new { Clip = fixture.Name, Target = target.ToString(), Encoded = encoded.Path,
                        EncodedSha256 = encodedHash, DecodedWave = decoded.Path, DecodedSha256 = Hash(decoded.Path), encoded.Plan,
                        wave.SampleRate, wave.Channels, wave.SampleBits, wave.SampleFrames, wave.FloatingPoint,
                        ExactPcm = target == AudioFormat.Wave && fixture.MaximumError == 0,
                        IndependentWaveComparison = comparison, HumanListening = "Not performed", PlayerCompatibility = "Not performed" });
                    Console.WriteLine("Prepared expanded listening comparison: " + fixture.Name + " / " + target);
                }
            }
            async Task<(string Path, AudioConversionPlan Plan)> Convert(string path, AudioFormat target, string directory)
            {
                Directory.CreateDirectory(directory);
                var source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), path), target, default);
                var fixture = fixtures.SingleOrDefault(item => item.Path == path);
                if (fixture is not null)
                {
                    var audio = source.Facts.Streams.Single(item => item.Kind == "audio");
                    if (audio.Codec != fixture.Codec || audio.SampleRate != fixture.Rate || audio.Channels != fixture.Channels)
                        throw new InvalidDataException("Expanded listening source profile differs.");
                }
                var plan = AudioConversionBatch.Create(Guid.NewGuid(), [source], target,
                    new("convert", new(OutputDirectory: directory), PlayCompletionSound: false));
                if (!plan.HasExecutableItems) throw new InvalidDataException("Listening source refused: " + plan.Items.Single().BlockReason);
                var encoding = plan.Items.Single().Encoding!;
                var result = await executor.ExecuteAsync(plan.Confirm(encoding.RequiredConsent, false, false), null, default);
                var row = result.Results.Single();
                if (!result.Admission.IsAllowed || row.State != OperationState.Succeeded ||
                    row.Publication is not { Outcome: PublicationOutcome.CopyCreated, OutputPath: { } output } ||
                    row.EngineIdentity is null || Path.GetDirectoryName(output) != directory ||
                    fixture?.Codec == "wmavoice" && !row.EngineIdentity.Contains("Windows Voice", StringComparison.Ordinal))
                    throw new InvalidDataException("Listening publication failed: " + row.Message);
                publications++;
                return (output, encoding);
            }
        }
        if (publications != 15 || rows.Count != 10 || originals.Any(item =>
                Hash(item.Key) != item.Value.Hash || File.GetLastWriteTimeUtc(item.Key) != item.Value.Written) ||
            Directory.EnumerateFileSystemEntries(records).Any() || Directory.EnumerateDirectories(scratch).Any() ||
            Directory.EnumerateFiles(pack, ".context-suite-*.tmp", SearchOption.AllDirectories).Any())
            throw new IOException("Incomplete listening pack, altered original or retained scratch.");
        await File.WriteAllTextAsync(Path.Combine(pack, "results.json"), JsonSerializer.Serialize(new
        {
            PackKind = "expanded", AutomatedPassed = true, HumanListening = "Not performed", PlayerCompatibility = "Not performed",
            Publications = publications, Sources = sourceRows, Comparisons = rows,
            Scope = "Five generated AIFF/WMA sources to WAV/Opus; independent WAV sample checks, production worker and publication. Human and final-candidate review remain open."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Prepared ten expanded listening comparisons through fifteen validated publications.");
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input));
    }

    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Listening review preserves all originals.");
    }
}
