using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class OptimizationCorpus
{
    internal static async Task RunAsync(string scratch, string corpus)
    {
        var root = Path.Combine(Path.GetFullPath(scratch), "production-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var adapter = new ImageAdapter(Path.Combine(root, "scratch"));
        var results = new List<object>();
        foreach (var path in Directory.EnumerateFiles(corpus, "*.png").Order().Take(12))
        {
            var bytes = File.ReadAllBytes(path);
            var hash = SHA256.HashData(bytes);
            var facts = adapter.ProbeOptimization(new(Guid.NewGuid(), path), default);
            if (facts.UnsupportedReason is not null)
            {
                results.Add(new { File = Path.GetFileName(path), Status = "Unsupported", Reason = facts.UnsupportedReason });
                continue;
            }
            foreach (var policy in new[] { PngOptimizationPlan.AutoPolicy, PngOptimizationPlan.Policy,
                PngOptimizationPlan.BalancedPolicy, PngOptimizationPlan.SmallestPolicy })
            {
                var output = Path.Combine(root, $".context-suite-{facts.ItemId:N}.tmp");
                File.WriteAllBytes(output, []);
                var timer = Stopwatch.StartNew();
                var result = await adapter.OptimizeAsync(new(facts, output, policy), default);
                timer.Stop();
                var encoded = File.ReadAllBytes(output);
                using var original = new MagickImage(bytes);
                using var after = new MagickImage(encoded);
                var originalPixels = original.GetPixels().ToByteArray(PixelMapping.RGBA)!;
                var afterPixels = after.GetPixels().ToByteArray(PixelMapping.RGBA)!;
                var metrics = QuantizationMetrics.Measure(originalPixels, afterPixels, (int)facts.Width, (int)facts.Height);
                var expectedMetadata = result.PngFdECRemoved
                    ? QuantizationFixtures.Pack(QuantizationFixtures.Parse(bytes).Where(chunk => chunk.Type != "fdEC")) : bytes;
                var retained = PngPrecisionContracts.MetadataRetained(expectedMetadata, encoded, result.OptimizationMethod == PngPalette.Method);
                if (result.PngFdECRemoved != facts.PngFdECRemovalRequired)
                    throw new InvalidDataException("Corpus metadata removal differs from the probe.");
                if (!retained || encoded.Length > bytes.Length || !SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hash) ||
                    metrics.AlphaMaximumError != 0 || metrics.HiddenRgbChangedPixels != 0 ||
                    result.OptimizationMethod == "Lossless" && !original.GetPixels().ToArray()!.SequenceEqual(after.GetPixels().ToArray()!))
                    throw new InvalidDataException("Corpus preservation failed: " + Path.GetFileName(path));
                if (result.OptimizationMethod != "Lossless" &&
                    (metrics.CompositeRmse > (policy == PngOptimizationPlan.SmallestPolicy ? 4 : 2) ||
                     metrics.WorstTileRmse > (policy == PngOptimizationPlan.SmallestPolicy ? 12 : 6) ||
                     metrics.MinimumMeanSsim < (policy == PngOptimizationPlan.SmallestPolicy ? .95 : .98)))
                    throw new InvalidDataException("Corpus quality screen failed: " + Path.GetFileName(path));
                var final = Path.Combine(root, Path.GetFileNameWithoutExtension(path) + "-" + policy + ".png");
                File.Move(output, final);
                results.Add(new { File = Path.GetFileName(path), Status = "Validated", Policy = policy,
                    Method = result.OptimizationMethod, Reason = result.OptimizationReason, SourceBytes = bytes.Length,
                    OutputBytes = encoded.Length, Milliseconds = timer.Elapsed.TotalMilliseconds, Attempts = result.OptimizationAttempts,
                    MetadataRemoved = result.PngFdECRemoved ? "fdEC" : null,
                    Gradient = GradientRejection.Measure(originalPixels, afterPixels, (int)facts.Width, (int)facts.Height), Metrics = metrics });
            }
        }
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Production corpus: {results.Count} recorded outcomes. Any fdEC removal is recorded; originals unchanged. Evidence: {root}");
    }
}
