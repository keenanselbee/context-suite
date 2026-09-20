using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

internal static class HeifPerformanceContracts
{
    private sealed record Sample(string Operation, int Iteration, double Seconds, int WorkerPid,
        long WorkerPeakWorkingSetBytes, string Output, string Sha256, long Bytes, double SampledRgbRmse);

    internal static async Task RunAsync(string evidence, string executable, string fixtures)
    {
        evidence = Path.GetFullPath(evidence);
        if (!evidence.Contains("\\.codex-temp\\heif-performance\\", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(evidence) != "runs" || Directory.Exists(evidence))
            throw new ArgumentException("Use fresh isolated HEIF performance evidence.");
        var sources = new[] { "heic", "avif" }.Select(extension => Path.Combine(fixtures, "performance." + extension)).ToArray();
        var originals = sources.ToDictionary(path => path,
            path => new { Sha256 = Hash(path), Written = File.GetLastWriteTimeUtc(path), Bytes = new FileInfo(path).Length });
        Directory.CreateDirectory(evidence);
        var samples = new List<Sample>();
        for (var iteration = 0; iteration < 5; iteration++)
            foreach (var source in sources)
                foreach (var target in new[] { ImageFormat.Jpeg, ImageFormat.Png })
                {
                    samples.Add(await MeasureAsync(source, target, iteration));
                    await File.WriteAllTextAsync(Path.Combine(evidence, "samples.json"),
                        JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine($"{samples[^1].Operation} / {iteration + 1}: {samples[^1].Seconds:F3} s");
                }
        var groups = samples.GroupBy(sample => sample.Operation).Select(group =>
        {
            var times = group.Select(sample => sample.Seconds).Order().ToArray();
            return new { Operation = group.Key, Runs = times.Length, MedianSeconds = times[2],
                MaximumSeconds = times[^1], Passed = times.Length == 5 && times[2] <= 10 && times[^1] <= 20 };
        }).ToArray();
        var preserved = originals.All(pair => Hash(pair.Key) == pair.Value.Sha256 && File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Written);
        var passed = preserved && samples.Count == 20 && groups.Length == 4 && groups.All(group => group.Passed);
        await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, OriginalsPreserved = preserved, MedianLimitSeconds = 10, MaximumLimitSeconds = 20,
            Groups = groups, Samples = samples, Sources = originals,
            Scope = "4000x3000 opaque upright 8-bit SDR, lossless HEIC/AVIF with full-range BT.709 matrix and sRGB transfer. " +
                "Fresh application model/worker, direct command through validated copy publication. " +
                "Excludes fixture encoding, source/output audit hashing, output inspection and disposal. " +
                "Managed worker peak only; cold storage, broader fidelity, human review and final candidate remain separate."
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!passed) throw new InvalidOperationException("HEIF performance budget missed; retain all recorded samples.");

        async Task<Sample> MeasureAsync(string source, ImageFormat target, int iteration)
        {
            var operation = Path.GetExtension(source)[1..] + "-to-" + target.ToString().ToLowerInvariant();
            var directory = Path.Combine(evidence, operation + "-" + iteration); Directory.CreateDirectory(directory);
            var outputs = Path.Combine(directory, "outputs"); Directory.CreateDirectory(outputs);
            var records = Path.Combine(directory, "publications");
            var scratch = Path.Combine(directory, "worker");
            Process? process = null;
            Sample sample;
            var watch = Stopwatch.StartNew();
            try
            {
                await using (var worker = new WorkerClient(executable, scratch))
                await using (var vm = new MainViewModel(worker,
                    new SuiteSettings { Convert = new(OutputDirectory: outputs), PlayCompletionSound = false },
                    new OutputPublisher(records, new ForbiddenRecycle()), new LocalTrialStore(Path.Combine(directory, "trial.json"))))
                {
                    vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Routine HEIF commands must not open a planner.");
                    vm.Admit(new(Guid.NewGuid(), "convert", target == ImageFormat.Jpeg ? "jpeg" : "png", [source]));
                    await vm.WaitForIdleAsync();
                    watch.Stop();
                    var row = vm.Rows.Single();
                    if (row.Result.State != OperationState.Succeeded || vm.HasProblems ||
                        !row.HasOutput || row.Result.Publication?.Outcome != PublicationOutcome.CopyCreated)
                        throw new InvalidDataException("HEIF benchmark did not publish a validated copy: " + row.Status);
                    process = Process.GetProcessById(worker.ProcessId ?? throw new IOException("Worker did not start."));
                    process.Refresh();
                    var peak = process.PeakWorkingSet64;
                    var path = Directory.GetFiles(outputs).Single();
                    var extension = target == ImageFormat.Jpeg ? "jpg" : "png";
                    if (Path.GetFileName(path) != "performance - Converted." + extension)
                        throw new InvalidDataException("Unexpected output name.");
                    var facts = await worker.ProbeAsync(new(Guid.NewGuid(), path), default);
                    await File.WriteAllTextAsync(Path.Combine(directory, "output-facts.json"), JsonSerializer.Serialize(new
                    {
                        facts.Format, facts.Width, facts.Height, facts.BitDepth, facts.HasTransparency,
                        facts.Orientation, facts.ProfileNames
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    if (facts.Format != target || facts.Width != 4000 || facts.Height != 3000 || facts.BitDepth != 8 ||
                        facts.HasTransparency || facts.Orientation is not (0 or 1) || !facts.ProfileNames.Contains("icc"))
                        throw new InvalidDataException("Output geometry, precision, alpha, orientation or profile changed.");
                    var rmse = Inspect(path);
                    if (rmse > (target == ImageFormat.Png ? 3 : 8))
                        throw new InvalidDataException($"Authored signal differs: {operation}, RGB RMSE {rmse}.");
                    sample = new(operation, iteration, watch.Elapsed.TotalSeconds, process.Id, peak,
                        path, Hash(path), new FileInfo(path).Length, rmse);
                }
                if (!process.HasExited || Directory.EnumerateFiles(records).Any() || Directory.EnumerateDirectories(scratch).Any() ||
                    Directory.EnumerateFiles(directory, ".context-suite-*.tmp", SearchOption.AllDirectories).Any() ||
                    originals.Any(pair => Hash(pair.Key) != pair.Value.Sha256 || File.GetLastWriteTimeUtc(pair.Key) != pair.Value.Written))
                    throw new IOException("HEIF benchmark changed a source or left a worker/publication artifact.");
                return sample;
            }
            finally { process?.Dispose(); }
        }
    }

    private static double Inspect(string path)
    {
        using var input = File.OpenRead(path);
        var frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        if (bitmap.PixelWidth != 4000 || bitmap.PixelHeight != 3000) throw new InvalidDataException("Independent decoder geometry differs.");
        var pixels = new byte[4000 * 3000 * 4]; bitmap.CopyPixels(pixels, 4000 * 4, 0);
        double squares = 0; var count = 0;
        // A 31-pixel stride visits every phase of the 16-level authored detail.
        // Full-range BT.709 YCbCr to RGB; declared transfer is already sRGB.
        for (var y = 7; y < 2993; y += 31)
        for (var x = 7; x < 3993; x += 31)
        {
            var luma = 32 + x * 160 / 3999 + y * 31 / 2999 + ((x * 13 + y * 7) & 15);
            var cb = 96 + (x / 2) * 64 / 1999 - 128;
            var cr = 96 + (y / 2) * 64 / 1499 - 128;
            var expected = new[] { luma + 1.8556 * cb, luma - 0.187324 * cb - 0.468124 * cr, luma + 1.5748 * cr };
            var offset = (y * 4000 + x) * 4;
            if (pixels[offset + 3] != 255) throw new InvalidDataException("Opaque output became transparent.");
            for (var channel = 0; channel < 3; channel++)
            {
                squares += Math.Pow(pixels[offset + channel] - Math.Clamp(expected[channel], 0, 255), 2); count++;
            }
        }
        return Math.Sqrt(squares / count);
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input));
    }

    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("HEIF performance sources must remain copies.");
    }
}
