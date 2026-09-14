using System.Diagnostics;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.ContractTests;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static partial class AudioInterruptionContracts
{
    internal static async Task FiniteResamplingAsync(string root, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use new finite-resampling interruption evidence.");
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "Original short.wav");
        File.Copy(Path.Combine(fixtures, "8000-2-256.wav"), sourcePath);
        var sourceHash = await Hash(sourcePath);
        var sourceTime = File.GetLastWriteTimeUtc(sourcePath);
        var workerRoot = Path.Combine(root, "workers");
        Directory.CreateDirectory(workerRoot);
        var records = Path.Combine(root, "records");
        var publisher = new OutputPublisher(records, new NoRecycle());
        var clock = new ImageInterruptionContracts.DeadlineClock();
        await using var worker = new WorkerClient(executable, workerRoot, clock);
        var executor = new AudioConversionExecutor(worker, publisher, new LocalTrialStore(Path.Combine(root, "trial.json")));
        var source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sourcePath), AudioFormat.Opus, default);
        var baseline = await executor.ExecuteAsync(Confirm(source), null, default);
        var published = baseline.Results.Single().Publication;
        check(published is { Outcome: PublicationOutcome.CopyCreated, OutputPath: not null },
            "Finite interruption: baseline publishes a validated Opus copy");
        var committed = new Dictionary<string, string> { [published!.OutputPath!] = await Hash(published.OutputPath!) };
        var evidence = new List<object>();
        foreach (var fault in new[] { "cancel", "client-timeout", "worker-crash" })
        {
            source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sourcePath), AudioFormat.Opus, default);
            var reservation = await publisher.ReserveAsync(new(source.ItemId, sourcePath, "opus", new("convert", new())));
            var workerId = worker.ProcessId!.Value;
            using var ownedWorker = Process.GetProcessById(workerId);
            _ = ownedWorker.Handle;
            if (!string.Equals(ownedWorker.MainModule!.FileName, Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected worker executable identity.");
            var created = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watcher = new FileSystemWatcher(workerRoot, "finite-extended.wav") { IncludeSubdirectories = true };
            watcher.Created += (_, args) => created.TrySetResult(args.FullPath);
            watcher.Error += (_, args) => created.TrySetException(args.GetException());
            watcher.EnableRaisingEvents = true;
            using var cancellation = new CancellationTokenSource();
            var encoding = AudioConversionPlan.Create(source.Facts, AudioFormat.Opus);
            var operation = worker.ConvertAudioAsync(new(source, reservation.TemporaryPath, AudioFormat.Opus, encoding.RequiredConsent), cancellation.Token);
            try
            {
                var workingFile = await created.Task.WaitAsync(TimeSpan.FromSeconds(15));
                if (operation.IsCompleted || ownedWorker.HasExited || !File.Exists(workingFile))
                    throw new InvalidOperationException("Did not observe active finite resampling before " + fault);
                // Inject immediately after observing the owned working file;
                // enumerating native children would race this very short phase.
                if (fault == "cancel") cancellation.Cancel();
                else if (fault == "client-timeout")
                {
                    check(clock.Current!.Due == TimeSpan.FromSeconds(150), "Finite interruption: production deadline remains 150 seconds");
                    clock.Current.Expire();
                }
                else ownedWorker.Kill();
                check(true, "Finite interruption: live worker created its real working file before " + fault);
                try
                {
                    await operation.WaitAsync(TimeSpan.FromSeconds(10));
                    throw new InvalidOperationException("Interrupted finite resampling unexpectedly completed.");
                }
                catch (OperationCanceledException) when (fault == "cancel")
                { check(true, "Finite interruption: cancellation has its distinct result"); }
                catch (MediaWorkerException error) when (fault != "cancel")
                {
                    check(error.Failure == (fault == "client-timeout" ? ImageFailure.TimedOut : ImageFailure.WorkerTerminated),
                        "Finite interruption: distinct typed result for " + fault);
                }
                await ownedWorker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                check(worker.ProcessId is null && ownedWorker.HasExited, "Finite interruption: owned worker exits after " + fault);
                var abandoned = await publisher.AbandonAsync(reservation, fault == "cancel");
                check(abandoned.Outcome == (fault == "cancel" ? PublicationOutcome.Cancelled : PublicationOutcome.Failed) &&
                    !File.Exists(workingFile) && !File.Exists(reservation.TemporaryPath) && !File.Exists(reservation.Record.OutputPath) &&
                    !Directory.EnumerateDirectories(workerRoot).Any() && !Directory.EnumerateFiles(records).Any(),
                    "Finite interruption: working files, reservation and journal are removed after " + fault);
                var preserved = await Hash(sourcePath) == sourceHash && File.GetLastWriteTimeUtc(sourcePath) == sourceTime;
                foreach (var item in committed) preserved &= await Hash(item.Key) == item.Value;
                check(preserved, "Finite interruption: original and committed copies survive " + fault);
                var retrySource = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), sourcePath), AudioFormat.Opus, default);
                var retry = await executor.ExecuteAsync(Confirm(retrySource), null, default);
                var retryOutput = retry.Results.Single().Publication;
                check(worker.ProcessId is > 0 && worker.ProcessId != workerId &&
                    retryOutput is { Outcome: PublicationOutcome.CopyCreated, OutputPath: not null },
                    "Finite interruption: fresh worker publishes a validated same-source retry after " + fault);
                committed.Add(retryOutput!.OutputPath!, await Hash(retryOutput.OutputPath!));
                evidence.Add(new { fault, workerId, workerStarted = ownedWorker.StartTime.ToUniversalTime(), workingFile,
                    abandoned, retryOutput = retryOutput.OutputPath });
                await File.WriteAllTextAsync(Path.Combine(root, "finite-interruptions.json"),
                    JsonSerializer.Serialize(new { sourcePath, sourceHash, sourceTime, committed, evidence }, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally
            {
                cancellation.Cancel();
                try { await operation; } catch (Exception) { }
                if (!reservation.Finished) await publisher.AbandonAsync(reservation, true);
            }
        }

        ConfirmedAudioConversion Confirm(AudioFileSource input)
        {
            var plan = AudioConversionBatch.Create(Guid.NewGuid(), [input], AudioFormat.Opus, new("convert", new()));
            return plan.Confirm(plan.RequiredConsent, false, false);
        }
    }
}
