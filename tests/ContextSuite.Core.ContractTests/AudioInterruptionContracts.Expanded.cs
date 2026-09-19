using System.Diagnostics;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.ContractTests;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static partial class AudioInterruptionContracts
{
    internal sealed record ExpandedFixture(string Name, string Path, string Sha256, string Codec);

    public static async Task ExpandedAsync(string root, string executable, string manifest, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use new expanded recovery evidence.");
        var fixtures = JsonSerializer.Deserialize<ExpandedFixture[]>(await File.ReadAllTextAsync(manifest))!;
        if (fixtures.Length != 5 || fixtures.Select(value => value.Codec).Distinct().Count() != 5)
            throw new InvalidDataException("Expected five expanded input families.");
        Directory.CreateDirectory(root);
        var evidence = new List<object>();
        foreach (var fixture in fixtures)
        {
            if (Path.GetFileName(fixture.Name) != fixture.Name || await Hash(fixture.Path) != fixture.Sha256)
                throw new InvalidDataException("Fixture identity changed.");
            var directory = Path.Combine(root, fixture.Name); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "Original" + Path.GetExtension(fixture.Path)); File.Copy(fixture.Path, path);
            var modified = File.GetLastWriteTimeUtc(path);
            var outputs = Path.Combine(directory, "outputs"); Directory.CreateDirectory(outputs);
            var records = Path.Combine(directory, "records");
            var workerRoot = Path.Combine(directory, "workers");
            var publisher = new OutputPublisher(records, new NoRecycle());
            var clock = new ImageInterruptionContracts.DeadlineClock();
            await using var worker = new WorkerClient(executable, workerRoot, clock);
            var executor = new AudioConversionExecutor(worker, publisher, new LocalTrialStore(Path.Combine(directory, "trial.json")));
            var source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), path), AudioFormat.Flac, default);
            check(source.Facts.Streams.Single(value => value.Kind == "audio").Codec == fixture.Codec,
                fixture.Name + ": expected expanded input codec");
            var committed = new Dictionary<string, string>();
            await PublishAsync("baseline");

            foreach (var phase in new[] { "decode", "encode" })
            foreach (var fault in new[] { "cancel", "client-timeout", "worker-crash" })
            {
                var scenario = fixture.Name + "/" + phase + "/" + fault;
                source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), path), AudioFormat.Flac, default);
                var workerId = worker.ProcessId!.Value;
                using var ownedWorker = Process.GetProcessById(workerId);
                using var cancellation = new CancellationTokenSource();
                var operation = executor.ExecuteAsync(Plan(), null, cancellation.Token);
                Process? native = null;
                string? working = null;
                long before = 0, after = 0;
                try
                {
                    var voice = phase == "decode" && fixture.Codec == "wmavoice";
                    var filename = phase == "encode" ? "encoded.candidate" : voice ? "voice-decoded.wav" : "reference.f64le";
                    var nativePath = Path.Combine(Path.GetDirectoryName(executable)!, "audio-engine",
                        voice ? "voice/WmaVoiceMftProbe.exe" : "ffmpeg.exe");
                    nativePath = Path.GetFullPath(nativePath);
                    var watch = Stopwatch.StartNew();
                    while (!operation.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(25))
                    {
                        working = Directory.EnumerateFiles(workerRoot, filename, SearchOption.AllDirectories).SingleOrDefault();
                        if (working is not null && new FileInfo(working).Length > 4096)
                        {
                            native = PdfFailureContracts.FindChild(workerId, nativePath);
                            if (native is not null && !native.HasExited)
                            {
                                before = new FileInfo(working).Length;
                                await Task.Delay(2);
                                native.Refresh();
                                after = File.Exists(working) ? new FileInfo(working).Length : 0;
                                if (!operation.IsCompleted && !native.HasExited && after > before && native.TotalProcessorTime > TimeSpan.Zero) break;
                            }
                            native?.Dispose(); native = null;
                        }
                        await Task.Delay(2);
                    }
                    if (native is null || native.HasExited || operation.IsCompleted)
                        throw new InvalidOperationException("No live native process and growing working file observed before " + scenario +
                            (operation.IsCompletedSuccessfully ? ": " + (await operation).Results.Single().Message : ""));
                    var nativeId = native.Id;
                    var temporary = Path.Combine(outputs, $".context-suite-{source.ItemId:N}.tmp");
                    check(IsLocked(temporary), scenario + ": live native CPU, output growth and reservation lock precede injection");
                    if (fault == "cancel") cancellation.Cancel();
                    else if (fault == "client-timeout")
                    {
                        check(clock.Current!.Due == TimeSpan.FromSeconds(150), scenario + ": real client deadline is 150 seconds");
                        clock.Current.Expire();
                    }
                    else ownedWorker.Kill(); // Only this client's worker; its jobs must stop native work.
                    var interrupted = await operation.WaitAsync(TimeSpan.FromSeconds(10));
                    var row = interrupted.Results.Single();
                    check(row.State == (fault == "cancel" ? OperationState.Cancelled : OperationState.Failed) &&
                        row.Publication?.Outcome == (fault == "cancel" ? PublicationOutcome.Cancelled : PublicationOutcome.Failed),
                        scenario + ": application returns the distinct cancellation/failure result");
                    await native.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    await ownedWorker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    check(native.HasExited && ownedWorker.HasExited && worker.ProcessId is null,
                        scenario + ": owned worker and native process exited");
                    check(!File.Exists(working) && !File.Exists(temporary) &&
                        !Directory.EnumerateDirectories(workerRoot).Any() && !Directory.EnumerateFiles(records).Any() &&
                        Directory.EnumerateFiles(outputs).Count() == committed.Count,
                        scenario + ": incomplete work, reservation and journal removed");
                    var preserved = await Hash(path) == fixture.Sha256 && await Hash(fixture.Path) == fixture.Sha256 && File.GetLastWriteTimeUtc(path) == modified;
                    foreach (var output in committed) preserved &= await Hash(output.Key) == output.Value;
                    check(preserved, scenario + ": source and earlier published copies unchanged");
                    await PublishAsync(scenario);
                    check(worker.ProcessId is > 0 && worker.ProcessId != workerId, scenario + ": retry uses a fresh worker");
                    evidence.Add(new { fixture.Name, phase, fault, workerId, nativeId, before, after, row, committed = committed.Count });
                    await File.WriteAllTextAsync(Path.Combine(root, "expanded-recovery.json"), JsonSerializer.Serialize(
                        new { fixtures, evidence }, new JsonSerializerOptions { WriteIndented = true }));
                }
                finally
                {
                    cancellation.Cancel();
                    try { await operation; } catch (Exception) { }
                    native?.Dispose();
                }
            }

            ConfirmedAudioConversion Plan()
            {
                var batch = AudioConversionBatch.Create(Guid.NewGuid(), [source], AudioFormat.Flac, new("convert", new(OutputDirectory: outputs)));
                return batch.Confirm(batch.RequiredConsent, false, false);
            }

            async Task PublishAsync(string scenario)
            {
                source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), path), AudioFormat.Flac, default);
                var result = await executor.ExecuteAsync(Plan(), null, default);
                var publication = result.Results.Single().Publication;
                check(publication is { Outcome: PublicationOutcome.CopyCreated, OutputPath: not null },
                    fixture.Name + ": validated same-source FLAC publication after " + scenario);
                var output = publication!.OutputPath!;
                var reparsed = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), output), AudioFormat.Flac, default);
                check(AudioConversionPlan.Create(reparsed.Facts, AudioFormat.Flac).AlreadyTarget,
                    fixture.Name + ": published retry reparses as FLAC after " + scenario);
                committed.Add(output, await Hash(output));
            }
        }
    }
}
