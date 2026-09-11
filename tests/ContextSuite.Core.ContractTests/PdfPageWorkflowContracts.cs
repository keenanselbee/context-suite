using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

internal static class PdfPageWorkflowContracts
{
    public static async Task RunAsync(string root, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use a new PDF page workflow directory.");
        Directory.CreateDirectory(root);
        var original = await File.ReadAllBytesAsync(Path.Combine(fixtures, "authored original ü.pdf"));
        var sourcePath = Path.Combine(root, "Authored ü.pdf");
        var secondPath = Path.Combine(root, "Second.pdf");
        await File.WriteAllBytesAsync(sourcePath, original); await File.WriteAllBytesAsync(secondPath, original);
        var originalHash = Convert.ToHexString(SHA256.HashData(original));
        var clock = new Clock();
        var trialPath = Path.Combine(root, "trial.json");
        var access = new CountingAccess(new LocalTrialStore(trialPath, clock));
        var records = Path.Combine(root, "records");
        var publisher = new OutputPublisher(records, new ForbiddenRecycle(), replacementVerified: true);
        await using var worker = new WorkerClient(executable, Path.Combine(root, "workers"));
        var executor = new PdfPageConversionExecutor(worker, publisher, access);
        check(worker.HasPdfRenderer, "PDF pages: isolated renderer is present");
        var first = await worker.ProbePdfPagesAsync(new(Guid.NewGuid(), sourcePath), default);
        var second = await worker.ProbePdfPagesAsync(new(Guid.NewGuid(), secondPath), default);
        var workerId = worker.ProcessId;
        check(first.Document.Pages.Length == 2 && !File.Exists(trialPath), "PDF pages: inspecting all geometry leaves trial unstarted");
        var collision = Path.Combine(root, "Authored ü - Page 001.png");
        await File.WriteAllTextAsync(collision, "existing file canary");
        var plan = Plan(first, second);
        var completed = await executor.ExecuteAsync(plan, (_, _, row) =>
        { if (row.State == OperationState.Succeeded) clock.Now = clock.Now.AddDays(4); }, default);
        check(completed.Admission.IsAllowed && completed.Pages.Count == 4 && completed.Pages.All(page => page.Result.Publication?.Outcome == PublicationOutcome.CopyCreated) && access.Admissions == 1,
            "PDF pages: four page copies complete under one admission across trial expiry and overwrite preference");
        check(worker.ProcessId == workerId, "PDF pages: one sequential worker handles the batch");
        check(Path.GetFileName(completed.Pages[0].Result.Publication!.OutputPath!) == "Authored ü - Page 001 (2).png" &&
            Path.GetFileName(completed.Pages[1].Result.Publication!.OutputPath!) == "Authored ü - Page 002.png" &&
            await File.ReadAllTextAsync(collision) == "existing file canary", "PDF pages: readable page names preserve collision canary");
        foreach (var page in completed.Pages)
        {
            var output = page.Result.Publication!.OutputPath!;
            var facts = await worker.ProbeAsync(new(Guid.NewGuid(), output), default);
            var expected = (page.SourceId == first.ItemId ? first : second).Document.Pages[page.PageIndex];
            check(facts.Width == expected.Width && facts.Height == expected.Height && facts.Format == ImageFormat.Png,
                "PDF pages: actual published PNG has the planned dimensions");
        }
        var denied = await executor.ExecuteAsync(plan, null, default);
        check(!denied.Admission.IsAllowed && denied.Pages.All(page => page.Result.State == OperationState.Failed && page.Result.Publication is null),
            "PDF pages: expired access creates no reservations");
        var fresh = new CountingAccess(new LocalTrialStore(Path.Combine(root, "fresh-trial.json")));
        var freshExecutor = new PdfPageConversionExecutor(worker, publisher, fresh);
        using var cancelled = new CancellationTokenSource();
        var partial = await freshExecutor.ExecuteAsync(Plan(first), (_, _, row) =>
        { if (row.State == OperationState.Succeeded) cancelled.Cancel(); }, cancelled.Token);
        check(partial.Pages[0].Result.Publication?.IsCommitted == true && partial.Pages[1].Result.State == OperationState.Cancelled &&
            File.Exists(partial.Pages[0].Result.Publication!.OutputPath), "PDF pages: cancellation retains first copy and identifies unconverted page");
        using var beforeRender = new CancellationTokenSource();
        var stopped = await freshExecutor.ExecuteAsync(Plan(first), (_, _, row) =>
        { if (row.Message == "Rendering and validating page") beforeRender.Cancel(); }, beforeRender.Token);
        check(stopped.Pages.All(page => page.Result.State == OperationState.Cancelled) && Directory.GetFiles(records).Length == 0,
            "PDF pages: cancellation after reservation cleans pending page and journals");
        var changedPath = Path.Combine(root, "Changed.pdf"); await File.WriteAllBytesAsync(changedPath, original);
        var changed = await worker.ProbePdfPagesAsync(new(Guid.NewGuid(), changedPath), default);
        await File.AppendAllTextAsync(changedPath, "\n");
        var mixed = await freshExecutor.ExecuteAsync(Plan(changed, second), null, default);
        check(mixed.Pages.Take(2).All(page => page.Result.State == OperationState.Failed) && mixed.Pages.Skip(2).All(page => page.Result.State == OperationState.Succeeded),
            "PDF pages: stale document stops its remaining pages and later document completes");
        foreach (var afterMove in new[] { false, true })
        {
            var faultRoot = Path.Combine(root, afterMove ? "after-move" : "before-move"); Directory.CreateDirectory(faultRoot);
            var faultPublisher = new OutputPublisher(Path.Combine(faultRoot, "records"), new ForbiddenRecycle(), io: new SecondPageFailure(afterMove));
            var faultPlan = PdfPageConversionPlan.Create(Guid.NewGuid(), [first, second], new("convert", new(OutputDirectory: faultRoot))).Confirm();
            var fault = await new PdfPageConversionExecutor(worker, faultPublisher, fresh).ExecuteAsync(faultPlan, null, default);
            check(fault.Pages[0].Result.Publication?.IsCommitted == true && fault.Pages[1].Result.Publication?.IsCommitted == afterMove &&
                fault.Pages.Skip(2).All(page => page.Result.State == OperationState.Succeeded), "PDF pages: publication failure distinguishes committed copy and later document continues");
            check(File.Exists(fault.Pages[0].Result.Publication!.OutputPath) && Directory.GetFiles(Path.Combine(faultRoot, "records")).Length > 0,
                "PDF pages: completed page and failure recovery evidence survive");
        }
        var outputId = Guid.NewGuid(); var temporary = Path.Combine(root, $".context-suite-{outputId:N}.tmp");
        var work = new PdfPageWork(first, 0, outputId, temporary);
        await File.WriteAllTextAsync(temporary, "nonempty canary");
        await Refuse(work, () => File.ReadAllText(temporary) == "nonempty canary", "nonempty reservation is unchanged");
        File.Delete(temporary);
        var linkTarget = Path.Combine(root, "empty-link.tmp"); await File.WriteAllBytesAsync(linkTarget, []);
        if (!CreateHardLinkW(temporary, linkTarget, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        await Refuse(work, () => new FileInfo(linkTarget).Length == 0, "hard-linked reservation is unchanged");
        File.Delete(temporary); File.Delete(linkTarget);
        await File.WriteAllBytesAsync(temporary, []);
        await Refuse(work with { Source = first with { Sha256 = new('0', 64) } }, () => new FileInfo(temporary).Length == 0,
            "worker independently refuses a changed source before writing");
        File.Delete(temporary);
        foreach (var name in new[] { "encrypted.pdf", "owner-protected.pdf" })
        {
            try { await worker.ProbePdfPagesAsync(new(Guid.NewGuid(), Path.Combine(fixtures, name)), default); check(false, "Protected PDF accepted."); }
            catch (MediaWorkerException) { check(true, "PDF pages: protected input remains refused through worker"); }
        }
        var wrongAdmission = new OperationAdmission(new(true, "synthetic"), Guid.NewGuid());
        try { await freshExecutor.ExecuteAdmittedAsync(Plan(first), wrongAdmission, null, default); check(false, "Wrong admission accepted."); }
        catch (InvalidDataException) { check(true, "PDF pages: another batch admission is refused"); }
        check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))) == originalHash &&
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(secondPath))) == originalHash,
            "PDF pages: originals survive successful, partial and failed conversions");
        check(!Directory.GetFiles(root, ".context-suite-*.tmp").Any() && Directory.GetFiles(records).Length == 0,
            "PDF pages: ordinary completion and cancellation clean temporary outputs; injected recovery evidence remains separate");
        await File.WriteAllTextAsync(Path.Combine(root, "pdf-page-workflow.json"), JsonSerializer.Serialize(new { completed, denied, partial, stopped, mixed },
            new JsonSerializerOptions { WriteIndented = true }));

        async Task Refuse(PdfPageWork request, Func<bool> unchanged, string description)
        {
            try { await worker.RenderPdfPageAsync(request, default); check(false, "Unexpected page acceptance."); }
            catch (MediaWorkerException) { check(unchanged(), "PDF pages: " + description); }
        }
        static ConfirmedPdfPageConversion Plan(params PdfRasterSource[] sources) =>
            PdfPageConversionPlan.Create(Guid.NewGuid(), sources, new("convert", new(ReplaceOriginals: true))).Confirm();
    }

    private sealed class SecondPageFailure(bool afterMove) : PublicationIo
    {
        public override void Move(string temporary, string destination)
        {
            if (!Path.GetFileName(destination).Contains("Authored") || !destination.Contains("Page 002")) { base.Move(temporary, destination); return; }
            if (afterMove) base.Move(temporary, destination);
            throw new IOException("Generated page publication fault.");
        }
    }
    private sealed class CountingAccess(IOperationAccess inner) : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => inner.ReadAccessAsync(token);
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected image admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected Optimize admission.");
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedPdfPageConversion confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitConversionAsync(confirmed, token); }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => 0;
    }
    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("PDF page conversion must never recycle.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string path, string existing, IntPtr security);
}
