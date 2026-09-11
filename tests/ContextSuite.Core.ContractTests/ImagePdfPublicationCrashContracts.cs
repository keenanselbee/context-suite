using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;

internal static class ImagePdfPublicationCrashContracts
{
    private static readonly string[] Names = ["samples-16-3-True.png", "orientation-6.jpg"];

    public static async Task RunAsync(string root, string executable, string fixtures, Action<bool, string> check)
    {
        var originals = Names.Select(name => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixtures, name))))).ToArray();
        foreach (var stage in new[] { "Prepared", "Validated", "Publishing", "PublishedBeforeRecord", "Committed" })
        {
            var directory = Path.Combine(root, stage); Directory.CreateDirectory(directory);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(typeof(ImagePdfPublicationCrashContracts).Assembly.Location);
            foreach (var value in new[] { "--image-pdf-publication-crash", directory, executable, fixtures, stage }) start.ArgumentList.Add(value);
            using var child = Process.Start(start) ?? throw new IOException("Combined PDF crash child did not start.");
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch { if (!child.HasExited) child.Kill(true); throw; }
            check(child.ExitCode != 0 && File.ReadAllText(Path.Combine(directory, "checkpoint.txt")) == stage,
                "combined PDF app crash: real publication checkpoint reached " + stage);
            var publisher = new OutputPublisher(Path.Combine(directory, "records"), new NoRecycle());
            var recordPath = publisher.FindRecoveryRecords().Single();
            var bytes = File.ReadAllBytes(recordPath);
            var record = JsonSerializer.Deserialize<PublicationRecord>(bytes)!;
            var members = record.Sources!.Value;
            check(record.BackupPath is null && members.Length == 2 && members.Select(member => member.Fingerprint.Sha256).SequenceEqual(originals) &&
                members.Select(member => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(member.Path)))).SequenceEqual(originals),
                "combined PDF app crash: every original and ordered fingerprint survives " + stage);
            var committed = stage is "PublishedBeforeRecord" or "Committed";
            check(File.Exists(record.OutputPath) == committed && File.Exists(record.TemporaryPath) != committed &&
                (stage == "Prepared" ? record.Candidate is null && new FileInfo(record.TemporaryPath).Length == 0 :
                    record.Candidate is not null && await PublicationFiles.MatchesAsync(committed ? record.OutputPath : record.TemporaryPath, record.Candidate)),
                "combined PDF app crash: candidate and commit evidence match " + stage);
            check(new OutputPublisher(Path.Combine(directory, "records"), new NoRecycle()).FindRecoveryRecords().Contains(recordPath) &&
                File.ReadAllBytes(recordPath).AsSpan().SequenceEqual(bytes), "combined PDF app crash: restart discovery preserves recovery record " + stage);
            var identity = JsonSerializer.Deserialize<WorkerIdentity>(File.ReadAllText(Path.Combine(directory, "worker.json")))!;
            Process? owned = null;
            try
            {
                try { owned = Process.GetProcessById(identity.Id); } catch (ArgumentException) { }
                if (owned is not null && !owned.HasExited && owned.StartTime.ToUniversalTime().Ticks == identity.StartTicks)
                    await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                check(owned is null || owned.HasExited || owned.StartTime.ToUniversalTime().Ticks != identity.StartTicks,
                    "combined PDF app crash: owned worker exits with parent " + stage);
            }
            finally { owned?.Dispose(); }
        }
    }

    public static async Task<int> RunChildAsync(string root, string executable, string fixtures, string stage)
    {
        await using var worker = new WorkerClient(executable, Path.Combine(root, "worker-scratch"));
        var sources = new List<ImageSourceFacts>();
        foreach (var name in Names)
        {
            var path = Path.Combine(root, name); File.Copy(Path.Combine(fixtures, name), path);
            sources.Add(await worker.ProbeAsync(new(Guid.NewGuid(), path), default));
        }
        using (var owned = Process.GetProcessById(worker.ProcessId!.Value))
            File.WriteAllText(Path.Combine(root, "worker.json"), JsonSerializer.Serialize(new WorkerIdentity(owned.Id, owned.StartTime.ToUniversalTime().Ticks)));
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle(), io: new ExitAt(root, stage));
        var plan = ImagePdfPlan.Create(Guid.NewGuid(), sources, new("convert", new(ReplaceOriginals: true))).Confirm(true);
        await new ImagePdfExecutor(worker, publisher, new LocalTrialStore(Path.Combine(root, "trial.json"))).ExecuteAsync(plan, null, default);
        throw new Exception("Combined PDF crash checkpoint was not reached.");
    }

    private sealed record WorkerIdentity(int Id, long StartTicks);
    private sealed class ExitAt(string root, string stage) : PublicationIo
    {
        public override void Checkpoint(PublicationStage current) { if (current.ToString() == stage) Exit(); }
        public override void Move(string source, string destination) { base.Move(source, destination); if (stage == "PublishedBeforeRecord") Exit(); }
        private void Exit() { File.WriteAllText(Path.Combine(root, "checkpoint.txt"), stage); Process.GetCurrentProcess().Kill(); }
    }
    private sealed class NoRecycle : IFileRecycler
    { public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) => throw new Exception("Combined PDF crash tests never recycle."); }
}
