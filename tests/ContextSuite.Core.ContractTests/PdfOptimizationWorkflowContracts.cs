using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Operations;

internal static class PdfOptimizationWorkflowContracts
{
    public static async Task RunAsync(string scratch, string executable, string fixtures, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(fixtures, "authored original ü.pdf"));
        var path = Path.Combine(scratch, "Authored ü.pdf");
        var secondPath = Path.Combine(scratch, "Second.pdf");
        await File.WriteAllBytesAsync(path, bytes); await File.WriteAllBytesAsync(secondPath, bytes);
        var digest = SHA256.HashData(bytes);
        var trialPath = Path.Combine(scratch, "access", "trial.json");
        var clock = new Clock();
        var access = new CountingAccess(new LocalTrialStore(trialPath, clock));
        var records = Path.Combine(scratch, "publications");
        var publisher = new OutputPublisher(records, new ForbiddenRecycle(), replacementVerified: true);
        await using var worker = new WorkerClient(executable, Path.Combine(scratch, "workers"));
        var executor = new PdfOptimizationExecutor(worker, publisher, access);
        check(worker.HasPdfOptimizer, "PDF optimization: isolated candidate engine is present");
        var first = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), path), default);
        var workerId = worker.ProcessId;
        check(first.Facts.PageCount == 2 && !File.Exists(trialPath), "PDF optimization: planning facts leave trial unstarted");
        var second = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), secondPath), default);
        var collision = Path.Combine(scratch, OutputNames.Create(path, "optimize", "pdf"));
        await File.WriteAllTextAsync(collision, "existing output canary");
        var confirmed = PdfOptimizationPlan.Create(Guid.NewGuid(), [first, second], new("optimize", new(ReplaceOriginals: true))).Confirm();
        var result = await executor.ExecuteAsync(confirmed, (item, row) =>
        {
            if (item.Source.ItemId == first.ItemId && row.State == OperationState.Succeeded) clock.Now = clock.Now.AddDays(8);
        }, default);
        check(result.Admission.IsAllowed && access.Admissions == 1 && result.Results.All(row => row.Publication?.Outcome == PublicationOutcome.CopyCreated),
            "PDF optimization: one trial admission completes both copies across expiry despite overwrite preference");
        check(worker.ProcessId == workerId, "PDF optimization: one sequential worker handles the batch");
        var published = result.Results[0].Publication!.OutputPath!;
        check(published != collision && await File.ReadAllTextAsync(collision) == "existing output canary" &&
            Path.GetFileName(published) == OutputNames.Create(path, "optimize", "pdf", 2), "PDF optimization: named copy resolves existing-output collision");
        var outputBytes = await File.ReadAllBytesAsync(published);
        check(outputBytes.Length < bytes.Length && SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(digest) &&
            SHA256.HashData(await File.ReadAllBytesAsync(secondPath)).SequenceEqual(digest), "PDF optimization: validated smaller copies preserve both originals");
        var denied = await executor.ExecuteAsync(confirmed, null, default);
        check(!denied.Admission.IsAllowed && denied.Results.All(row => row.State == OperationState.Failed) && Directory.GetFiles(records).Length == 0,
            "PDF optimization: expired access creates no reservation or output");

        var freshAccess = new CountingAccess(new LocalTrialStore(Path.Combine(scratch, "fresh-access", "trial.json")));
        var freshExecutor = new PdfOptimizationExecutor(worker, publisher, freshAccess);
        var compressed = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), published), default);
        var beforeCount = Directory.GetFiles(scratch, "*.pdf").Length;
        var unchanged = await freshExecutor.ExecuteAsync(Plan(compressed), null, default);
        check(unchanged.Results.Single().State == OperationState.Unchanged && Directory.GetFiles(scratch, "*.pdf").Length == beforeCount,
            "PDF optimization: no smaller result publishes no duplicate");

        var changedPath = Path.Combine(scratch, "Changed.pdf"); await File.WriteAllBytesAsync(changedPath, bytes);
        var changed = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), changedPath), default);
        await File.AppendAllTextAsync(changedPath, "\n");
        var failed = await freshExecutor.ExecuteAsync(Plan(changed), null, default);
        check(failed.Results.Single().State == OperationState.Failed && new FileInfo(changedPath).Length == bytes.Length + 1,
            "PDF optimization: changed source is retained and reservation abandoned");

        var signature = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), Path.Combine(fixtures, "unattached-signature-canary.pdf")), default);
        var encrypted = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), Path.Combine(fixtures, "owner-protected.pdf")), default);
        var good = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), path), default);
        var mixed = await freshExecutor.ExecuteAsync(Plan(signature, encrypted, good), null, default);
        check(mixed.Results[0].State == OperationState.Unsupported && mixed.Results[0].Publication?.IsCommitted != true,
            "PDF optimization: complete-object admission refuses signature missed by summary without publication");
        check(mixed.Results[1].State == OperationState.Unsupported && mixed.Results[1].Publication is null && mixed.Results[2].State == OperationState.Succeeded && worker.ProcessId == workerId,
            "PDF optimization: known encryption is skipped and later valid file completes in the same worker");

        var a = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), path), default);
        var b = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), secondPath), default);
        using var cancel = new CancellationTokenSource();
        var cancelled = await freshExecutor.ExecuteAsync(Plan(a, b), (_, row) =>
        { if (row.State == OperationState.Succeeded) cancel.Cancel(); }, cancel.Token);
        check(cancelled.Results[0].State == OperationState.Succeeded && cancelled.Results[1].State == OperationState.Cancelled,
            "PDF optimization: cancellation between files retains committed copy");
        var during = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), path), default);
        using var cancelReserved = new CancellationTokenSource();
        var stopped = await freshExecutor.ExecuteAsync(Plan(during), (_, row) =>
        { if (row.Message == "Optimizing and verifying PDF") cancelReserved.Cancel(); }, cancelReserved.Token);
        check(stopped.Results.Single().State == OperationState.Cancelled && Directory.GetFiles(records).Length == 0,
            "PDF optimization: cancellation after reservation abandons output safely");

        var reservedSource = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), path), default);
        var reserved = Path.Combine(scratch, $".context-suite-{reservedSource.ItemId:N}.tmp");
        await File.WriteAllTextAsync(reserved, "nonempty reservation canary");
        try { await worker.OptimizePdfAsync(new(reservedSource, reserved), default); check(false, "PDF optimization: nonempty reservation"); }
        catch (MediaWorkerException) { check(await File.ReadAllTextAsync(reserved) == "nonempty reservation canary", "PDF optimization: nonempty reservation refuses writes"); }
        File.Delete(reserved);
        var linkTarget = Path.Combine(scratch, "empty-link-target.tmp"); await File.WriteAllBytesAsync(linkTarget, []);
        if (!CreateHardLinkW(reserved, linkTarget, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            try { await worker.OptimizePdfAsync(new(reservedSource, reserved), default); check(false, "PDF optimization: linked reservation"); }
            catch (MediaWorkerException) { check(new FileInfo(linkTarget).Length == 0, "PDF optimization: hard-linked reservation refuses writes"); }
        }
        finally { File.Delete(reserved); File.Delete(linkTarget); }
        await File.WriteAllBytesAsync(reserved, []);
        try
        {
            try { await worker.OptimizePdfAsync(new(reservedSource with { Sha256 = new('0', 64) }, reserved), default); check(false, "PDF optimization: stale source digest"); }
            catch (MediaWorkerException) { check(new FileInfo(reserved).Length == 0, "PDF optimization: worker independently rejects changed source before writing"); }
        }
        finally { File.Delete(reserved); }
        check(Directory.GetFiles(records).Length == 0 && !Directory.GetFiles(scratch, ".context-suite-*.tmp").Any() &&
            !Directory.GetFiles(Path.Combine(scratch, "workers"), "*.pdf", SearchOption.AllDirectories).Any(),
            "PDF optimization: success, unchanged, cancellation and refusal clean owned temporary files and records");
        await File.WriteAllTextAsync(Path.Combine(scratch, "pdf-optimization-workflow.json"), JsonSerializer.Serialize(new { firstBatch = result, unchanged, failed, mixed, cancelled, stopped },
            new JsonSerializerOptions { WriteIndented = true }));

        static ConfirmedPdfOptimization Plan(params PdfFileSource[] sources) =>
            PdfOptimizationPlan.Create(Guid.NewGuid(), sources, new("optimize", new())).Confirm();
    }

    private sealed class CountingAccess(IOperationAccess inner) : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => inner.ReadAccessAsync(token);
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected image conversion admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected PNG admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPdfOptimization confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitOptimizationAsync(confirmed, token); }
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
            throw new InvalidOperationException("PDF optimization must never recycle.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string path, string existing, IntPtr security);
}
