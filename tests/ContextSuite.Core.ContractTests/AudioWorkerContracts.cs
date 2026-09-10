using System.Security.Cryptography;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static class AudioWorkerContracts
{
    public static async Task RunAsync(string scratch, string executable, string fixtures, Action<bool, string> check, string? artworkFixture = null)
    {
        Directory.CreateDirectory(scratch);
        var paths = new[] { "wav", "flac", "mp3", "m4a", "ogg", "opus" }.Select(format => Path.Combine(fixtures, "source." + format))
            .Concat(artworkFixture is null ? [] : new[] { artworkFixture }).ToArray();
        var hashes = paths.Select(path => SHA256.HashData(File.ReadAllBytes(path))).ToArray();
        var malformed = Path.Combine(scratch, "malformed.mp3");
        var unknown = Path.Combine(scratch, "unknown.bin");
        await File.WriteAllBytesAsync(malformed, [0, 1, 2, 3, 255]);
        await File.WriteAllBytesAsync(unknown, [0, 1, 2, 3, 255]);
        await using var worker = new WorkerClient(executable, Path.Combine(scratch, "workers"));
        check(worker.HasAudioProbe, "audio worker: isolated optional engine is present");
        await using var view = new MainViewModel(worker, trial: new ForbiddenAccess());
        view.Admit(new(Guid.NewGuid(), "analyze", "open-details", [.. paths, malformed, unknown]));
        await view.WaitForIdleAsync();
        for (var index = 0; index < paths.Length; index++)
        {
            var row = view.Rows[index];
            check(row.Result.State == OperationState.Succeeded && row.Analysis!.Facts.Any(fact =>
                fact.Id == "audio.probe.stream.0.rate" && fact.Integer == 48000), "audio worker: integrated Analyze facts for " + Path.GetExtension(paths[index]));
        }
        if (artworkFixture is not null)
        {
            var probed = await worker.ProbeAudioAsync(await File.ReadAllBytesAsync(artworkFixture), default);
            check(probed.Streams.Count(stream => stream.AttachedPicture) == 2, "audio worker: two attached pictures survive IPC serialization");
            var report = view.Rows[paths.Length - 1].Analysis!;
            check(report.Facts.Count(fact => fact.Id.EndsWith(".artwork", StringComparison.Ordinal) && fact.Text == "Embedded artwork") == 2 &&
                report.Facts.All(fact => !fact.Group.Contains("embedded artwork", StringComparison.Ordinal) || !fact.Id.EndsWith(".rate", StringComparison.Ordinal)),
                "audio worker: Analyze identifies embedded artwork without irrelevant audio properties");
        }
        check(view.Rows[paths.Length].Result.State == OperationState.Succeeded && view.Rows[paths.Length].Analysis!.Warnings.Any(warning => warning.Contains("Deeper audio")),
            "audio worker: malformed audio retains usable header report");
        check(view.Rows[paths.Length + 1].Result.State == OperationState.Succeeded && view.Rows[paths.Length + 1].Analysis!.Identity.FormatId is null,
            "audio worker: failure does not stop the next unknown file");
        check(view.Rows.All(row => row.Result.Publication is null && !row.HasOutput), "audio worker: no Analyze publication or license admission");
        check(paths.Select((path, index) => SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hashes[index])).All(value => value),
            "audio worker: all generated original hashes preserved");
        await File.WriteAllTextAsync(Path.Combine(scratch, "analysis-results.txt"), string.Join("\n\n", view.Rows.Select(row => row.AnalysisSummary + "\n" + row.AnalysisDetails)));
    }

    private sealed class ForbiddenAccess : IOperationAccess
    {
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Analyze accessed licensing.");
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken cancellationToken) => throw new InvalidOperationException("Analyze admitted conversion.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken cancellationToken) => throw new InvalidOperationException("Analyze admitted optimization.");
    }
}
