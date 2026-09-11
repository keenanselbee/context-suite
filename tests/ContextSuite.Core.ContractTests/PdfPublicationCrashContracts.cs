using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

internal static class PdfPublicationCrashContracts
{
    public static async Task RunAsync(string root, string executable, string fixtures, Action<bool, string> check)
    {
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(fixtures, "authored original ü.pdf")));
        foreach (var stage in new[] { "Prepared", "Validated", "Publishing", "PublishedBeforeRecord", "Committed" })
        {
            var directory = Path.Combine(root, stage); Directory.CreateDirectory(directory);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(typeof(PdfPublicationCrashContracts).Assembly.Location);
            foreach (var argument in new[] { "--pdf-publication-crash", directory, executable, fixtures, stage }) start.ArgumentList.Add(argument);
            using var child = Process.Start(start) ?? throw new IOException("PDF crash child did not start.");
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch { if (!child.HasExited) child.Kill(true); throw; }
            check(child.ExitCode != 0 && await File.ReadAllTextAsync(Path.Combine(directory, "checkpoint.txt")) == stage,
                "PDF app crash: actual publication checkpoint reached " + stage);
            var publisher = new OutputPublisher(Path.Combine(directory, "records"), new NoRecycle());
            var recordPath = publisher.FindRecoveryRecords().Single();
            var recordBytes = await File.ReadAllBytesAsync(recordPath);
            var record = JsonSerializer.Deserialize<PublicationRecord>(recordBytes)!;
            check(record.BackupPath is null && SHA256.HashData(await File.ReadAllBytesAsync(record.SourcePath)).SequenceEqual(sourceHash),
                "PDF app crash: exact original remains in place at " + stage);
            var published = stage is "PublishedBeforeRecord" or "Committed";
            check(File.Exists(record.OutputPath) == published && File.Exists(record.TemporaryPath) != published &&
                (stage == "Prepared" ? record.Candidate is null && new FileInfo(record.TemporaryPath).Length == 0 :
                    record.Candidate is not null && await PublicationFiles.MatchesAsync(published ? record.OutputPath : record.TemporaryPath, record.Candidate)),
                "PDF app crash: candidate and publication state match checkpoint " + stage);
            check(new OutputPublisher(Path.Combine(directory, "records"), new NoRecycle()).FindRecoveryRecords().Contains(recordPath) &&
                (await File.ReadAllBytesAsync(recordPath)).SequenceEqual(recordBytes),
                "PDF app crash: restart discovers and preserves recovery evidence at " + stage);
            var identity = JsonSerializer.Deserialize<WorkerIdentity>(await File.ReadAllTextAsync(Path.Combine(directory, "worker.json")))!;
            Process? owned = null;
            try
            {
                try { owned = Process.GetProcessById(identity.Id); }
                catch (ArgumentException) { }
                if (owned is not null && !owned.HasExited && owned.StartTime.ToUniversalTime().Ticks == identity.StartTicks)
                    await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                check(owned is null || owned.HasExited || owned.StartTime.ToUniversalTime().Ticks != identity.StartTicks,
                    "PDF app crash: worker exits after application death at " + stage);
            }
            finally { owned?.Dispose(); }
        }
    }

    public static async Task<int> RunChildAsync(string root, string executable, string fixtures, string stage)
    {
        var path = Path.Combine(root, "source.pdf");
        File.Copy(Path.Combine(fixtures, "authored original ü.pdf"), path);
        await using var worker = new WorkerClient(executable, Path.Combine(root, "worker-scratch"));
        var source = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), path), default);
        using (var process = Process.GetProcessById(worker.ProcessId!.Value))
            await File.WriteAllTextAsync(Path.Combine(root, "worker.json"), JsonSerializer.Serialize(new WorkerIdentity(process.Id, process.StartTime.ToUniversalTime().Ticks)));
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle(), io: new TerminatingIo(root, stage));
        var executor = new PdfOptimizationExecutor(worker, publisher, new LocalTrialStore(Path.Combine(root, "trial", "trial.json")));
        await executor.ExecuteAsync(PdfOptimizationPlan.Create(Guid.NewGuid(), [source], new("optimize", new())).Confirm(), null, default);
        throw new InvalidOperationException("PDF publication crash checkpoint was not reached.");
    }

    private sealed record WorkerIdentity(int Id, long StartTicks);
    private sealed class TerminatingIo(string root, string stage) : PublicationIo
    {
        public override void Checkpoint(PublicationStage current)
        {
            if (current.ToString() == stage) Terminate();
        }
        public override void Move(string source, string destination)
        {
            base.Move(source, destination);
            if (stage == "PublishedBeforeRecord") Terminate();
        }
        private void Terminate()
        {
            File.WriteAllText(Path.Combine(root, "checkpoint.txt"), stage);
            Process.GetCurrentProcess().Kill();
        }
    }
    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) => throw new InvalidOperationException("PDF crash tests never recycle.");
    }
}
