using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static class WwiseWorkflowContracts
{
    public static async Task RunAsync(string scratch, string executable, string fixtures, string console, Action<bool, string> check)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use a new Wwise workflow evidence directory.");
        Directory.CreateDirectory(scratch);
        var installation = await WwiseInstallation.InspectAsync(console);
        var paths = Directory.GetFiles(Path.Combine(fixtures, "inputs"), "*.wav").Order().Select(path =>
        {
            var copied = Path.Combine(scratch, Path.GetFileName(path)); File.Copy(path, copied); return copied;
        }).ToArray();
        if (paths.Length != 4) throw new InvalidDataException("Use the four authored Wwise source WAVs.");
        var access = new CountingAccess(new LocalTrialStore(Path.Combine(scratch, "access", "trial.json")));
        var records = Path.Combine(scratch, "publications");
        var publisher = new OutputPublisher(records, new ForbiddenRecycle());
        await using var worker = new WorkerClient(executable, Path.Combine(scratch, "workers"));
        var hasAudio = worker.HasAudioConverter;
        check(worker.HasWemDecoder, "Wwise: installed-tool encoding finds the isolated decoder");
        if (!hasAudio) check(true, "Wwise: PCM and float WAV encoding does not require FFmpeg");
        var sources = new List<AudioFileSource>();
        foreach (var path in paths) sources.Add(await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), path), AudioFormat.Wem, default));
        var workerId = worker.ProcessId;
        var executor = new AudioConversionExecutor(worker, publisher, access);
        ConfirmedAudioConversion Plan(IEnumerable<AudioFileSource> selection, WemEncodingOptions options)
        {
            var plan = AudioConversionBatch.Create(Guid.NewGuid(), selection, AudioFormat.Wem, new("convert", new()), wem: options);
            return plan.Confirm(plan.RequiredConsent, false, false);
        }
        foreach (var codec in new[] { WemCodec.Pcm16, WemCodec.Vorbis })
        {
            var options = new WemEncodingOptions(installation, codec);
            var batch = await executor.ExecuteAsync(Plan(sources, options), null, default);
            check(batch.Results.Count == 4 && batch.Results.All(row => row.Publication is { Outcome: PublicationOutcome.CopyCreated }),
                "Wwise: all four " + codec + " outputs publish validated copies");
            for (var i = 0; i < sources.Count; i++)
            {
                var output = batch.Results[i].Publication!.OutputPath!;
                using var file = File.OpenRead(output); var header = await WemHeader.ReadAsync(file);
                var stream = sources[i].Facts.Streams[0];
                check(header.Codec == codec && header.SampleRate == stream.SampleRate && header.Channels == stream.Channels &&
                    header.Loop is { StartSample: 100 } && header.SampleCount == stream.SampleRate / 4,
                    "Wwise: codec/rate/channels/frame count/source loop verified " + codec + "/" + i);
                check(await Hash(sources[i].Path) == sources[i].Sha256 && worker.ProcessId == workerId,
                    "Wwise: original WAV retained and worker reused " + codec + "/" + i);
            }
        }
        check(access.Admissions == 2, "Wwise: one access admission per four-item batch");

        var floatPath = Path.Combine(scratch, "authored-float-48000-1ch.wav");
        WriteFloatWave(floatPath);
        var floating = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), floatPath), AudioFormat.Wem, default);
        check(floating.Facts.Streams.Single().Codec == "pcm_f32le" && floating.Facts.Streams.Single().SampleBits == 32,
            "Wwise: authored float32 WAV probes without FFmpeg");
        var vorbisSource = Path.Combine(scratch, "decoded-vorbis-source.wem");
        File.Copy(Path.Combine(fixtures, "output-vorbis", "Windows", "sine-48000-1ch.wem"), vorbisSource);
        var decodedSource = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), vorbisSource), AudioFormat.Wave, default);
        var decoded = await executor.ExecuteAsync(AudioConversionBatch.Create(Guid.NewGuid(), [decodedSource],
            AudioFormat.Wave, new("convert", new())).Confirm(0, false, false), null, default);
        var decodedPath = decoded.Results.Single().Publication?.OutputPath ?? throw new InvalidDataException("Vorbis WEM did not decode to WAV.");
        var decodedFloat = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), decodedPath), AudioFormat.Wem, default);
        check(decodedFloat.Facts.Streams.Single().Codec == "pcm_f32le" && decodedFloat.Facts.Streams.Single().SampleBits == 32,
            "Wwise: decoded Vorbis WEM supplies looped float32 WAV without FFmpeg");
        foreach (var codec in new[] { WemCodec.Pcm16, WemCodec.Vorbis })
        {
            var options = new WemEncodingOptions(installation, codec);
            var floatBatch = AudioConversionBatch.Create(Guid.NewGuid(), [floating, decodedFloat], AudioFormat.Wem,
                new("convert", new()), wem: options);
            check(floatBatch.RequiredConsent == AudioConversionConsent.PrecisionReduction && floatBatch.HasExecutableItems,
                "Wwise: float WAV to " + codec + " requires one explicit PCM16 normalization decision");
            var result = await executor.ExecuteAsync(floatBatch.Confirm(AudioConversionConsent.PrecisionReduction, false, false), null, default);
            check(result.Results.Count == 2 && result.Results.All(row => row.Publication is { Outcome: PublicationOutcome.CopyCreated }),
                "Wwise: authored and decoded float WAV publish " + codec + " copies");
            for (var i = 0; i < 2; i++)
            {
                var source = i == 0 ? floating : decodedFloat;
                using var output = File.OpenRead(result.Results[i].Publication!.OutputPath!);
                var header = await WemHeader.ReadAsync(output);
                check(header.Codec == codec && header.SampleRate == 48000 && header.Channels == 1 &&
                    header.Loop is { StartSample: 100 } &&
                    header.Loop.EndSampleExclusive == (i == 0 ? 4001 : 11900) &&
                    header.SampleCount == 12000 &&
                    await Hash(source.Path) == source.Sha256,
                    "Wwise: normalized float output retains exact frames/loop and original hash " + codec + "/" + i);
            }
        }
        var changedFloatPath = Path.Combine(scratch, "changed-float.wav");
        File.Copy(floatPath, changedFloatPath);
        var changedFloat = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), changedFloatPath), AudioFormat.Wem, default);
        await File.AppendAllTextAsync(changedFloatPath, "changed");
        var changedFloatHash = await Hash(changedFloatPath);
        var refusedFloat = await executor.ExecuteAsync(Plan([changedFloat], new(installation, WemCodec.Pcm16)), null, default);
        check(refusedFloat.Results.Single().State == OperationState.Failed &&
            refusedFloat.Results.Single().Publication?.IsCommitted != true && await Hash(changedFloatPath) == changedFloatHash,
            "Wwise: changed float source cannot publish and original bytes remain");
        foreach (var invalidValue in new[] { float.NaN, 1.2f })
        {
            var invalidPath = Path.Combine(scratch, float.IsNaN(invalidValue) ? "float-nan.wav" : "float-clipped.wav");
            WriteFloatWave(invalidPath, invalidValue);
            var originalHash = await Hash(invalidPath);
            var rejected = false;
            try
            {
                var invalid = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), invalidPath), AudioFormat.Wem, default);
                var outcome = await executor.ExecuteAsync(Plan([invalid], new(installation, WemCodec.Pcm16)), null, default);
                rejected = outcome.Results.Single().State is OperationState.Failed or OperationState.Unsupported &&
                    outcome.Results.Single().Publication?.IsCommitted != true;
            }
            catch (MediaWorkerException) { rejected = true; }
            check(rejected && await Hash(invalidPath) == originalHash &&
                !Directory.GetFiles(scratch, ".context-suite-*.tmp", SearchOption.AllDirectories).Any(),
                "Wwise: nonfinite or clipping float input is refused without output or reservation");
        }
        if (hasAudio)
        {
            var ffmpeg = Path.Combine(Path.GetDirectoryName(executable)!, "audio-engine", "ffmpeg.exe");
            var mp3Paths = new[] { Path.Combine(scratch, "authored-44100-1ch.mp3"),
                Path.Combine(scratch, "authored-48000-2ch.mp3") };
            await EncodeMp3(ffmpeg, paths[0], mp3Paths[0]);
            await EncodeMp3(ffmpeg, paths[3], mp3Paths[1]);
            var mp3Sources = new List<AudioFileSource>();
            foreach (var path in mp3Paths)
            {
                var source = await worker.ProbeAudioFileAsync(new(Guid.NewGuid(), path), AudioFormat.Wem, default);
                check(source.Facts.Container == "mp3" && source.Facts.Streams.Single().Codec == "mp3" &&
                    source.Sha256 == await Hash(path), "Wwise: curated FFmpeg authored MP3 has typed facts and fingerprint");
                mp3Sources.Add(source);
            }
            foreach (var codec in new[] { WemCodec.Pcm16, WemCodec.Vorbis })
            {
                var options = new WemEncodingOptions(installation, codec);
                var consent = AudioConversionConsent.PrecisionReduction |
                    (codec == WemCodec.Vorbis ? AudioConversionConsent.LossyTranscoding : 0);
                var plan = AudioConversionBatch.Create(Guid.NewGuid(), mp3Sources, AudioFormat.Wem,
                    new("convert", new()), wem: options);
                check(plan.RequiredConsent == consent && plan.HasExecutableItems,
                    "Wwise: MP3 to " + codec + " requires only explicit applicable quality consent");
                var result = await executor.ExecuteAsync(plan.Confirm(consent, false, false), null, default);
                check(result.Results.Count == 2 && result.Results.All(row => row.Publication is { Outcome: PublicationOutcome.CopyCreated }),
                    "Wwise: MP3 to " + codec + " publishes validated WEM copies");
                for (var i = 0; i < 2; i++)
                {
                    using var output = File.OpenRead(result.Results[i].Publication!.OutputPath!);
                    var header = await WemHeader.ReadAsync(output);
                    var audio = mp3Sources[i].Facts.Streams.Single();
                    check(header.Codec == codec && header.SampleRate == audio.SampleRate && header.Channels == audio.Channels &&
                        header.SampleCount == audio.SampleRate / 4 && await Hash(mp3Sources[i].Path) == mp3Sources[i].Sha256,
                        "Wwise: published MP3 input retains exact quarter-second frames and source bytes " + codec + "/" + i);
                }
            }
            var beforePrompt = access.Admissions;
            var qualityOpened = new TaskCompletionSource<AudioConversionViewModel>(TaskCreationOptions.RunContinuationsAsynchronously);
            var qualityDecision = new TaskCompletionSource<ConfirmedAudioConversion?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var promptWorker = new WorkerClient(executable, Path.Combine(scratch, "quality-prompt-workers"));
            await using (var prompted = new MainViewModel(promptWorker, new() { PlayCompletionSound = false }, publisher, access))
            {
                prompted.WemEncodingRequested += _ => Task.FromResult<WemEncodingOptions?>(new(installation, WemCodec.Vorbis));
                var prompts = 0;
                prompted.AudioConversionRequested += (decision, token) =>
                { prompts++; qualityOpened.TrySetResult(decision); return qualityDecision.Task.WaitAsync(token); };
                check(prompted.Admit(new(Guid.NewGuid(), "convert", "wem", [floatPath, mp3Paths[0]])).Accepted,
                    "Wwise: mixed float WAV/MP3 action enters one pending batch");
                var decision = await qualityOpened.Task.WaitAsync(TimeSpan.FromSeconds(15));
                check(prompts == 1 && decision.Plan.RequiredConsent ==
                    (AudioConversionConsent.PrecisionReduction | AudioConversionConsent.LossyTranscoding) &&
                    decision.QualityChanges.Contains("16-bit") && decision.QualityChanges.Contains("quality loss") &&
                    access.Admissions == beforePrompt && prompted.Rows.All(row => row.Result.Publication is null),
                    "Wwise: one visible quality decision precedes admission and any mixed output");
                qualityDecision.SetResult(decision.Plan.Confirm(decision.Plan.RequiredConsent, false, false));
                await prompted.WaitForIdleAsync();
                check(prompts == 1 && access.Admissions == beforePrompt + 1 &&
                    prompted.Rows.All(row => row.Result.Publication is { Outcome: PublicationOutcome.CopyCreated }),
                    "Wwise: explicit quality acceptance admits mixed batch once and publishes both");
            }
            beforePrompt = access.Admissions;
            var cancelPromptWorker = new WorkerClient(executable, Path.Combine(scratch, "cancel-quality-workers"));
            await using (var cancelledPrompt = new MainViewModel(cancelPromptWorker, new() { PlayCompletionSound = false }, publisher, access))
            {
                cancelledPrompt.WemEncodingRequested += _ => Task.FromResult<WemEncodingOptions?>(new(installation, WemCodec.Vorbis));
                var prompts = 0;
                cancelledPrompt.AudioConversionRequested += (_, _) => { prompts++; return Task.FromResult<ConfirmedAudioConversion?>(null); };
                check(cancelledPrompt.Admit(new(Guid.NewGuid(), "convert", "wem", [mp3Paths[1]])).Accepted,
                    "Wwise: MP3 quality-cancel action enters pending batch");
                await cancelledPrompt.WaitForIdleAsync();
                check(prompts == 1 && access.Admissions == beforePrompt &&
                    cancelledPrompt.Rows.Single().Result.State == OperationState.Cancelled &&
                    cancelledPrompt.Rows.Single().Result.Publication is null,
                    "Wwise: cancelling quality prompt makes no access admission or output");
            }
        }
        var referencePath = Path.Combine(scratch, "match.wem");
        File.Copy(Path.Combine(fixtures, "output-vorbis", "Windows", Path.GetFileNameWithoutExtension(paths[0]) + ".wem"), referencePath);
        var reference = await WemReference.InspectAsync(referencePath);
        var matched = new WemEncodingOptions(installation, WemCodec.Vorbis, reference);
        var match = await executor.ExecuteAsync(Plan([sources[0]], matched), null, default);
        check(match.Results.Single().Publication is { Outcome: PublicationOutcome.CopyCreated }, "Wwise: matching original properties executes and publishes");
        await File.AppendAllTextAsync(referencePath, "changed");
        var badReference = await executor.ExecuteAsync(Plan([sources[0]], matched), null, default);
        check(badReference.Results.Single().State == OperationState.Failed && badReference.Results.Single().Publication?.IsCommitted != true,
            "Wwise: changed reference cannot publish using stale matching facts");
        var pcm = new WemEncodingOptions(installation, WemCodec.Pcm16);
        var badTool = await executor.ExecuteAsync(Plan([sources[0]], pcm with { Installation = installation with { ExecutableSha256 = new string('F', 64) } }), null, default);
        check(badTool.Results.Single().State == OperationState.Failed && badTool.Results.Single().Publication?.IsCommitted != true,
            "Wwise: changed console identity refuses publication");

        var before = access.Admissions;
        var pending = new TaskCompletionSource<WemEncodingOptions?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var view = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access);
        view.WemEncodingRequested += token => { opened.TrySetResult(); return pending.Task.WaitAsync(token); };
        check(view.Admit(new(Guid.NewGuid(), "convert", "wem", [paths[0], paths[1]])).Accepted, "Wwise: direct WEM action starts a coordinated batch");
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
        check(view.IsBusy && access.Admissions == before && view.Rows.All(row => row.Result.Publication is null),
            "Wwise: pending setup performs no conversion or access admission");
        pending.SetResult(pcm);
        await view.WaitForIdleAsync();
        check(view.Rows.Count == 2 && view.Rows.All(row => row.Result.Publication is { Outcome: PublicationOutcome.CopyCreated }) &&
            access.Admissions == before + 1 && worker.ProcessId == workerId, "Wwise: completed setup resumes the original two-file batch once");
        var cancelledSetup = new TaskCompletionSource<WemEncodingOptions?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // A separate view owns a separate worker but never starts it while setup is pending.
        await using var cancelWorker = new WorkerClient(executable, Path.Combine(scratch, "cancel-workers"));
        await using var cancelView = new MainViewModel(cancelWorker, new() { PlayCompletionSound = false }, publisher, access);
        cancelView.WemEncodingRequested += token => { cancelledOpened.TrySetResult(); return cancelledSetup.Task.WaitAsync(token); };
        before = access.Admissions;
        cancelView.Admit(new(Guid.NewGuid(), "convert", "wem", [paths[0]]));
        await cancelledOpened.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancelView.CancelCommand.Execute(null);
        await cancelView.WaitForIdleAsync();
        check(cancelView.Rows.Single().Result.State == OperationState.Cancelled && access.Admissions == before && cancelWorker.ProcessId is null,
            "Wwise: cancellation during setup starts no worker and keeps the source");
        check(Directory.GetFiles(records).Length == 0 && !Directory.GetFiles(scratch, ".context-suite-*.tmp", SearchOption.AllDirectories).Any(),
            "Wwise: completed and rejected work leaves no publication reservations");
    }

    private static void WriteFloatWave(string path, float? invalid = null)
    {
        const int rate = 48000, frames = 12000;
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(file, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write((uint)(116 + frames * 4));
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt ")); writer.Write((uint)16);
        writer.Write((ushort)3); writer.Write((ushort)1); writer.Write((uint)rate);
        writer.Write((uint)(rate * 4)); writer.Write((ushort)4); writer.Write((ushort)32);
        writer.Write(Encoding.ASCII.GetBytes("fact")); writer.Write((uint)4); writer.Write((uint)frames);
        writer.Write(Encoding.ASCII.GetBytes("smpl")); writer.Write((uint)60);
        writer.Write((uint)0); writer.Write((uint)0); writer.Write((uint)(1_000_000_000 / rate));
        writer.Write((uint)60); writer.Write((uint)0); writer.Write((uint)0); writer.Write((uint)0);
        writer.Write((uint)1); writer.Write((uint)0);
        writer.Write((uint)0); writer.Write((uint)0); writer.Write((uint)100); writer.Write((uint)4000);
        writer.Write((uint)0); writer.Write((uint)0);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write((uint)(frames * 4));
        for (var frame = 0; frame < frames; frame++)
            writer.Write(frame == 250 && invalid is { } bad ? bad :
                (float)(0.4 * Math.Sin(frame * 2 * Math.PI * 440 / rate)));
    }

    private static async Task EncodeMp3(string ffmpeg, string input, string output)
    {
        var start = new ProcessStartInfo(ffmpeg)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-threads", "1",
            "-i", input, "-map", "0:a:0", "-map_metadata", "-1", "-codec:a", "libmp3lame",
            "-b:a", "192k", "-id3v2_version", "0", "-write_id3v1", "0", output })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Curated FFmpeg did not start for authored MP3.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var errors = process.StandardError.ReadToEndAsync(deadline.Token);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Curated FFmpeg exceeded the authored MP3 test deadline.");
        }
        if (process.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
            throw new InvalidDataException("Curated FFmpeg could not author the MP3 fixture: " + await errors);
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
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) => throw new InvalidOperationException();
    }
}
