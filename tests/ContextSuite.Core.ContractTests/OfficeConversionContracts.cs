using System.Collections.Immutable;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

internal static class OfficeConversionContracts
{
    internal static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "office-admission-" + Guid.NewGuid().ToString("N"));
        var source = new OfficeConversionSource(Guid.NewGuid(), Path.Combine(root, "document.docx"), "docx", "none", 100, new string('A', 64));
        var sheet = source with { ItemId = Guid.NewGuid(), Path = Path.Combine(root, "workbook.xlsx"), Format = "xlsx", Calculation = "cached" };
        var slides = source with { ItemId = Guid.NewGuid(), Path = Path.Combine(root, "slides.pptx"), Format = "pptx" };
        var settings = new BatchSettings("convert", new(ReplaceOriginals: true));
        var selection = new List<OfficeConversionSource> { source, sheet, slides };
        var plan = OfficeConversionPlan.Create(Guid.NewGuid(), selection, settings);
        selection.Clear();
        check(plan.Sources.SequenceEqual(new[] { source, sheet, slides }) && plan.Output.Mode == OutputMode.SiblingCopy,
            "Office plan: independent PDFs retain selected order and keep originals despite overwrite preference");
        var confirmed = plan.Confirm();
        check(confirmed.Plan.Settings == settings && confirmed.Plan.BatchId == plan.BatchId && !Directory.Exists(root),
            "Office plan: confirmation retains settings and identity without file or profile writes");
        foreach (var invalid in new[] { source with { ItemId = Guid.Empty }, source with { Format = "docm" }, source with { Format = "odt" },
            source with { Path = "relative.docx" }, source with { Calculation = "cached" }, sheet with { Calculation = "none" },
            sheet with { Calculation = "" }, source with { FileBytes = 0 }, source with { FileBytes = OfficeHostProtocol.MaximumSourceBytes + 1 },
            source with { Sha256 = new string('G', 64) } })
            Reject(() => OfficeConversionPlan.Create(plan.BatchId, [invalid], settings), "invalid source or unspecified calculation");
        OfficeConversionPlan.Create(plan.BatchId, [sheet with { Calculation = "recalculate" }], settings).Confirm();
        check(true, "Office plan: explicit recalculation remains available without selecting a customer default");
        Reject(() => OfficeConversionPlan.Create(Guid.Empty, [source], settings), "empty batch identity");
        Reject(() => OfficeConversionPlan.Create(plan.BatchId, [], settings), "empty selection");
        Reject(() => (plan with { Sources = default }).Confirm(), "uninitialized selection");
        Reject(() => OfficeConversionPlan.Create(plan.BatchId, [source, source], settings), "duplicate identity");
        Reject(() => OfficeConversionPlan.Create(plan.BatchId, [source, source with { ItemId = Guid.NewGuid(), Path = source.Path.ToUpperInvariant() }], settings), "case-insensitive duplicate path");
        Reject(() => OfficeConversionPlan.Create(plan.BatchId, [source], settings with { Operation = "optimize" }), "wrong operation");
        Reject(() => OfficeConversionPlan.Create(plan.BatchId, [source], new("convert", new(OutputDirectory: "relative"))), "invalid output folder");
        Reject(() => OfficeConversionPlan.Create(plan.BatchId, Enumerable.Repeat(source, OfficeConversionPlan.MaximumFiles + 1), settings), "selection count bound");
        Reject(() => OfficeConversionPlan.Create(plan.BatchId, Enumerable.Range(0, 40).Select(index => source with
        { ItemId = Guid.NewGuid(), Path = Path.Combine(root, new string('x', 30000) + index + ".docx") }), settings), "aggregate path bound");
        var clock = new Clock();
        var trialPath = Path.Combine(root, "trial.json");
        IOperationAccess trial = new LocalTrialStore(trialPath, clock);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await trial.AdmitConversionAsync(confirmed, cancelled.Token); check(false, "Office cancelled admission"); }
        catch (OperationCanceledException) { check(!File.Exists(trialPath), "Office access: cancelled admission cannot start trial"); }
        var admitted = await trial.AdmitConversionAsync(confirmed, default);
        check(admitted.IsAllowed && admitted.BatchId == plan.BatchId && File.Exists(trialPath), "Office access: one confirmed batch starts the ordinary trial");
        clock.Advance(TimeSpan.FromDays(7));
        var denied = await trial.AdmitConversionAsync(confirmed, default);
        check(!denied.IsAllowed && denied.BatchId == plan.BatchId && denied.Status.Message.Contains("expired", StringComparison.OrdinalIgnoreCase),
            "Office access: expiry blocks new admission with readable status");
        check(admitted.IsAllowed, "Office access: admitted batch survives later trial expiry");
        await using var absent = new WorkerClient(Path.Combine(root, "absent", "ContextSuite.Worker.exe"));
        var contextRoot = Path.Combine(root, "must-not-create-contexts");
        var publications = Path.Combine(root, "must-not-create-publications");
        await using var executor = new OfficeConversionExecutor(absent, new OutputPublisher(publications, null!), null!, contextRoot);
        var unavailable = await executor.ExecuteAsync(confirmed, null, default);
        check(unavailable.Results.All(result => result.State == OperationState.Unsupported) && absent.ProcessId is null &&
            !Directory.Exists(contextRoot) && !Directory.Exists(publications), "Office execution: missing engines refuse before access, preparation or publication");
        var blocked = await executor.ExecuteAdmittedAsync(confirmed, new(new(false, "Activate a license."), plan.BatchId), null, default);
        check(blocked.Results.Count == 3 && blocked.Results.All(result => result.State == OperationState.Failed && result.Message == "Activate a license.") &&
            !Directory.Exists(contextRoot), "Office execution: denied batch reports every document without native preparation");
        try { await executor.ExecuteAdmittedAsync(confirmed, new(new(true, "foreign"), Guid.NewGuid()), null, default); check(false, "Office foreign admission"); }
        catch (InvalidDataException) { check(absent.ProcessId is null, "Office execution: foreign admission cannot start a worker"); }
        var notStarted = await executor.ExecuteAdmittedAsync(confirmed, admitted, null, cancelled.Token);
        check(notStarted.Results.All(result => result.State == OperationState.Cancelled) && !Directory.Exists(contextRoot),
            "Office execution: cancellation before the first item creates no context");

        void Reject(Action action, string label)
        {
            try { action(); check(false, "Office plan accepted " + label); }
            catch (InvalidDataException) { check(true, "Office plan refuses " + label); }
        }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _utc = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
        private long _ticks;
        public override DateTimeOffset GetUtcNow() => _utc;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan elapsed) { _utc += elapsed; _ticks += elapsed.Ticks; }
    }
}
