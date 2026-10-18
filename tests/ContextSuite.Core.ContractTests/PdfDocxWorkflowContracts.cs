using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Application;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

internal static class PdfDocxWorkflowContracts
{
    // Opt-in local fidelity evidence: copy the caller's sample into a new
    // evidence directory, then exercise the actual direct command and publisher.
    // No personal fixture, installed application state or activation is needed.
    public static async Task ConvertSampleAsync(string root, string executable, string input)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root)) throw new IOException("Use a new absolute sample evidence directory.");
        if (new FileInfo(input).Length is <= 0 or > PdfDocxSource.MaximumSourceBytes) throw new InvalidDataException("Sample exceeds the PDF input limit.");
        var originalHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(input)));
        Directory.CreateDirectory(root);
        var copy = Path.Combine(root, "Sample.pdf");
        File.Copy(input, copy);
        await using var vm = new MainViewModel(new WorkerClient(executable, Path.Combine(root, "worker")),
            new() { Convert = new(OutputDirectory: root) }, new OutputPublisher(Path.Combine(root, "records"), new NoRecycle()),
            new LocalTrialStore(Path.Combine(root, "trial.json")));
        vm.ConversionRequested += (_, _) => throw new InvalidOperationException("PDF-to-Word must use the direct command.");
        vm.Admit(new(Guid.NewGuid(), "convert", "docx", [copy]));
        await vm.WaitForIdleAsync();
        var result = vm.Rows.Single().Result;
        if (result.State != OperationState.Succeeded || result.Publication?.Outcome != PublicationOutcome.CopyCreated ||
            originalHash != Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(input))) ||
            originalHash != Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(copy))))
            throw new InvalidDataException("Sample did not produce a validated copy with unchanged originals: " + result.Message);
        await File.WriteAllTextAsync(Path.Combine(root, "result.json"), System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: actual direct command, worker and publisher; unchanged source. Output: " + result.Publication.OutputPath);
    }

    public static async Task RunAsync(string root, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use a new PDF-to-Word evidence directory.");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Report.pdf");
        File.Copy(Path.Combine(fixtures, "ordinary.pdf"), path);
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        await using var worker = new WorkerClient(executable, Path.Combine(root, "worker"));
        var trialPath = Path.Combine(root, "trial.json");
        IOperationAccess access = new LocalTrialStore(trialPath);
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle(), replacementVerified: true);
        var executor = new PdfDocxExecutor(worker, publisher, access);
        var source = await worker.ProbePdfDocxAsync(new(Guid.NewGuid(), path), default);
        check(!source.NeedsOcr && source.TextCharacters > 0 && !File.Exists(trialPath), "DOCX: inspection does not start trial");
        var collision = Path.Combine(root, "Report.docx");
        await File.WriteAllTextAsync(collision, "existing document");
        var results = new List<FileResult>();
        await executor.ExecuteAsync(Plan(source), (_, result) => { if (result.State != OperationState.Running) results.Add(result); }, default);
        var output = results.Single().Publication?.OutputPath;
        check(results[0].Publication?.Outcome == PublicationOutcome.CopyCreated && output != collision && File.Exists(output),
            "DOCX: worker output publishes a collision-safe copy despite replacement setting");
        using (var zip = ZipFile.OpenRead(output!))
            check(zip.GetEntry("word/document.xml") is not null, "DOCX: publication contains a Word document part");
        check(await File.ReadAllTextAsync(collision) == "existing document" &&
            hash == Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))), "DOCX: original and collision canary survive");
        var scanned = await worker.ProbePdfDocxAsync(new(Guid.NewGuid(), Path.Combine(fixtures, "scanned.pdf")), default);
        check(scanned.NeedsOcr, "DOCX: scanned input reports OCR requirement through real worker");
        try { _ = Plan(scanned); check(false, "Scanned input admitted."); }
        catch (InvalidDataException) { check(true, "DOCX: no-text source cannot become an executable plan"); }
        var id = Guid.NewGuid(); var reservation = Path.Combine(root, $".context-suite-{id:N}.tmp");
        await File.WriteAllTextAsync(reservation, "reserved canary");
        try { await worker.ConvertPdfDocxAsync(new(source, id, reservation), default); check(false, "Nonempty output accepted."); }
        catch (MediaWorkerException) { check(await File.ReadAllTextAsync(reservation) == "reserved canary", "DOCX: worker refuses nonempty output"); }
        File.Delete(reservation);
        var artwork = await worker.ProbePdfDocxAsync(new(Guid.NewGuid(), Path.Combine(fixtures, "artwork.pdf")), default);
        var figurePlan = new PdfDocxPlan(Guid.NewGuid(), new("convert", new(OutputDirectory: root)), [artwork]).Confirm();
        results.Clear();
        await executor.ExecuteAsync(figurePlan, (_, result) => { if (result.State != OperationState.Running) results.Add(result); }, default);
        check(results.Single().State == OperationState.Succeeded && results[0].Message.Contains("figures"), "DOCX: real worker preserves complex artwork and reports image fallback");
        var malformed = Path.Combine(root, "Malformed.pdf"); await File.WriteAllTextAsync(malformed, "%PDF-1.7\nnot a PDF");
        var workerId = worker.ProcessId;
        try { await worker.ProbePdfDocxAsync(new(Guid.NewGuid(), malformed), default); check(false, "Malformed PDF accepted."); }
        catch (MediaWorkerException) { check(worker.ProcessId == workerId, "DOCX: malformed input is refused without crashing the worker"); }
        foreach (var afterMove in new[] { false, true })
        {
            var faultRoot = Path.Combine(root, afterMove ? "after-move" : "before-move"); Directory.CreateDirectory(faultRoot);
            var faultPublisher = new OutputPublisher(Path.Combine(faultRoot, "records"), new NoRecycle(), io: new MoveFailure(afterMove));
            var faultPlan = new PdfDocxPlan(Guid.NewGuid(), new("convert", new(OutputDirectory: faultRoot)), [source]).Confirm();
            results.Clear();
            await new PdfDocxExecutor(worker, faultPublisher, access).ExecuteAsync(faultPlan,
                (_, result) => { if (result.State != OperationState.Running) results.Add(result); }, default);
            check(results.Single().Publication?.IsCommitted == afterMove && File.Exists(path),
                "DOCX: publication failure accurately retains committed output and original: " + afterMove);
        }
        using var cancelled = new CancellationTokenSource();
        results.Clear();
        await executor.ExecuteAsync(Plan(source), (_, result) =>
        { if (result.State == OperationState.Running) cancelled.Cancel(); else results.Add(result); }, cancelled.Token);
        check(results.Single().State == OperationState.Cancelled && !Directory.GetFiles(root, ".context-suite-*.tmp").Any(),
            "DOCX: cancellation leaves no candidate");
        await File.AppendAllTextAsync(path, "\n");
        results.Clear();
        await executor.ExecuteAsync(Plan(source), (_, result) => { if (result.State != OperationState.Running) results.Add(result); }, default);
        check(results.Single().State == OperationState.Failed && Directory.GetFiles(Path.Combine(root, "records")).Length == 0,
            "DOCX: changed source is rejected and reservation cleaned");
        await worker.DisposeAsync();
        var direct = Path.Combine(root, "direct"); Directory.CreateDirectory(direct);
        await using (var vm = new MainViewModel(new WorkerClient(executable, Path.Combine(direct, "worker")),
            new() { Convert = new(OutputDirectory: direct) }, new OutputPublisher(Path.Combine(direct, "records"), new NoRecycle()), access))
        {
            vm.ConversionRequested += (_, _) => throw new InvalidOperationException("PDF-to-Word must not open the image planner.");
            vm.Admit(new(Guid.NewGuid(), "convert", "docx", [path, scanned.Path, malformed]));
            await vm.WaitForIdleAsync();
            check(vm.Rows[0].Result.State == OperationState.Succeeded && vm.Rows[1].Result.State == OperationState.Unsupported &&
                vm.Rows[1].Result.Message.Contains("OCR") && vm.Rows[2].Result.State == OperationState.Unsupported,
                "DOCX: actual direct command converts text PDF and reports scanned/malformed members independently");
        }
        static ConfirmedPdfDocx Plan(params PdfDocxSource[] sources) => new PdfDocxPlan(Guid.NewGuid(),
            new("convert", new(ReplaceOriginals: true)), sources.ToImmutableArray()).Confirm();
    }

    private sealed class MoveFailure(bool afterMove) : PublicationIo
    {
        public override void Move(string temporary, string destination)
        {
            if (afterMove) base.Move(temporary, destination);
            throw new IOException("Authored publication fault.");
        }
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("PDF-to-Word must keep the source.");
    }
}
