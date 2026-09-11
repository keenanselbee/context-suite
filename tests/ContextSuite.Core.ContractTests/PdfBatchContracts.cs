using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Transport;

internal static class PdfBatchContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var source = new PdfFileSource(Guid.NewGuid(), Path.Combine(scratch, "sample.pdf"), new('A', 64), 1024,
            new(2, false, false, 0, 0, 0, 0, false));
        var settings = new BatchSettings("optimize", new(ReplaceOriginals: true));
        var plan = PdfOptimizationPlan.Create(Guid.NewGuid(), [source], settings);
        check(plan.Confirm().Plan.Settings == settings, "PDF batch: overwrite preference remains immutable while PDF uses copies");
        var locked = source with { ItemId = Guid.NewGuid(), Facts = new(null, true, null, null, null, null, null, null) };
        var signed = source with { ItemId = Guid.NewGuid(), Facts = new(2, false, true, 1, 1, 0, 0, false) };
        var mixed = PdfOptimizationPlan.Create(Guid.NewGuid(), [locked, signed, source], settings);
        check(!mixed.Items[0].CanExecute && !mixed.Items[1].CanExecute && mixed.Items[2].CanExecute, "PDF batch: known protection is excluded per file");
        Refuses(() => PdfOptimizationPlan.Create(Guid.NewGuid(), [locked], settings).Confirm(), "all protected selection cannot be confirmed");
        Refuses(() => (mixed with { Items = mixed.Items.SetItem(0, mixed.Items[0] with { BlockReason = null }) }).Confirm(), "forged eligibility is refused");
        Refuses(() => PdfOptimizationPlan.Create(Guid.NewGuid(), [source, source], settings), "duplicate item identity is refused");
        Refuses(() => PdfOptimizationPlan.Create(Guid.NewGuid(), [source], new("convert", new())), "conversion settings are refused");
        Refuses(() => (source with { FileBytes = PdfFileSource.MaximumFileBytes + 1L }).Validate(), "oversized source is refused");
        Refuses(() => (source with { Sha256 = new('Z', 64) }).Validate(), "invalid source digest is refused");
        var work = new PdfOptimizationWork(source, Path.Combine(scratch, $".context-suite-{source.ItemId:N}.tmp"));
        new WorkerCommand(1, Guid.NewGuid(), "pdf-optimize", PdfWork: work).Validate();
        new WorkerCommand(1, Guid.NewGuid(), "pdf-file-probe", PdfFile: new(source.ItemId, source.Path)).Validate();
        check(true, "PDF batch: typed file probe and optimization requests validate");
        Refuses(() => (work with { TemporaryPath = source.Path }).Validate(), "source cannot be used as reservation");
        Refuses(() => (work with { Policy = "arbitrary" }).Validate(), "unreviewed policy is refused");
        Refuses(() => new WorkerCommand(1, Guid.NewGuid(), "pdf-optimize", PdfWork: work, PdfBytes: [1]).Validate(), "mixed PDF payload is refused");
        Refuses(() => new WorkerCommand(1, Guid.NewGuid(), "capabilities", PdfWork: work).Validate(), "unrelated operation cannot carry PDF work");
        Refuses(() => new WorkerCommand(1, Guid.NewGuid(), "pdf-file-probe", PdfFile: new(source.ItemId, source.Path), PdfWork: work).Validate(), "probe cannot carry optimization work");
        IOperationAccess access = new LocalTrialStore(Path.Combine(scratch, "pdf-batch-trial", "trial.json"));
        var admitted = await access.AdmitOptimizationAsync(plan.Confirm(), default);
        check(admitted.IsAllowed && admitted.BatchId == plan.BatchId, "PDF batch: confirmed optimization starts the normal local trial");
        await using var worker = new WorkerClient(Path.Combine(scratch, "missing-pdf-worker.exe"));
        var recordPath = Path.Combine(scratch, "pdf-denied-records");
        var executor = new PdfOptimizationExecutor(worker, new OutputPublisher(recordPath, null!), null!);
        try { await executor.ExecuteAdmittedAsync(plan.Confirm(), new(new(true, "Other batch"), Guid.NewGuid()), null, default); check(false, "PDF batch: foreign admission"); }
        catch (InvalidDataException) { check(worker.ProcessId is null && !Directory.Exists(recordPath), "PDF batch: foreign admission starts no worker or publication"); }
        var denied = await executor.ExecuteAdmittedAsync(plan.Confirm(), new(new(false, "Activate a license."), plan.BatchId), null, default);
        check(denied.Results.Single().Message == "Activate a license." && worker.ProcessId is null && !Directory.Exists(recordPath),
            "PDF batch: denied admission does not touch files or start worker");

        void Refuses(Action action, string description)
        {
            try { action(); check(false, "PDF batch: " + description); }
            catch (InvalidDataException) { check(true, "PDF batch: " + description); }
        }
    }
}
