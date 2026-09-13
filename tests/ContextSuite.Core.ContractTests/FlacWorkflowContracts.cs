using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static class FlacWorkflowContracts
{
    public static async Task RunAsync(string scratch, string executable, string fixtures, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(fixtures, "source.flac"));
        var padding = new byte[4 + 262144]; padding[0] = 1; padding[1] = 4;
        var padded = bytes[..42].Concat(padding).Concat(bytes[42..]).ToArray();
        var path = Path.Combine(scratch, "Authored ü.flac");
        var secondPath = Path.Combine(scratch, "Second.flac");
        await File.WriteAllBytesAsync(path, padded); await File.WriteAllBytesAsync(secondPath, padded);
        var digest = SHA256.HashData(padded);
        var trialPath = Path.Combine(scratch, "access", "trial.json");
        var clock = new Clock();
        var access = new CountingAccess(new LocalTrialStore(trialPath, clock));
        var records = Path.Combine(scratch, "publications");
        var publisher = new OutputPublisher(records, new ForbiddenRecycle());
        await using var worker = new WorkerClient(executable, Path.Combine(scratch, "workers"));
        var executor = new FlacOptimizationExecutor(worker, publisher, access);
        check(worker.HasFlacOptimizer, "FLAC workflow: isolated encoder payload is present");
        var first = await worker.ProbeFlacAsync(new(Guid.NewGuid(), path), default);
        var workerId = worker.ProcessId;
        check(first.OptimizationBlockReason is null && !File.Exists(trialPath), "FLAC workflow: planning probe leaves trial unstarted");
        var second = await worker.ProbeFlacAsync(new(Guid.NewGuid(), secondPath), default);
        var collision = Path.Combine(scratch, OutputNames.Create(path, "optimize", "flac"));
        await File.WriteAllTextAsync(collision, "existing output canary");
        var confirmed = FlacOptimizationPlan.Create(Guid.NewGuid(), [first, second], new("optimize", new())).Confirm(false, false);
        var result = await executor.ExecuteAsync(confirmed, (item, row) =>
        {
            if (item.Source.ItemId == first.ItemId && row.State == OperationState.Succeeded) clock.Now = clock.Now.AddDays(8);
        }, default);
        check(result.Admission.IsAllowed && access.Admissions == 1 && result.Results.All(row => row.Publication?.Outcome == PublicationOutcome.CopyCreated),
            "FLAC workflow: one trial admission lets both files finish across expiry");
        check(worker.ProcessId == workerId, "FLAC workflow: one sequential worker handles the batch");
        var published = result.Results[0].Publication!.OutputPath!;
        check(published != collision && await File.ReadAllTextAsync(collision) == "existing output canary" &&
            Path.GetFileName(published) == OutputNames.Create(path, "optimize", "flac", 2), "FLAC workflow: named copy resolves collision without touching existing output");
        var outputBytes = await File.ReadAllBytesAsync(published);
        FlacMetadata.RequirePreservedMetadata(FlacMetadata.Parse(padded), FlacMetadata.Parse(outputBytes));
        check(outputBytes.Length < padded.Length && SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(digest) &&
            SHA256.HashData(await File.ReadAllBytesAsync(secondPath)).SequenceEqual(digest), "FLAC workflow: validated smaller copies preserve metadata and both originals");
        var denied = await executor.ExecuteAsync(confirmed, null, default);
        check(!denied.Admission.IsAllowed && denied.Results.All(row => row.State == OperationState.Failed) && Directory.GetFiles(records).Length == 0,
            "FLAC workflow: expired access creates no reservation or output");

        var freshAccess = new CountingAccess(new LocalTrialStore(Path.Combine(scratch, "fresh-access", "trial.json")));
        var freshExecutor = new FlacOptimizationExecutor(worker, publisher, freshAccess);
        var compressed = await worker.ProbeFlacAsync(new(Guid.NewGuid(), published), default);
        var beforeCount = Directory.GetFiles(scratch, "*.flac").Length;
        var unchanged = await freshExecutor.ExecuteAsync(FlacOptimizationPlan.Create(Guid.NewGuid(), [compressed], new("optimize", new())).Confirm(false, false), null, default);
        check(unchanged.Results.Single().State == OperationState.Unchanged && Directory.GetFiles(scratch, "*.flac").Length == beforeCount,
            "FLAC workflow: no smaller result keeps the original without publishing a duplicate");

        var changedPath = Path.Combine(scratch, "Changed.flac"); await File.WriteAllBytesAsync(changedPath, padded);
        var changed = await worker.ProbeFlacAsync(new(Guid.NewGuid(), changedPath), default);
        await using (var writer = new FileStream(changedPath, FileMode.Append, FileAccess.Write)) await writer.WriteAsync(new byte[] { 1 });
        var failed = await freshExecutor.ExecuteAsync(FlacOptimizationPlan.Create(Guid.NewGuid(), [changed], new("optimize", new())).Confirm(false, false), null, default);
        check(failed.Results.Single().State == OperationState.Failed && new FileInfo(changedPath).Length == padded.Length + 1,
            "FLAC workflow: changed source is retained and its reservation abandoned");

        var corruptPath = Path.Combine(scratch, "Corrupt.flac"); var corrupt = padded.ToArray(); corrupt[^1] ^= 1; await File.WriteAllBytesAsync(corruptPath, corrupt);
        var bad = await worker.ProbeFlacAsync(new(Guid.NewGuid(), corruptPath), default);
        var good = await worker.ProbeFlacAsync(new(Guid.NewGuid(), path), default);
        var mixed = await freshExecutor.ExecuteAsync(FlacOptimizationPlan.Create(Guid.NewGuid(), [bad, good], new("optimize", new())).Confirm(false, false), null, default);
        check(mixed.Results[0].State == OperationState.Failed && mixed.Results[1].State == OperationState.Succeeded && worker.ProcessId == workerId,
            "FLAC workflow: corrupt frame fails safely and the next file uses the same worker");

        var a = await worker.ProbeFlacAsync(new(Guid.NewGuid(), path), default);
        var b = await worker.ProbeFlacAsync(new(Guid.NewGuid(), secondPath), default);
        using var cancel = new CancellationTokenSource();
        var cancelled = await freshExecutor.ExecuteAsync(FlacOptimizationPlan.Create(Guid.NewGuid(), [a, b], new("optimize", new())).Confirm(false, false), (_, row) =>
        { if (row.State == OperationState.Succeeded) cancel.Cancel(); }, cancel.Token);
        check(cancelled.Results[0].State == OperationState.Succeeded && cancelled.Results[1].State == OperationState.Cancelled,
            "FLAC workflow: cancellation between files retains completed output and skips remaining work");

        var reservedSource = await worker.ProbeFlacAsync(new(Guid.NewGuid(), path), default);
        var reserved = Path.Combine(scratch, $".context-suite-{reservedSource.ItemId:N}.tmp");
        await File.WriteAllTextAsync(reserved, "nonempty reservation canary");
        try { await worker.OptimizeFlacAsync(new(reservedSource, reserved), default); check(false, "FLAC workflow: nonempty reservation"); }
        catch (MediaWorkerException) { check(await File.ReadAllTextAsync(reserved) == "nonempty reservation canary", "FLAC workflow: nonempty reservation refuses all writes"); }
        File.Delete(reserved);
        var linkTarget = Path.Combine(scratch, "empty-link-target.tmp"); await File.WriteAllBytesAsync(linkTarget, []);
        if (!CreateHardLinkW(reserved, linkTarget, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            try { await worker.OptimizeFlacAsync(new(reservedSource, reserved), default); check(false, "FLAC workflow: linked reservation"); }
            catch (MediaWorkerException) { check(new FileInfo(linkTarget).Length == 0, "FLAC workflow: hard-linked reservation refuses all writes"); }
        }
        finally { File.Delete(reserved); File.Delete(linkTarget); }
        check(Directory.GetFiles(records).Length == 0 && !Directory.GetFiles(scratch, ".context-suite-*.tmp").Any(),
            "FLAC workflow: success, no-change, cancellation and failure close publication records and temporary files");
        await File.WriteAllTextAsync(Path.Combine(scratch, "flac-workflow.json"), JsonSerializer.Serialize(new { firstBatch = result, unchanged, failed, mixed, cancelled },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class CountingAccess(IOperationAccess inner) : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => inner.ReadAccessAsync(token);
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected image conversion admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected PNG admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedFlacOptimization confirmed, CancellationToken token)
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
            throw new InvalidOperationException("Isolated copy workflow must never recycle.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string path, string existing, IntPtr security);
}
