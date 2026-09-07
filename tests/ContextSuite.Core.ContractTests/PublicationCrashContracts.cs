using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;

namespace ContextSuite.Core.ContractTests;

internal static class PublicationCrashContracts
{
    private const string Original = "Disposable source preserved across forced process termination.";

    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        foreach (var stage in new[] { "Prepared", "Validated", "Publishing", "OriginalMoved", "PublishedBeforeRecord", "Committed" })
        {
            var root = Path.Combine(Path.GetFullPath(scratch), "crash-" + stage + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
                start.ArgumentList.Add(typeof(PublicationCrashContracts).Assembly.Location);
            start.ArgumentList.Add("--crash-publication");
            start.ArgumentList.Add(root);
            start.ArgumentList.Add(stage);
            using var child = Process.Start(start) ?? throw new IOException("Crash-test child could not start.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await child.WaitForExitAsync(timeout.Token); }
            catch { if (!child.HasExited) child.Kill(true); throw; }
            check(File.Exists(Path.Combine(root, "checkpoint.txt")) && child.ExitCode != 0,
                "crash: forced termination reached " + stage);
            var publisher = new OutputPublisher(Path.Combine(root, "records"), new RetainOriginal());
            var recordPath = publisher.FindRecoveryRecords().Single();
            var record = JsonSerializer.Deserialize<PublicationRecord>(await File.ReadAllTextAsync(recordPath))!;
            var originalPath = await PublicationFiles.MatchesAsync(record.SourcePath, record.Source)
                ? record.SourcePath : record.BackupPath;
            check(originalPath is not null && await File.ReadAllTextAsync(originalPath) == Original,
                "crash: exact original survives " + stage);
            // Reconstructing services discovers evidence only; it must never interpret leftovers as garbage.
            _ = new OutputPublisher(Path.Combine(root, "records"), new RetainOriginal()).FindRecoveryRecords();
            check(File.Exists(recordPath) && File.Exists(originalPath), "crash: restart preserves evidence " + stage);
        }
    }

    public static async Task<int> RunChildAsync(string root, string stage)
    {
        var source = Path.Combine(root, "source.png");
        await File.WriteAllTextAsync(source, Original);
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new RetainOriginal(), true, new TerminatingIo(root, stage));
        var intent = new OutputIntent(Guid.NewGuid(), source, "png", new("convert", new(true)), true, true);
        var reservation = await publisher.ReserveAsync(intent);
        await File.WriteAllTextAsync(reservation.TemporaryPath, "new output");
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(reservation.TemporaryPath)));
        await publisher.PublishAsync(reservation, new(intent.ItemId, hash, true));
        throw new InvalidOperationException("Crash checkpoint was not reached.");
    }

    private sealed class TerminatingIo(string root, string stage) : PublicationIo
    {
        public override void Checkpoint(PublicationStage current)
        {
            if (current.ToString() == stage) Terminate();
        }
        public override void Replace(string temporary, string source, string backup)
        {
            if (stage == "OriginalMoved")
            {
                File.Move(source, backup, overwrite: true); // Publisher-owned empty marker only.
                Terminate();
            }
            base.Replace(temporary, source, backup);
            if (stage == "PublishedBeforeRecord") Terminate();
        }
        private void Terminate()
        {
            File.WriteAllText(Path.Combine(root, "checkpoint.txt"), stage);
            Process.GetCurrentProcess().Kill();
        }
    }
    private sealed class RetainOriginal : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken)
        {
            return Task.FromResult(new RecycleResult(false, "Crash tests never recycle originals."));
        }
    }
}
