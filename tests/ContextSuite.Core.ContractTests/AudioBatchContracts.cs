using System.Collections.Immutable;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Transport;

internal static class AudioBatchContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var facts = new AudioProbeFacts("mp3", 2, [new(0, "audio", "mp3", 44100, 2, null, null, 2, "stereo", ImmutableDictionary<string, string>.Empty)], ImmutableDictionary<string, string>.Empty);
        var source = new AudioFileSource(Guid.NewGuid(), Path.Combine(scratch, "planned.mp3"), new string('A', 64), 5000, facts);
        var plan = AudioConversionBatch.Create(Guid.NewGuid(), [source], AudioFormat.Opus, new("convert", new()));
        var consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling;
        var confirmed = plan.Confirm(consent, false, false);
        check(plan.RequiredConsent == consent && confirmed.AcceptedConsent == consent && plan.Notices.Length == 2 && !plan.ReplaceOriginal,
            "Audio batch: fixed target, combined quality choices and copy-default snapshot");
        Reject(() => plan.Confirm(AudioConversionConsent.None, false, false), "missing quality acknowledgement");
        Reject(() => plan.Confirm(AudioConversionConsent.LossyTranscoding, false, false), "partial quality acknowledgement");
        Reject(() => plan.Confirm((AudioConversionConsent)128, false, false), "unknown consent flags");
        Reject(() => AudioConversionBatch.Create(Guid.Empty, [source], AudioFormat.Wave, new("convert", new())), "empty batch identity");
        Reject(() => AudioConversionBatch.Create(Guid.NewGuid(), [], AudioFormat.Wave, new("convert", new())), "empty selection");
        Reject(() => AudioConversionBatch.Create(Guid.NewGuid(), [source, source], AudioFormat.Wave, new("convert", new())), "duplicate item identity");
        Reject(() => AudioConversionBatch.Create(Guid.NewGuid(), [source], (AudioFormat)999, new("convert", new())), "unknown target");
        Reject(() => AudioConversionBatch.Create(Guid.NewGuid(), [source], AudioFormat.Wave, new("optimize", new())), "wrong settings operation");
        Reject(() => (plan with { Items = default }).Confirm(consent, false, false), "missing confirmation items");
        Reject(() => (plan with { Items = [null!] }).Confirm(consent, false, false), "null confirmation item");
        Reject(() => (plan with { Items = [plan.Items[0] with { Encoding = plan.Items[0].Encoding! with { RequiredConsent = 0 } }] }).Confirm(0, false, false), "forged consent requirement");
        Reject(() => (plan with { Items = [plan.Items[0] with { Encoding = plan.Items[0].Encoding! with { SampleRate = 44100 } }] }).Confirm(consent, false, false), "forged sample rate");
        var blocked = source with { ItemId = Guid.NewGuid(), ConversionBlockReason = "Metadata needs a handler." };
        var mixed = AudioConversionBatch.Create(Guid.NewGuid(), [blocked, source], AudioFormat.Opus, new("convert", new()));
        check(mixed.Confirm(consent, false, false).Plan.Items.Count(item => item.CanExecute) == 1, "Audio batch: unsupported selection stays separate from executable items");
        Reject(() => (mixed with { Items = [mixed.Items[0] with { BlockReason = null }, mixed.Items[1]] }).Confirm(consent, false, false), "forged eligibility");
        var unchanged = AudioConversionBatch.Create(Guid.NewGuid(), [source], AudioFormat.Mp3, new("convert", new()));
        check(!unchanged.HasExecutableItems && unchanged.Items[0].Encoding!.AlreadyTarget && unchanged.RequiredConsent == 0,
            "Audio batch: same-format audio requires no conversion or quality consent");
        Reject(() => unchanged.Confirm(0, false, false), "same-format-only admission");
        Reject(() => AudioConversionBatch.Create(Guid.NewGuid(), [blocked], AudioFormat.Wave, new("convert", new())).Confirm(0, false, false), "blocked-only admission");
        Reject(() => (plan with { ReplaceOriginal = true }).Confirm(consent, true, true), "overwrite without saved consent");
        var replacement = AudioConversionBatch.Create(Guid.NewGuid(), [source], AudioFormat.Wave, new("convert", new(true)), true);
        Reject(() => replacement.Confirm(0, true, false), "overwrite on unverified platform");
        check(replacement.Confirm(0, true, true).Plan.ReplaceOriginal, "Audio batch: explicit settings and verified platform gate overwrite");
        var work = new AudioConversionWork(source, Path.Combine(scratch, $".context-suite-{source.ItemId:N}.tmp"), AudioFormat.Opus, consent);
        var probe = new WorkerCommand(1, Guid.NewGuid(), "audio-file-probe", AudioFile: new(source.ItemId, source.Path), AudioTarget: AudioFormat.Opus);
        probe.Validate(); new WorkerCommand(1, Guid.NewGuid(), "audio-convert", AudioWork: work).Validate();
        check(true, "Audio IPC: bounded facts, target and consent travel without recording bytes");
        foreach (var command in new[] { probe with { AudioBytes = [1] }, probe with { AudioTarget = null }, probe with { AudioWork = work },
            probe with { Command = "shutdown" }, new WorkerCommand(1, Guid.NewGuid(), "audio-convert"),
            new WorkerCommand(1, Guid.NewGuid(), "audio-convert", AudioWork: work with { AcceptedConsent = 0 }),
            new WorkerCommand(1, Guid.NewGuid(), "audio-convert", AudioWork: work with { Policy = "arbitrary" }),
            new WorkerCommand(1, Guid.NewGuid(), "audio-convert", AudioWork: work with { TemporaryPath = source.Path }) })
            Reject(command.Validate, "contradictory or invalid worker payload");
        await using (var worker = new WorkerClient(Path.Combine(scratch, "missing-audio-worker.exe")))
        {
            var executor = new AudioConversionExecutor(worker, new OutputPublisher(Path.Combine(scratch, "unused-audio-records"), null!), null!);
            try { await executor.ExecuteAdmittedAsync(confirmed, new(new(true, "Other batch"), Guid.NewGuid()), null, default); check(false, "Audio admission mismatch"); }
            catch (InvalidDataException) { check(worker.ProcessId is null, "Audio batch: mismatched admission starts no worker or publication"); }
        }
        var clock = new Clock(); var trialPath = Path.Combine(scratch, "audio-trial-" + Guid.NewGuid().ToString("N"), "trial.json");
        IOperationAccess trial = new LocalTrialStore(trialPath, clock);
        check((await trial.ReadAccessAsync()).CanStart && !File.Exists(trialPath), "Audio access: status does not start trial");
        var admitted = await trial.AdmitConversionAsync(confirmed, default);
        check(admitted.IsAllowed && admitted.BatchId == plan.BatchId && File.Exists(trialPath), "Audio access: one confirmed batch uses existing trial bookkeeping");
        clock.Now = clock.Now.AddDays(4);
        check(admitted.IsAllowed && !(await trial.AdmitConversionAsync(confirmed, default)).IsAllowed, "Audio access: expiry blocks new work without revoking admission");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var untouched = Path.Combine(scratch, "audio-cancelled-trial-" + Guid.NewGuid().ToString("N"), "trial.json");
        IOperationAccess cancelledTrial = new LocalTrialStore(untouched);
        try { await cancelledTrial.AdmitConversionAsync(confirmed, cancelled.Token); check(false, "Audio pre-cancellation"); }
        catch (OperationCanceledException) { check(!File.Exists(untouched), "Audio access: pre-cancellation leaves trial unstarted"); }
        void Reject(Action action, string name)
        {
            try { action(); check(false, "Audio batch rejects " + name); }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or NotSupportedException)
            { check(true, "Audio batch rejects " + name); }
        }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => 0;
    }
}
