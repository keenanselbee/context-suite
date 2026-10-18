using System.Security.Cryptography;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class VideoAudioWorkflowContracts
{
    internal static async Task RunAsync(string root, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use a fresh video audio workflow directory.");
        Directory.CreateDirectory(root);
        var settings = new SuiteSettings { Convert = new(ReplaceOriginals: true), PlayCompletionSound = false };
        foreach (var extension in new[] { "mp4", "mov", "webm", "mkv" })
        {
            var directory = Path.Combine(root, extension); Directory.CreateDirectory(directory);
            var source = Copy("two-tracks." + extension, directory);
            var hash = SHA256.HashData(File.ReadAllBytes(source));
            var access = new CountingAccess();
            var worker = new WorkerClient(executable, Path.Combine(directory, "worker"));
            await using var view = new MainViewModel(worker, settings,
                new OutputPublisher(Path.Combine(directory, "records"), new NoRecycle()), access);
            var choices = 0;
            view.VideoAudioTrackRequested += (decision, _) =>
            {
                choices++;
                check(decision.Tracks.Count == 2 && decision.Tracks[1].Label.Contains("fra", StringComparison.OrdinalIgnoreCase),
                    extension + " track picker includes both tracks and the second track's language");
                return Task.FromResult<VideoAudioSelection?>(decision.Tracks[1].Selection);
            };
            view.AudioConversionRequested += (_, _) => throw new InvalidOperationException("Decoded-precision WAV needs no quality consent.");
            view.Admit(new(Guid.NewGuid(), "convert", "wav", [source]));
            await view.WaitForIdleAsync();
            var row = view.Rows.Single();
            check(row.Result.State == OperationState.Succeeded && row.Result.Publication?.IsCommitted == true &&
                row.VideoAudioSource?.Video is not null && choices == 1 && access.Admissions == 1,
                extension + " uses the selected track with one picker and one admission: " + row.Result.Message);
            using var output = File.OpenRead(row.OutputPath);
            var wave = await WaveMetadata.ReadAsync(output, CancellationToken.None, true);
            check(wave.Channels == 1 && wave.SampleRate == 44100 && wave.FloatingPoint &&
                wave.SampleFrames > 40000 && SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(hash),
                extension + " extracts the distinct second track and keeps the original despite overwrite settings");
            check(row.Result.Message.Contains("timing offset", StringComparison.Ordinal),
                extension + " explains that standalone audio omits its video start offset");
        }

        var singleDirectory = Path.Combine(root, "single"); Directory.CreateDirectory(singleDirectory);
        var single = Copy("pcm_s16le-in-mov.mov", singleDirectory);
        var singleAccess = new CountingAccess();
        await using (var view = new MainViewModel(new WorkerClient(executable, Path.Combine(singleDirectory, "worker")), settings,
            new OutputPublisher(Path.Combine(singleDirectory, "records"), new NoRecycle()), singleAccess))
        {
            view.VideoAudioTrackRequested += (_, _) => throw new InvalidOperationException("Single audio track must not show a picker.");
            view.AudioConversionRequested += (_, _) => throw new InvalidOperationException("PCM extraction must not show a quality prompt.");
            view.Admit(new(Guid.NewGuid(), "convert", "wav", [single]));
            await view.WaitForIdleAsync();
            check(view.Rows.Single().Result.State == OperationState.Succeeded && singleAccess.Admissions == 1,
                "single PCM video track converts directly without redundant choices");
        }

        var silentDirectory = Path.Combine(root, "silent"); Directory.CreateDirectory(silentDirectory);
        var silent = Copy("no-audio.mp4", silentDirectory);
        var silentAccess = new CountingAccess();
        await using (var view = new MainViewModel(new WorkerClient(executable, Path.Combine(silentDirectory, "worker")), settings,
            new OutputPublisher(Path.Combine(silentDirectory, "records"), new NoRecycle()), silentAccess))
        {
            view.Admit(new(Guid.NewGuid(), "convert", "wav", [silent]));
            await view.WaitForIdleAsync();
            check(view.Rows.Single().Result.State == OperationState.Unsupported &&
                view.Rows.Single().Result.Message.Contains("no audio track", StringComparison.Ordinal) && silentAccess.Admissions == 0,
                "silent video reports no audio without consuming a conversion admission");
        }

        var failureDirectory = Path.Combine(root, "refusals"); Directory.CreateDirectory(failureDirectory);
        var malformed = Path.Combine(failureDirectory, "malformed.mkv"); File.WriteAllText(malformed, "Not a video container.");
        var mismatch = Path.Combine(failureDirectory, "mislabeled.webm"); File.Copy(single, mismatch);
        var malformedAccess = new CountingAccess();
        await using (var view = new MainViewModel(new WorkerClient(executable, Path.Combine(failureDirectory, "worker")), settings,
            new OutputPublisher(Path.Combine(failureDirectory, "records"), new NoRecycle()), malformedAccess))
        {
            view.Admit(new(Guid.NewGuid(), "convert", "wav", [malformed, mismatch]));
            await view.WaitForIdleAsync();
            check(view.Rows.All(row => row.Result.State is OperationState.Failed or OperationState.Unsupported) &&
                malformedAccess.Admissions == 0 && Directory.GetFiles(failureDirectory, "*.wav").Length == 0 &&
                File.ReadAllText(malformed) == "Not a video container.", "malformed and mismatched video containers fail without admission or output");
        }
        var missingAccess = new CountingAccess();
        var missingWorker = new WorkerClient(Path.Combine(failureDirectory, "no-engine", "ContextSuite.Worker.exe"));
        await using (var view = new MainViewModel(missingWorker, settings,
            new OutputPublisher(Path.Combine(failureDirectory, "missing-records"), new NoRecycle()), missingAccess))
        {
            view.Admit(new(Guid.NewGuid(), "convert", "wav", [single]));
            await view.WaitForIdleAsync();
            check(view.Rows.Single().Result.State == OperationState.Unsupported && missingAccess.Admissions == 0 &&
                missingWorker.ProcessId is null, "missing video audio engine refuses before worker launch and admission");
        }
        var declinedAccess = new CountingAccess();
        await using (var view = new MainViewModel(new WorkerClient(executable, Path.Combine(failureDirectory, "declined-worker")), settings,
            new OutputPublisher(Path.Combine(failureDirectory, "declined-records"), new NoRecycle()), declinedAccess))
        {
            var source = Copy("chapter-artwork.mp4", failureDirectory);
            view.VideoAudioTrackRequested += (decision, _) => Task.FromResult<VideoAudioSelection?>(decision.Tracks[0].Selection);
            view.AudioConversionRequested += (_, _) => Task.FromResult<ConfirmedAudioConversion?>(null);
            view.Admit(new(Guid.NewGuid(), "convert", "wav", [source]));
            await view.WaitForIdleAsync();
            check(view.Rows.Single().Result.State == OperationState.Cancelled && declinedAccess.Admissions == 0 &&
                Directory.GetFiles(failureDirectory, "*.wav").Length == 0, "declining metadata omission cancels video extraction without a stuck running row");
        }

        foreach (var changeSource in new[] { false, true })
        {
            var directory = Path.Combine(root, changeSource ? "changed-retry" : "retry"); Directory.CreateDirectory(directory);
            var source = Copy("two-tracks.mp4", directory);
            var access = new CountingAccess();
            await using var view = new MainViewModel(new WorkerClient(executable, Path.Combine(directory, "worker")), settings,
                new OutputPublisher(Path.Combine(directory, "records"), new NoRecycle(), io: new FailOutputOnce()), access);
            var choices = 0;
            view.VideoAudioTrackRequested += (decision, _) =>
            { choices++; return Task.FromResult<VideoAudioSelection?>(decision.Tracks[1].Selection); };
            view.Admit(new(Guid.NewGuid(), "convert", "wav", [source]));
            await view.WaitForIdleAsync();
            check(view.Rows.Single().Result.State == OperationState.Failed && view.CanRetry,
                "video audio publication failure is retryable and keeps its selected track");
            var selected = view.Rows.Single().VideoAudioSource!.Video;
            var changedOutput = Path.Combine(directory, "changed-settings");
            Directory.CreateDirectory(changedOutput);
            view.Settings = settings with { Convert = new(OutputDirectory: changedOutput) };
            if (changeSource) File.Copy(Path.Combine(fixtures, "flac-in-mp4.mp4"), source, true);
            view.RetryFailed();
            await view.WaitForIdleAsync();
            var retried = view.Rows.Last();
            check(choices == 1 && (changeSource ? retried.VideoAudioResumeBlocked && !view.CanRetry &&
                    Directory.GetFiles(directory, "*.wav").Length == 0 :
                    retried.Result.State == OperationState.Succeeded && retried.VideoAudioSource!.Video == selected &&
                    Directory.GetFiles(directory, "*.wav").Length == 1),
                changeSource ? "video audio retry refuses changed source identity without silently choosing another track" :
                    "video audio retry reuses the confirmed track and output settings, then publishes one output");
        }

        var mixedDirectory = Path.Combine(root, "mixed"); Directory.CreateDirectory(mixedDirectory);
        var clip = Copy("two-tracks.webm", mixedDirectory);
        var audio = Copy("first.wav", mixedDirectory);
        var mixedAccess = new CountingAccess();
        await using (var mixed = new MainViewModel(new WorkerClient(executable, Path.Combine(mixedDirectory, "worker")), settings,
            new OutputPublisher(Path.Combine(mixedDirectory, "records"), new NoRecycle()), mixedAccess))
        {
            var prompts = 0;
            mixed.VideoAudioTrackRequested += (decision, _) => Task.FromResult<VideoAudioSelection?>(decision.Tracks[0].Selection);
            mixed.AudioConversionRequested += (decision, _) =>
            {
                prompts++;
                return Task.FromResult<ConfirmedAudioConversion?>(decision.Plan.Confirm(decision.Plan.RequiredConsent, false, false));
            };
            mixed.Admit(new(Guid.NewGuid(), "convert", "flac", [clip, audio]));
            await mixed.WaitForIdleAsync();
            check(mixed.Rows.All(row => row.Result.State == OperationState.Succeeded) && mixedAccess.Admissions == 1 &&
                prompts == 1 && File.Exists(clip) && File.Exists(audio),
                "video and ordinary audio share one quality decision and access admission on copies");
        }

        foreach (var terminateWorker in new[] { false, true })
        {
            var directory = Path.Combine(root, terminateWorker ? "worker-stop" : "cancel"); Directory.CreateDirectory(directory);
            var first = Copy("pcm_s16le-in-mov.mov", directory);
            var second = Copy("long-recording.mkv", directory);
            var access = new CountingAccess();
            await using var worker = new WorkerClient(executable, Path.Combine(directory, "worker"));
            var sources = new List<AudioFileSource>();
            foreach (var path in new[] { first, second })
            {
                var source = await worker.ProbeVideoAudioAsync(new(Guid.NewGuid(), path), default);
                sources.Add(source with { Video = new(source.Facts.Streams.Single(stream => stream.Kind == "audio").Index) });
            }
            var batch = AudioConversionBatch.Create(Guid.NewGuid(), sources, AudioFormat.Wave, new("convert", settings.Convert));
            var publisher = new OutputPublisher(Path.Combine(directory, "records"), new NoRecycle());
            var executor = new AudioConversionExecutor(worker, publisher, access);
            using var cancellation = new CancellationTokenSource();
            Task? interrupt = null;
            var result = await executor.ExecuteAsync(batch.Confirm(0, false, false), (item, progress) =>
            {
                if (item.Source.Path != second || progress.Message != "Converting and verifying audio") return;
                var processId = worker.ProcessId ?? throw new InvalidOperationException("Probed worker must be running.");
                interrupt = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    if (terminateWorker)
                    {
                        using var process = System.Diagnostics.Process.GetProcessById(processId);
                        process.Kill(true);
                    }
                    else cancellation.Cancel();
                });
            }, cancellation.Token);
            if (interrupt is null) throw new InvalidOperationException("The second extraction did not start.");
            await interrupt;
            check(result.Results[0].State == OperationState.Succeeded &&
                result.Results[1].State == (terminateWorker ? OperationState.Failed : OperationState.Cancelled) &&
                Directory.GetFiles(directory, "*.wav").Length == 1 && File.Exists(first) && File.Exists(second),
                "video " + (terminateWorker ? "worker termination" : "cancellation") + " retains committed output and both originals");
            var committed = Directory.GetFiles(directory, "*.wav").Single();
            var committedHash = SHA256.HashData(File.ReadAllBytes(committed));
            var retry = AudioConversionBatch.Create(Guid.NewGuid(), [sources[1]], AudioFormat.Wave, batch.Settings);
            var resumed = await executor.ExecuteAsync(retry.Confirm(0, false, false), null, default);
            check(resumed.Results.Single().State == OperationState.Succeeded && Directory.GetFiles(directory, "*.wav").Length == 2 &&
                SHA256.HashData(File.ReadAllBytes(committed)).SequenceEqual(committedHash),
                "video interrupted extraction retries only the unfinished item and keeps the earlier output unchanged");
        }

        string Copy(string name, string directory)
        {
            var destination = Path.Combine(directory, name);
            File.Copy(Path.Combine(fixtures, name), destination);
            return destination;
        }
    }

    private sealed class FailOutputOnce : PublicationIo
    {
        private bool _failed;
        public override void Move(string source, string destination)
        {
            if (!_failed && destination.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            { _failed = true; throw new IOException("Injected video audio publication failure."); }
            base.Move(source, destination);
        }
    }
    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Video audio extraction must keep originals.");
    }
    private sealed class CountingAccess : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => Task.FromResult(new OperationAccessStatus(true, "Available"));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedAudioConversion confirmed, CancellationToken token)
        { Admissions++; return Task.FromResult(new OperationAdmission(new(true, "Available"), confirmed.Plan.BatchId)); }
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new NotSupportedException();
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new NotSupportedException();
    }
}
