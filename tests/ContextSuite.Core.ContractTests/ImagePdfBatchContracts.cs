using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Transport;

namespace ContextSuite.Core.ContractTests;

internal static class ImagePdfBatchContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var source = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(scratch, "first.png"), new('A', 64), 100,
            ImageFormat.Png, 4, 3, 8, 1, false, false, "sRGB", []);
        var other = source with { ItemId = Guid.NewGuid(), Path = Path.Combine(scratch, "second.png") };
        var plan = ImagePdfPlan.Create(Guid.NewGuid(), [source, other], new("convert", new(ReplaceOriginals: true))).Confirm(true);
        var id = Guid.NewGuid(); var work = new ImagePdfWork(plan.Plan, id, Path.Combine(scratch, $".context-suite-{id:N}.tmp"), true);
        var command = new WorkerCommand(1, Guid.NewGuid(), "images-to-pdf", ImagePdf: work); command.Validate();
        using var frame = new MemoryStream(); await JsonFrames.WriteAsync(frame, command, default); frame.Position = 0;
        var received = await JsonFrames.ReadAsync<WorkerCommand>(frame, default); received.Validate();
        check(received.ImagePdf!.Plan.Pages.Select(page => page.Source.ItemId).SequenceEqual(new[] { source.ItemId, other.ItemId }), "combined PDF: typed IPC preserves exact page order");
        Reject(() => (command with { ImagePdf = null }).Validate(), "missing work");
        Reject(() => (command with { Command = "engine-info" }).Validate(), "payload on another operation");
        Reject(() => (command with { Probe = new(source.ItemId, source.Path) }).Validate(), "contradictory image payload");
        Reject(() => (command with { PdfBytes = [1] }).Validate(), "contradictory PDF payload");
        Reject(() => (command with { AudioBytes = [1] }).Validate(), "contradictory audio payload");
        Reject(() => (work with { PageOrderReviewed = false }).Validate(), "unreviewed multiple-page order");
        Reject(() => (work with { Policy = "unreviewed" }).Validate(), "unknown policy");
        Reject(() => (work with { OutputId = source.ItemId }).Validate(), "source/output ID collision");
        Reject(() => (work with { TemporaryPath = source.Path }).Validate(), "source-path output");
        Reject(() => (work with { TemporaryPath = Path.Combine(scratch, "arbitrary.pdf") }).Validate(), "unreserved output name");
        check(OutputNames.Create(source.Path, "convert", "pdf", combinedPdf: true) == "first - Combined.pdf" &&
            OutputNames.Create(source.Path, "convert", "pdf", 2, combinedPdf: true) == "first - Combined (2).pdf", "combined PDF: combined suffix and collision suffix stay distinct");
        Reject(() => OutputNames.Create(source.Path, "optimize", "pdf", combinedPdf: true), "combined Optimize name");
        Reject(() => OutputNames.Create(source.Path, "convert", "pdf", replaceSource: true, combinedPdf: true), "combined replacement name");
        Reject(() => OutputNames.Create(source.Path, "convert", "png", combinedPdf: true), "combined non-PDF name");
        Reject(() => ImagePdfPlan.Create(Guid.NewGuid(), [source], new("convert", new(OutputDirectory: "relative"))), "relative output folder");
        Reject(() => ImagePdfPlan.Create(Guid.NewGuid(), Enumerable.Range(0, 40).Select(i => source with {
            ItemId = Guid.NewGuid(), Path = Path.Combine(scratch, new string('x', 30000) + i + ".png") }), plan.Plan.Settings), "bounded source descriptions");
        var records = Path.Combine(scratch, "combined-pdf-unavailable-records");
        var publisher = new OutputPublisher(records, null!);
        await using var worker = new WorkerClient(Path.Combine(scratch, "missing-pdf-worker.exe"));
        var executor = new ImagePdfExecutor(worker, publisher, null!);
        var unavailable = await executor.ExecuteAsync(plan, null, default);
        check(unavailable.Result.State == OperationState.Unsupported && worker.ProcessId is null && !Directory.Exists(records),
            "combined PDF: missing engine declines before admission, worker or reservation");
        try { await executor.ExecuteAdmittedAsync(plan, new(new(true, "wrong batch"), Guid.NewGuid()), null, default); check(false, "Wrong combined admission accepted."); }
        catch (InvalidDataException) { check(worker.ProcessId is null, "combined PDF: foreign admission cannot start worker"); }
        var denied = await executor.ExecuteAdmittedAsync(plan, new(new(false, "Activate a license."), plan.Plan.BatchId), null, default);
        check(denied.Result.State == OperationState.Failed && denied.Result.Message == "Activate a license." && !Directory.Exists(records),
            "combined PDF: denied admission starts no publication");
        IOperationAccess trial = new LocalTrialStore(Path.Combine(scratch, "combined-pdf-trial", "trial.json"));
        var admission = await trial.AdmitConversionAsync(plan, default);
        check(admission.IsAllowed && admission.BatchId == plan.Plan.BatchId, "combined PDF: confirmed group starts one ordinary trial batch");

        void Reject(Action action, string name)
        {
            try { action(); check(false, "Combined PDF accepted " + name); }
            catch (InvalidDataException) { check(true, "combined PDF rejects " + name); }
        }
    }
}
