using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static class AnalyzeBenchmark
{
    public static async Task<int> RunAsync(string specification)
    {
        var path = Path.GetFullPath(specification);
        if (!path.Contains(Path.DirectorySeparatorChar + ".codex-temp" + Path.DirectorySeparatorChar + "analyze-benchmark" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Use generated benchmark scratch.");
        var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(path)) ?? throw new InvalidDataException("Missing benchmark request.");
        var fixtures = Path.Combine(Path.GetDirectoryName(path)!, "fixtures") + Path.DirectorySeparatorChar;
        if (request.Paths.Length is < 1 or > 32 || request.Iterations is < 2 or > 20 ||
            request.Paths.Any(file => !Path.GetFullPath(file).StartsWith(fixtures, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Benchmark accepts only its bounded generated fixtures.");
        var samples = new List<object>();
        for (var index = 0; index < request.Iterations; index++)
        {
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var timer = Stopwatch.StartNew();
            var observed = new List<object>();
            if (request.Batch)
            {
                await using var worker = new WorkerClient(Path.Combine(Path.GetDirectoryName(path)!, "absent-worker.exe"));
                await using var view = new MainViewModel(worker, trial: new NoAccess());
                view.Admit(new(Guid.NewGuid(), "analyze", "open-details", [.. request.Paths]));
                await view.WaitForIdleAsync();
                if (view.Rows.Count != request.Paths.Length || view.Rows.Any(row => row.HasOutput || row.Result.Publication is not null))
                    throw new InvalidDataException("Analyze batch shape or read-only behavior changed.");
                observed.AddRange(view.Rows.Select(row => Observation(row.Path, row.Analysis, row.Analysis is null ? row.Result.State.ToString() : null)));
            }
            else
            {
                foreach (var file in request.Paths)
                {
                    try { observed.Add(Observation(file, await FileAnalysisReader.ReadAsync(file, CancellationToken.None), null)); }
                    catch (Exception error) when (error is IOException or Win32Exception)
                    { observed.Add(Observation(file, null, error.GetType().Name)); }
                }
            }
            timer.Stop();
            samples.Add(new { Iteration = index, Milliseconds = timer.Elapsed.TotalMilliseconds,
                ManagedAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated, Files = observed });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Runtime = Environment.Version.ToString(),
            ProcessorCount = Environment.ProcessorCount, Samples = samples }));
        return 0;
    }

    private static object Observation(string path, FileAnalysis? result, string? error) => new
    {
        Name = Path.GetFileName(path), result?.FileBytes, result?.InspectedBytes,
        FormatId = result?.Identity.FormatId, Confidence = result?.Identity.Confidence.ToString(), Error = error
    };

    private sealed record Request(string[] Paths, bool Batch, int Iterations);
    private sealed class NoAccess : IOperationAccess
    {
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Analyze accessed licensing.");
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken cancellationToken) => throw new InvalidOperationException("Analyze admitted conversion.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken cancellationToken) => throw new InvalidOperationException("Analyze admitted optimization.");
    }
}
