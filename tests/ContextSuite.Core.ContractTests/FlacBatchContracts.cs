using System.Collections.Immutable;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Transport;

internal static class FlacBatchContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var facts = new AudioProbeFacts("flac", 2, [new(0, "audio", "flac", 48000, 2, 16, null, 2, "stereo", ImmutableDictionary<string, string>.Empty)], ImmutableDictionary<string, string>.Empty);
        var source = new AudioFileSource(Guid.NewGuid(), Path.Combine(scratch, "planned.flac"), new string('A', 64), 5000, facts);
        var blocked = source with { ItemId = Guid.NewGuid(), OptimizationBlockReason = "Unsupported application metadata" };
        var plan = FlacOptimizationPlan.Create(Guid.NewGuid(), [blocked, source], new("optimize", new()));
        var confirmed = plan.Confirm(false, false);
        check(confirmed.Plan.Items.Length == 2 && !confirmed.Plan.Items[0].CanExecute && confirmed.Plan.Items[1].CanExecute && !plan.ReplaceOriginal,
            "FLAC batch: mixed eligibility retains one immutable copy-default plan");
        Reject(() => FlacOptimizationPlan.Create(Guid.Empty, [source], new("optimize", new())), "empty batch identity");
        Reject(() => FlacOptimizationPlan.Create(Guid.NewGuid(), [], new("optimize", new())), "empty selection");
        Reject(() => FlacOptimizationPlan.Create(Guid.NewGuid(), [source, source], new("optimize", new())), "duplicate item identity");
        Reject(() => FlacOptimizationPlan.Create(Guid.NewGuid(), [source], new("convert", new())), "wrong settings operation");
        Reject(() => FlacOptimizationPlan.Create(Guid.NewGuid(), [blocked], new("optimize", new())).Confirm(false, false), "unsupported-only confirmation");
        Reject(() => (plan with { Items = [new(blocked, null), new(source, null)] }).Confirm(false, false), "forged eligibility");
        Reject(() => (plan with { Items = default }).Confirm(false, false), "missing confirmation items");
        Reject(() => (plan with { Items = [null!] }).Confirm(false, false), "null confirmation item");
        Reject(() => FlacOptimizationPlan.Create(Guid.NewGuid(), [source], new("optimize", new()), true).Confirm(true, true), "overwrite without settings consent");
        var replacement = FlacOptimizationPlan.Create(Guid.NewGuid(), [source], new("optimize", new(true)), true);
        Reject(() => replacement.Confirm(true, false), "overwrite on unverified platform");
        check(replacement.Confirm(true, true).Plan.ReplaceOriginal, "FLAC batch: verified explicit settings consent permits replacement plan");
        var probe = new WorkerCommand(1, Guid.NewGuid(), "flac-probe", AudioFile: new(source.ItemId, source.Path)); probe.Validate();
        var work = new FlacOptimizationWork(source, Path.Combine(scratch, $".context-suite-{source.ItemId:N}.tmp"));
        new WorkerCommand(1, Guid.NewGuid(), "flac-optimize", FlacWork: work).Validate();
        check(true, "FLAC IPC: file references replace whole-recording byte payloads");
        foreach (var request in new[] { probe with { AudioBytes = [1] }, probe with { Command = "shutdown" }, probe with { FlacWork = work },
            new WorkerCommand(1, Guid.NewGuid(), "flac-optimize"), new WorkerCommand(1, Guid.NewGuid(), "flac-optimize", FlacWork: work with { Policy = "arbitrary" }) })
            Reject(request.Validate, "contradictory worker payload");
        var clock = new Clock();
        var trialPath = Path.Combine(scratch, "flac-trial-" + Guid.NewGuid().ToString("N"), "trial.json");
        IOperationAccess access = new LocalTrialStore(trialPath, clock);
        check((await access.ReadAccessAsync()).CanStart && !File.Exists(trialPath), "FLAC access: read-only status does not start trial");
        var admitted = await access.AdmitOptimizationAsync(confirmed, default);
        check(admitted.IsAllowed && admitted.BatchId == plan.BatchId && File.Exists(trialPath), "FLAC access: confirmed batch starts existing trial bookkeeping");
        clock.Now = clock.Now.AddDays(4);
        check(admitted.IsAllowed && !(await access.AdmitOptimizationAsync(confirmed, default)).IsAllowed, "FLAC access: expiry blocks new work without revoking admitted batch");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var cancelledPath = Path.Combine(scratch, "flac-cancelled-trial-" + Guid.NewGuid().ToString("N"), "trial.json");
        IOperationAccess cancelledTrial = new LocalTrialStore(cancelledPath, clock);
        try { await cancelledTrial.AdmitOptimizationAsync(confirmed, cancelled.Token); check(false, "FLAC access: pre-cancellation"); }
        catch (OperationCanceledException) { check(!File.Exists(cancelledPath), "FLAC access: pre-cancellation leaves trial unstarted"); }
        void Reject(Action action, string name)
        {
            try { action(); check(false, "FLAC batch: " + name); }
            catch (InvalidDataException) { check(true, "FLAC batch: " + name); }
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => 0;
    }
}
