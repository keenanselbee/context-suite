using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.ContractTests;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using Microsoft.Win32.SafeHandles;

internal static class PdfFailureContracts
{
    public static async Task RunAsync(string root, string executable, string fixtures, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "large generated.pdf");
        WritePdf(path);
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var publisher = new OutputPublisher(Path.Combine(root, "interruption-records"), new NoRecycle());
        var clock = new ImageInterruptionContracts.DeadlineClock();
        var workerRoot = Path.Combine(root, "workers");
        await using var worker = new WorkerClient(executable, workerRoot, clock);
        var evidence = new List<object>();
        var baselineSource = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), path), default);
        var baseline = await new PdfOptimizationExecutor(worker, publisher, new LocalTrialStore(Path.Combine(root, "trial", "trial.json")))
            .ExecuteAsync(PdfOptimizationPlan.Create(Guid.NewGuid(), [baselineSource], new("optimize", new())).Confirm(), null, default);
        check(baseline.Results.Single().Publication?.Outcome == PublicationOutcome.CopyCreated,
            "PDF failure: generated large native-work fixture first completes validated optimization");
        var longDirectory = Path.Combine(root, "long-source", new string('s', 90), new string('t', 90));
        Directory.CreateDirectory(longDirectory);
        var longPath = Path.Combine(longDirectory, "generated ü.pdf");
        File.Copy(Path.Combine(fixtures, "authored original ü.pdf"), longPath);
        var expectedLongSource = await File.ReadAllBytesAsync(longPath);
        var longScratch = Path.Combine(root, "long-scratch", new string('a', 90), new string('b', 90));
        await using (var longWorker = new WorkerClient(executable, longScratch))
        {
            var snapshotFacts = await longWorker.ProbePdfAsync(expectedLongSource, default);
            check(snapshotFacts.PageCount == 2, "PDF failure: deep native snapshot directory works independently of source-file handles");
            var longSource = await longWorker.ProbePdfFileAsync(new(Guid.NewGuid(), longPath), default);
            check(longPath.Length > 260 && longScratch.Length > 260 && longSource.Facts.PageCount == 2,
                "PDF failure: source and native scratch paths beyond MAX_PATH remain readable");
            var result = await new PdfOptimizationExecutor(longWorker, publisher, new LocalTrialStore(Path.Combine(root, "trial", "trial.json")))
                .ExecuteAsync(PdfOptimizationPlan.Create(Guid.NewGuid(), [longSource], new("optimize", new())).Confirm(), null, default);
            check(result.Results.Single().Publication?.Outcome == PublicationOutcome.CopyCreated &&
                result.Results.Single().Publication!.OutputPath!.Length > 260 &&
                (await File.ReadAllBytesAsync(longPath)).SequenceEqual(expectedLongSource),
                "PDF failure: deep native snapshots optimize into a validated long-path copy without changing source");
        }
        foreach (var fault in new[] { "cancel", "timeout", "worker-crash" })
        {
            var source = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), path), default);
            var reservation = await publisher.ReserveAsync(new(source.ItemId, path, "pdf", new("optimize", new())));
            using var cancel = new CancellationTokenSource();
            var operation = worker.OptimizePdfAsync(new(source, reservation.TemporaryPath), cancel.Token);
            var processId = worker.ProcessId!.Value;
            Process? native = null;
            try
            {
                var watch = Stopwatch.StartNew();
                while (!operation.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(15))
                {
                    native = FindChild(processId, Path.Combine(Path.GetDirectoryName(executable)!, "pdf-engine", "qpdf.exe"));
                    if (native is not null && !native.HasExited) break;
                    native?.Dispose(); native = null;
                    await Task.Delay(2);
                }
                if (native is null || native.HasExited || operation.IsCompleted)
                    throw new InvalidOperationException("Did not observe live qpdf owned by active worker: " + fault);
                check(IsLocked(reservation.TemporaryPath), "PDF failure: live native child and checked output lock observed before " + fault);
                var nativeId = native.Id;
                if (fault == "cancel") cancel.Cancel();
                else if (fault == "timeout")
                {
                    check(clock.Current!.Due == TimeSpan.FromSeconds(120), "PDF failure: client uses reviewed 120-second deadline");
                    clock.Current.Expire();
                }
                else
                {
                    using var owned = Process.GetProcessById(processId);
                    owned.Kill(); // Worker only: its native job must clean up qpdf.
                }
                try { await operation.WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Interrupted PDF operation succeeded."); }
                catch (OperationCanceledException) when (fault == "cancel") { check(true, "PDF failure: native-phase cancellation remains cancellation"); }
                catch (MediaWorkerException error) when (fault != "cancel")
                {
                    check(error.Failure == (fault == "timeout" ? ImageFailure.TimedOut : ImageFailure.WorkerTerminated),
                        "PDF failure: typed native-phase failure for " + fault);
                }
                await native.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                check(native.HasExited && worker.ProcessId is null, "PDF failure: native child and worker exit after " + fault);
                var result = await publisher.AbandonAsync(reservation, fault == "cancel");
                check(result.Outcome == (fault == "cancel" ? PublicationOutcome.Cancelled : PublicationOutcome.Failed) &&
                    !File.Exists(reservation.TemporaryPath) && !File.Exists(reservation.Record.OutputPath) &&
                    !Directory.EnumerateDirectories(workerRoot).Any() && SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(hash),
                    "PDF failure: original survives and owned output/scratch are cleaned after " + fault);
                var fresh = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), Path.Combine(fixtures, "authored original ü.pdf")), default);
                var executor = new PdfOptimizationExecutor(worker, publisher, new LocalTrialStore(Path.Combine(root, "trial", "trial.json")));
                var resumed = await executor.ExecuteAsync(PdfOptimizationPlan.Create(Guid.NewGuid(), [fresh], new("optimize", new(OutputDirectory: root))).Confirm(), null, default);
                check(worker.ProcessId != processId && resumed.Results.Single().Publication?.Outcome == PublicationOutcome.CopyCreated,
                    "PDF failure: fresh worker completes a valid copy after " + fault);
                evidence.Add(new { fault, worker = processId, native = nativeId, result, resumed });
            }
            finally
            {
                cancel.Cancel();
                try { await operation; } catch (Exception) { }
                if (!reservation.Finished) await publisher.AbandonAsync(reservation, true);
                native?.Dispose();
            }
        }
        foreach (var afterMove in new[] { false, true })
        {
            var faultRoot = Path.Combine(root, afterMove ? "after-move" : "before-move"); Directory.CreateDirectory(faultRoot);
            var source = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), Path.Combine(fixtures, "authored original ü.pdf")), default);
            var io = new FailingMove(afterMove);
            var faultPublisher = new OutputPublisher(Path.Combine(faultRoot, "records"), new NoRecycle(), io: io);
            var executor = new PdfOptimizationExecutor(worker, faultPublisher, new LocalTrialStore(Path.Combine(root, "trial", "trial.json")));
            var plan = PdfOptimizationPlan.Create(Guid.NewGuid(), [source], new("optimize", new(OutputDirectory: faultRoot))).Confirm();
            var execution = await executor.ExecuteAsync(plan, null, default);
            var result = execution.Results.Single();
            check(io.Reached && result.Publication?.IsCommitted == afterMove && result.State == (afterMove ? OperationState.Succeeded : OperationState.Failed),
                "PDF failure: move error distinguishes committed copy from unpublished result: " + afterMove);
            var recordPath = faultPublisher.FindRecoveryRecords().Single();
            var record = JsonSerializer.Deserialize<PublicationRecord>(await File.ReadAllTextAsync(recordPath))!;
            check(await PublicationFiles.MatchesAsync(record.SourcePath, record.Source) && record.Candidate is not null &&
                await PublicationFiles.MatchesAsync(afterMove ? record.OutputPath : record.TemporaryPath, record.Candidate) &&
                new OutputPublisher(Path.Combine(faultRoot, "records"), new NoRecycle()).FindRecoveryRecords().Contains(recordPath),
                "PDF failure: restart retains original and validated recovery evidence after move error: " + afterMove);
            var next = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), source.Path), default);
            var completed = await executor.ExecuteAsync(PdfOptimizationPlan.Create(Guid.NewGuid(), [next], plan.Plan.Settings).Confirm(), null, default);
            check(completed.Results.Single().Publication?.Outcome == PublicationOutcome.CopyCreated && File.Exists(recordPath),
                "PDF failure: later copy completes without deleting retained recovery evidence: " + afterMove);
            evidence.Add(new { afterMove, execution, completed, recordPath });
        }
        await PdfPublicationCrashContracts.RunAsync(Path.Combine(root, "app-crashes"), executable, fixtures, check);
        await File.WriteAllTextAsync(Path.Combine(root, "pdf-failures.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool IsLocked(string path)
    {
        try { using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return false; }
        catch (IOException error) when ((error.HResult & 0xffff) == 32) { return true; }
    }

    private static Process? FindChild(int parent, string executable)
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (!Process32FirstW(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
        do
        {
            if (entry.Parent != parent || !string.Equals(entry.Executable, "qpdf.exe", StringComparison.OrdinalIgnoreCase)) continue;
            Process? child = null;
            try
            {
                child = Process.GetProcessById((int)entry.Id);
                if (!child.HasExited && string.Equals(child.MainModule!.FileName, executable, StringComparison.OrdinalIgnoreCase)) return child;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { }
            child?.Dispose();
        } while (Process32NextW(snapshot, ref entry));
        return null;
    }

    private static void WritePdf(string path)
    {
        using var file = File.Create(path);
        var offsets = new List<long> { 0 };
        Write("%PDF-1.4\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> /Contents 4 0 R >>");
        var content = "%" + new string('a', 8 * 1024 * 1024) + "\n";
        Object(4, $"<< /Length {content.Length} >>\nstream\n{content}endstream");
        var xref = file.Position;
        Write("xref\n0 5\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        void Write(string text) { file.Write(Encoding.ASCII.GetBytes(text)); }
        void Object(int id, string body) { offsets.Add(file.Position); Write($"{id} 0 obj\n{body}\nendobj\n"); }
    }

    private sealed class FailingMove(bool afterMove) : PublicationIo
    {
        public bool Reached { get; private set; }
        public override void Move(string source, string destination)
        {
            if (Reached) { base.Move(source, destination); return; }
            Reached = true;
            if (afterMove) base.Move(source, destination);
            throw new IOException("Injected isolated PDF publication failure.");
        }
    }
    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) => throw new InvalidOperationException("PDF failure tests never recycle.");
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id;
        public UIntPtr Heap;
        public uint Module, Threads, Parent;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32FirstW(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32NextW(SafeFileHandle snapshot, ref ProcessEntry entry);
}
