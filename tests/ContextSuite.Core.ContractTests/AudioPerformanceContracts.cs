using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static class AudioPerformanceContracts
{
    private sealed record Sample(int Rate, AudioFormat Target, int Iteration, double Seconds,
        int WorkerPid, long WorkerPeakWorkingSetBytes, string Source, string SourceSha256,
        string Output, string OutputSha256, long OutputBytes, AudioConversionPlan Plan);

    internal static async Task RunAsync(string evidence, string executable)
    {
        evidence = Path.GetFullPath(evidence);
        if (!evidence.Contains("\\.codex-temp\\audio-review\\", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(evidence) != "pack" || Directory.Exists(Path.Combine(evidence, "performance")))
            throw new ArgumentException("Use a fresh owned audio performance pack.");
        var work = Path.Combine(evidence, "performance");
        Directory.CreateDirectory(work);
        var originals = new Dictionary<string, (string Hash, DateTime Written)>();
        var samples = new List<Sample>();
        var targets = new[] { AudioFormat.Wave, AudioFormat.Flac, AudioFormat.Mp3,
            AudioFormat.M4a, AudioFormat.Vorbis, AudioFormat.Opus };
        foreach (var rate in new[] { 44100, 48000 })
        {
            var wave = Path.Combine(evidence, "sources", $"music-{rate}.wav");
            AudioListeningFixtures.Create(wave, false, rate, 60);
            using (var stream = File.OpenRead(wave))
            {
                var facts = await WaveMetadata.ReadAsync(stream, default);
                if (facts.SampleRate != rate || facts.Channels != 2 || facts.SampleBits != 24 ||
                    facts.SampleFrames != rate * 60L || facts.FloatingPoint)
                    throw new InvalidDataException("The benchmark requires sixty seconds of stereo PCM24.");
            }
            originals.Add(wave, (Hash(wave), File.GetLastWriteTimeUtc(wave)));
            // Prepare FLAC outside the timed matrix so WAV output is a conversion,
            // never the application's same-format no-op. All other targets use WAV.
            var reference = await ConvertAsync(wave, AudioFormat.Flac, rate, -1);
            originals.Add(reference.Output, (reference.OutputSha256, File.GetLastWriteTimeUtc(reference.Output)));
            // Interleave targets across five iterations; every operation starts a
            // fresh worker. This is not a cold-storage or visible-dialog benchmark.
            for (var iteration = 0; iteration < 5; iteration++)
                foreach (var target in targets)
                {
                    var sample = await ConvertAsync(target == AudioFormat.Wave ? reference.Output : wave,
                        target, rate, iteration);
                    samples.Add(sample);
                    await File.WriteAllTextAsync(Path.Combine(work, "samples.json"),
                        JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine($"Audio {rate} Hz / {target} / {iteration + 1}: {sample.Seconds:F3} s");
                }
        }
        foreach (var (path, expected) in originals)
            if (Hash(path) != expected.Hash || File.GetLastWriteTimeUtc(path) != expected.Written)
                throw new IOException("Performance measurement changed a source.");
        var groups = samples.GroupBy(sample => (sample.Rate, sample.Target)).Select(group =>
        {
            var times = group.Select(sample => sample.Seconds).Order().ToArray();
            return new { group.Key.Rate, Target = group.Key.Target.ToString(), Runs = times.Length,
                MedianSeconds = times[2], MaximumSeconds = times[^1],
                Passed = times.Length == 5 && times[2] <= 15 && times[^1] <= 30 };
        }).ToArray();
        var passed = samples.Count == 60 && groups.Length == 12 && groups.All(group => group.Passed);
        await File.WriteAllTextAsync(Path.Combine(evidence, "performance-results.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, MedianLimitSeconds = 15, MaximumLimitSeconds = 30, Groups = groups, Samples = samples,
            Sources = originals.Select(pair => new { Path = pair.Key, Sha256 = pair.Value.Hash, Written = pair.Value.Written }),
            Machine = new { OS = Environment.OSVersion.ToString(), Runtime = Environment.Version.ToString(),
                LogicalProcessors = Environment.ProcessorCount, Is64Bit = Environment.Is64BitProcess },
            Scope = "Fresh-worker application executor timing, including inspection, access, validation and publication. " +
                "Generated inputs; no human consent time, listening, cold-storage or final-candidate acceptance. " +
                "Memory is managed-worker peak working set only; native descendant/job peak is not measured."
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!passed) throw new InvalidOperationException("Audio performance budget missed; retain the recorded samples.");
        Console.WriteLine("All twelve audio rate/target groups meet the declared budgets across sixty operations.");

        async Task<Sample> ConvertAsync(string input, AudioFormat target, int rate, int iteration)
        {
            var directory = Path.Combine(work, $"{rate}-{target}-{iteration}");
            Directory.CreateDirectory(directory);
            var outputs = Path.Combine(directory, "outputs");
            Directory.CreateDirectory(outputs);
            var records = Path.Combine(directory, "publications");
            var workerRoot = Path.Combine(directory, "worker");
            var sourceHash = Hash(input);
            var written = File.GetLastWriteTimeUtc(input);
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var stopwatch = Stopwatch.StartNew();
            Sample sample;
            Process? process = null;
            try
            {
                await using (var worker = new WorkerClient(executable, workerRoot))
                {
                    var publisher = new OutputPublisher(records, new ForbiddenRecycle());
                    var access = new LocalTrialStore(Path.Combine(directory, "trial.json"));
                    var executor = new AudioConversionExecutor(worker, publisher, access);
                    var source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), input), target, deadline.Token);
                    var batch = AudioConversionBatch.Create(Guid.NewGuid(), [source], target,
                        new("convert", new(OutputDirectory: outputs), PlayCompletionSound: false));
                    if (!batch.HasExecutableItems || !batch.Items.Single().CanExecute)
                        throw new InvalidDataException("Performance source is not admitted: " + batch.Items.Single().BlockReason);
                    var encoding = batch.Items.Single().Encoding!;
                    process = Process.GetProcessById(worker.ProcessId ?? throw new IOException("Worker did not start."));
                    var result = await executor.ExecuteAsync(batch.Confirm(encoding.RequiredConsent, false, false), null, deadline.Token);
                    stopwatch.Stop();
                    var row = result.Results.Single();
                    if (!result.Admission.IsAllowed || row.EngineIdentity is null ||
                        row.Publication is not { Outcome: PublicationOutcome.CopyCreated, OutputPath: { } output } ||
                        Path.GetDirectoryName(output) != outputs)
                        throw new InvalidDataException("Performance conversion did not publish a validated copy: " + row.Message);
                    process.Refresh();
                    sample = new(rate, target, iteration, stopwatch.Elapsed.TotalSeconds, process.Id,
                        process.PeakWorkingSet64, input, sourceHash, output, Hash(output), new FileInfo(output).Length, encoding);
                }
                if (!process.HasExited || Hash(input) != sourceHash || File.GetLastWriteTimeUtc(input) != written ||
                    Directory.EnumerateFiles(records).Any() || Directory.EnumerateDirectories(workerRoot).Any() ||
                    Directory.EnumerateFiles(directory, ".context-suite-*.tmp", SearchOption.AllDirectories).Any())
                    throw new IOException("Performance run left a worker, publication record or changed source.");
                return sample;
            }
            finally { process?.Dispose(); }
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Performance fixtures must remain copies.");
    }
}
