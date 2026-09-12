using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static class AudioPublicationCrashContracts
{
    public static async Task RunAsync(string root, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use a new audio publication crash directory.");
        Directory.CreateDirectory(root);
        var wave = await File.ReadAllBytesAsync(Path.Combine(fixtures, "source.wav"));
        var flac = await File.ReadAllBytesAsync(Path.Combine(fixtures, "source.flac"));
        if (flac.Length < 42 || !flac.AsSpan(0, 8).SequenceEqual(new byte[] { 102, 76, 97, 67, 0, 0, 0, 34 }))
            throw new InvalidDataException("Expected generated FLAC with non-final STREAMINFO before inserting padding.");
        var padding = new byte[4 + 262144]; padding[0] = 1; padding[1] = 4;
        flac = [.. flac[..42], .. padding, .. flac[42..]];
        var evidence = new List<object>();
        foreach (var operation in new[] { "convert", "optimize" })
        foreach (var stage in new[] { "Prepared", "Validated", "Publishing", "PublishedBeforeRecord", "Committed" })
        {
            var directory = Path.Combine(root, operation + "-" + stage);
            Directory.CreateDirectory(directory);
            var sourcePath = Path.Combine(directory, operation == "convert" ? "Original \u00fc.wav" : "Original \u00fc.flac");
            await File.WriteAllBytesAsync(sourcePath, operation == "convert" ? wave : flac);
            var hash = await Hash(sourcePath);
            var modified = File.GetLastWriteTimeUtc(sourcePath);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
                start.ArgumentList.Add(typeof(AudioPublicationCrashContracts).Assembly.Location);
            foreach (var argument in new[] { "--audio-publication-crash-child", directory, executable, operation, stage })
                start.ArgumentList.Add(argument);
            using var child = Process.Start(start) ?? throw new IOException("Audio publication crash child could not start.");
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch { if (!child.HasExited) child.Kill(true); throw; }
            check(child.ExitCode != 0 && File.ReadAllText(Path.Combine(directory, "checkpoint.txt")) == stage,
                $"Audio app crash: {operation} reaches {stage} before forced termination");

            var publisher = new OutputPublisher(Path.Combine(directory, "records"), new NoRecycle());
            var recordPath = publisher.FindRecoveryRecords().Single();
            var recordBytes = await File.ReadAllBytesAsync(recordPath);
            var record = JsonSerializer.Deserialize<PublicationRecord>(recordBytes)!;
            check(record.SourcePath == sourcePath && record.BackupPath is null && record.Source.Sha256 == hash &&
                await PublicationFiles.MatchesAsync(sourcePath, record.Source) && File.GetLastWriteTimeUtc(sourcePath) == modified,
                $"Audio app crash: original fingerprint and timestamp survive {operation}/{stage}");
            var committed = stage is "PublishedBeforeRecord" or "Committed";
            var expectedStage = stage == "PublishedBeforeRecord" ? PublicationStage.Publishing : Enum.Parse<PublicationStage>(stage);
            var candidatePath = committed ? record.OutputPath : record.TemporaryPath;
            check(record.Stage == expectedStage && File.Exists(record.OutputPath) == committed && File.Exists(record.TemporaryPath) != committed &&
                (stage == "Prepared" ? record.Candidate is null && new FileInfo(candidatePath).Length == 0 :
                    record.Candidate is not null && await PublicationFiles.MatchesAsync(candidatePath, record.Candidate)),
                $"Audio app crash: journal and candidate identify publication boundary {operation}/{stage}");
            var candidateHash = await Hash(candidatePath);
            if (operation == "optimize" && stage != "Prepared")
            {
                var candidateBytes = await File.ReadAllBytesAsync(candidatePath);
                FlacMetadata.RequirePreservedMetadata(FlacMetadata.Parse(flac), FlacMetadata.Parse(candidateBytes));
                check(candidateBytes.Length < flac.Length, "Audio app crash: smaller FLAC candidate retains original metadata at " + stage);
            }

            var identity = JsonSerializer.Deserialize<WorkerIdentity>(await File.ReadAllTextAsync(Path.Combine(directory, "worker.json")))!;
            Process? owned = null;
            try
            {
                try { owned = Process.GetProcessById(identity.Id); } catch (ArgumentException) { }
                if (owned is not null && !owned.HasExited && owned.StartTime.ToUniversalTime().Ticks == identity.StartTicks)
                    await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                check(owned is null || owned.HasExited || owned.StartTime.ToUniversalTime().Ticks != identity.StartTicks,
                    $"Audio app crash: owned worker exits after parent termination {operation}/{stage}");
            }
            finally { owned?.Dispose(); }

            var restartedWorker = new WorkerClient(executable, Path.Combine(directory, "restarted-worker"));
            await using var restarted = new MainViewModel(restartedWorker, publisher: publisher);
            var rediscoveredBytes = await File.ReadAllBytesAsync(recordPath);
            check(restarted.RecoveryNotice.Contains(publisher.RecordDirectory) && restarted.RecoveryNotice.StartsWith("1 publication recovery record") &&
                restartedWorker.ProcessId is null && publisher.FindRecoveryRecords().Single() == recordPath &&
                recordBytes.SequenceEqual(rediscoveredBytes),
                $"Audio app crash: restart reports and retains recovery evidence without starting a worker {operation}/{stage}");
            var resumed = await ExecuteAsync(directory, operation, restartedWorker, publisher);
            check(resumed.Publication is { Outcome: PublicationOutcome.CopyCreated, OutputPath: not null } &&
                (!committed || resumed.Publication.OutputPath != record.OutputPath),
                $"Audio app crash: fresh operation publishes a collision-safe copy {operation}/{stage}");
            var afterRetryBytes = await File.ReadAllBytesAsync(recordPath);
            check(await Hash(sourcePath) == hash && File.GetLastWriteTimeUtc(sourcePath) == modified &&
                await Hash(candidatePath) == candidateHash && recordBytes.SequenceEqual(afterRetryBytes) &&
                publisher.FindRecoveryRecords().Single() == recordPath,
                $"Audio app crash: fresh work leaves original, prior candidate/copy and old journal unchanged {operation}/{stage}");
            evidence.Add(new { operation, stage, childExitCode = child.ExitCode, identity, committed, record, candidateHash, resumed });
        }
        await File.WriteAllTextAsync(Path.Combine(root, "audio-publication-crashes.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static async Task<int> RunChildAsync(string root, string executable, string operation, string stage)
    {
        await using var worker = new WorkerClient(executable, Path.Combine(root, "worker-scratch"));
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle(), io: new ExitAt(root, stage));
        await ExecuteAsync(root, operation, worker, publisher, () =>
        {
            using var owned = Process.GetProcessById(worker.ProcessId!.Value);
            File.WriteAllText(Path.Combine(root, "worker.json"), JsonSerializer.Serialize(new WorkerIdentity(owned.Id, owned.StartTime.ToUniversalTime().Ticks)));
        });
        throw new InvalidOperationException("Audio publication crash checkpoint was not reached.");
    }

    private static async Task<FileResult> ExecuteAsync(string root, string operation, WorkerClient worker,
        OutputPublisher publisher, Action? afterProbe = null)
    {
        var access = new LocalTrialStore(Path.Combine(root, "trial.json"));
        if (operation == "convert")
        {
            var source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), Path.Combine(root, "Original \u00fc.wav")), AudioFormat.Flac, default);
            afterProbe?.Invoke();
            var plan = AudioConversionBatch.Create(Guid.NewGuid(), [source], AudioFormat.Flac, new("convert", new())).Confirm(AudioConversionConsent.None, false, false);
            return (await new AudioConversionExecutor(worker, publisher, access).ExecuteAsync(plan, null, default)).Results.Single();
        }
        if (operation == "optimize")
        {
            var source = await worker.ProbeFlacAsync(new(Guid.NewGuid(), Path.Combine(root, "Original \u00fc.flac")), default);
            afterProbe?.Invoke();
            var plan = FlacOptimizationPlan.Create(Guid.NewGuid(), [source], new("optimize", new())).Confirm(false, false);
            return (await new FlacOptimizationExecutor(worker, publisher, access).ExecuteAsync(plan, null, default)).Results.Single();
        }
        throw new ArgumentException("Unsupported audio crash-test operation.");
    }

    private static async Task<string> Hash(string path)
    {
        await using var input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input));
    }

    private sealed record WorkerIdentity(int Id, long StartTicks);
    private sealed class ExitAt(string root, string stage) : PublicationIo
    {
        public override void Checkpoint(PublicationStage current) { if (current.ToString() == stage) Exit(); }
        public override void Move(string source, string destination) { base.Move(source, destination); if (stage == "PublishedBeforeRecord") Exit(); }
        private void Exit() { File.WriteAllText(Path.Combine(root, "checkpoint.txt"), stage); Process.GetCurrentProcess().Kill(); }
    }
    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Audio publication crash tests never recycle.");
    }
}
