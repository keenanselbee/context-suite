using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Transport;

internal static class OptionalAnalyzeBenchmark
{
    private sealed record Request(string Path, string Worker, string Format, string? Codec, int Rate);

    internal static async Task<int> RunAsync(string specification)
    {
        var path = Path.GetFullPath(specification);
        if (!path.Contains("\\.codex-temp\\optional-analyze-performance\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use isolated optional Analyze benchmark evidence.");
        var directory = Path.GetDirectoryName(path)!;
        var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(path)) ?? throw new InvalidDataException("Missing request.");
        if (Path.GetDirectoryName(Path.GetFullPath(request.Path)) != Path.Combine(directory, "fixtures") ||
            request.Format is not ("wave" or "flac" or "mp3" or "m4a" or "ogg" or "pdf") ||
            request.Rate is not (0 or 44100 or 48000))
            throw new InvalidDataException("Use the declared generated audio/PDF fixtures.");
        var run = Path.Combine(directory, Path.GetFileNameWithoutExtension(path));
        if (Directory.Exists(run)) throw new IOException("Use a new benchmark session.");
        Directory.CreateDirectory(run);
        var before = Hash(request.Path); var written = File.GetLastWriteTimeUtc(request.Path);
        var records = Path.Combine(run, "records"); var scratch = Path.Combine(run, "worker");
        var samples = new List<object>();
        Process? process = null;
        try
        {
            await using (var worker = new WorkerClient(request.Worker, scratch))
            await using (var vm = new MainViewModel(worker,
                new() { Convert = new(OutputDirectory: run), PlayCompletionSound = false },
                new OutputPublisher(records, new NoRecycle()), new NoAccess()))
            {
                for (var iteration = 0; iteration < 6; iteration++)
                {
                    var allocated = GC.GetTotalAllocatedBytes(precise: true);
                    var watch = Stopwatch.StartNew();
                    vm.Admit(new(Guid.NewGuid(), "analyze", "open-details", [request.Path]));
                    await vm.WaitForIdleAsync();
                    watch.Stop();
                    var bytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
                    var row = vm.Rows.Last();
                    var analysis = row.Analysis ?? throw new InvalidDataException("Analyze returned no facts: " + row.Status);
                    var observed = new { analysis.Path, analysis.FileBytes, analysis.InspectedBytes,
                        analysis.Identity.FormatId, analysis.Identity.Basis, analysis.Identity.Confidence,
                        Facts = analysis.Facts.ToArray(), Warnings = analysis.Warnings.ToArray() };
                    File.WriteAllText(Path.Combine(run, $"observation-{iteration}.json"), JsonSerializer.Serialize(observed));
                    if (vm.Rows.Count != iteration + 1 || row.HasOutput || row.Result.Publication is not null ||
                        analysis.Identity.FormatId != request.Format || analysis.Identity.Basis !=
                            (request.Format == "m4a" ? IdentificationBasis.Filename : IdentificationBasis.Content) ||
                        analysis.InspectedBytes != (request.Format == "pdf" ? new FileInfo(request.Path).Length :
                            Math.Min(new FileInfo(request.Path).Length, WorkerCommand.MaximumAudioProbeBytes)) ||
                        analysis.Warnings.Any(value => value.Contains("Deeper", StringComparison.Ordinal)))
                        throw new InvalidDataException("Analyze benchmark lost identity, bounded inspection or read-only behavior.");
                    if (request.Format == "m4a" &&
                        (analysis.Facts.Single(fact => fact.Id == "audio.probe.container").Text != "mov,mp4,m4a,3gp,3g2,mj2" ||
                         !analysis.Warnings.Any(value => value.Contains("based only on the filename", StringComparison.Ordinal))))
                        throw new InvalidDataException("Shared MP4 container probe must retain the qualified filename identity.");
                    if (request.Format == "pdf")
                    {
                        if (analysis.Facts.Single(fact => fact.Id == "document.pages").Integer != 10 ||
                            analysis.Facts.Single(fact => fact.Id == "document.encryption").Boolean != false)
                            throw new InvalidDataException("PDF optional probe did not report the authored structure.");
                    }
                    else if (analysis.Facts.Single(fact => fact.Id == "audio.probe.stream.0.codec").Text != request.Codec ||
                        analysis.Facts.Single(fact => fact.Id == "audio.probe.stream.0.rate").Integer != request.Rate ||
                        analysis.Facts.Single(fact => fact.Id == "audio.probe.stream.0.channels").Integer != 2 ||
                        analysis.Facts.Single(fact => fact.Id == "audio.probe.bytes").Integer != analysis.InspectedBytes)
                        throw new InvalidDataException("Audio optional probe did not report the authored stream.");
                    process ??= Process.GetProcessById(worker.ProcessId ?? throw new IOException("Optional worker did not start."));
                    if (worker.ProcessId != process.Id) throw new IOException("Repeat inspection unexpectedly replaced the worker.");
                    process.Refresh();
                    samples.Add(new { Iteration = iteration, Seconds = watch.Elapsed.TotalSeconds,
                        ManagedAllocatedBytes = bytes, WorkerPid = process.Id, WorkerPeakWorkingSetBytes = process.PeakWorkingSet64,
                        Analysis = observed });
                }
            }
            if (process is null || !process.HasExited || Hash(request.Path) != before || File.GetLastWriteTimeUtc(request.Path) != written ||
                Directory.Exists(records) && Directory.EnumerateFileSystemEntries(records).Any() ||
                Directory.Exists(scratch) && Directory.EnumerateFileSystemEntries(scratch).Any() ||
                Directory.EnumerateFiles(run, ".context-suite-*.tmp", SearchOption.AllDirectories).Any())
                throw new IOException("Optional Analyze benchmark changed an input or left execution artifacts.");
            Console.WriteLine(JsonSerializer.Serialize(new { Passed = true, Runtime = Environment.Version.ToString(),
                SourceSha256 = before, SourceWritten = written, Samples = samples }));
            return 0;
        }
        finally { process?.Dispose(); }
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input));
    }

    private sealed class NoAccess : IOperationAccess
    {
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => throw new InvalidOperationException("Analyze accessed licensing.");
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException("Analyze admitted conversion.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new InvalidOperationException("Analyze admitted optimization.");
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) => throw new InvalidOperationException("Analyze attempted recycling.");
    }
}
