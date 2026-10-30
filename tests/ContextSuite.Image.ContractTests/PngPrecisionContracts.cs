using System.Security.Cryptography;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class PngPrecisionContracts
{
    public static async Task RunAsync(string parent, Action<bool, string> check)
    {
        var root = Path.Combine(parent, "png-precision");
        Directory.CreateDirectory(root);
        var adapter = new ImageAdapter(Path.Combine(root, "scratch"));
        foreach (var bits in new[] { 7, 6 })
        {
            var samples = new byte[256 * 4];
            for (var value = 0; value < 256; value++)
            {
                samples[value * 4] = samples[value * 4 + 1] = samples[value * 4 + 2] = (byte)value;
                samples[value * 4 + 3] = 255;
            }
            var rounded = PngPrecision.Reduce(samples, bits, default);
            check(rounded[0] == 0 && rounded[^2] == 255 && rounded.Where((_, i) => i % 4 == 0).Distinct().Count() == 1 << bits &&
                rounded.SequenceEqual(PngPrecision.Reduce(rounded, bits, default)) && PngPrecision.MeetsQuality(samples, rounded, 256, 1, bits, default),
                $"RGB{bits}: exhaustive sample levels, endpoints, idempotence and quality bounds");
            var policy = bits == 7 ? PngOptimizationPlan.BalancedPolicy : PngOptimizationPlan.SmallestPolicy;
            var successes = 0;
            var unchanged = 0;
            foreach (var fixture in QuantizationFixtures.Create())
            {
                var path = Path.Combine(root, fixture.Name + ".png");
                File.WriteAllBytes(path, fixture.Png);
                var facts = adapter.ProbeOptimization(new(Guid.NewGuid(), path), default);
                var container = new PngLosslessContainer(fixture.Png);
                if (facts.PngLossyBlockReason is not null)
                {
                    check(fixture.Name == "indexed-icons", "PNG precision blocks indexed representation before execution");
                    continue;
                }
                var expected = QuantizationCandidate.ReducePrecision(fixture.Pixels, bits);
                check(PngPrecision.Reduce(fixture.Pixels, bits, default).SequenceEqual(expected), $"RGB{bits} {fixture.Name}: independent rounding oracle");
                var raw = container.EncodePrecision(expected, default);
                check(new PngLosslessContainer(raw).ReadRgba(default).SequenceEqual(expected), $"RGB{bits} {fixture.Name}: sample reconstruction");
                var temporary = Path.Combine(root, $".context-suite-{facts.ItemId:N}.tmp");
                File.WriteAllBytes(temporary, []);
                var result = await adapter.OptimizeAsync(new(facts, temporary, policy), default);
                var output = File.ReadAllBytes(temporary);
                check(result.Validation.MatchesPlan && result.Validation.Sha256 == Convert.ToHexString(SHA256.HashData(output)) &&
                    result.EngineIdentity!.EndsWith(policy), $"RGB{bits} {fixture.Name}: versioned policy and validated digest");
                check(File.ReadAllBytes(path).SequenceEqual(fixture.Png), $"RGB{bits} {fixture.Name}: source unchanged");
                var isPalette = result.OptimizationMethod == PngPalette.Method;
                check(MetadataRetained(fixture.Png, output, isPalette), $"Fixed preset {bits} {fixture.Name}: metadata retained, only approved palette structure changes");
                check(result.OptimizationAttempts is >= 1 and <= 2 && (!isPalette || bits == 6),
                    $"Fixed preset {bits} {fixture.Name}: bounded attempts and palette only for Smallest");
                if (bits == 6)
                    check(result.OptimizationAttempts == (!isPalette && PngPalette.CanUse(fixture.Pixels, fixture.Width, fixture.Height, default) ? 2 : 1),
                        "Smallest: palette or direct RGB7, at most one RGB7 fallback, no recursive lossless: " + fixture.Name);
                using var decoded = new MagickImage(output);
                var pixels = decoded.GetPixels().ToByteArray(PixelMapping.RGBA)!;
                var metrics = QuantizationMetrics.Measure(fixture.Pixels, pixels, fixture.Width, fixture.Height);
                check(metrics.AlphaMaximumError == 0 && metrics.HiddenRgbChangedPixels == 0, $"RGB{bits} {fixture.Name}: alpha and hidden RGB exact");
                var screen = metrics.CompositeRmse <= (bits == 7 ? 2 : 4) && metrics.WorstTileRmse <= (bits == 7 ? 6 : 12) &&
                    metrics.MinimumMeanSsim >= (bits == 7 ? .98 : .95);
                check(PngPrecision.MeetsQuality(fixture.Pixels, pixels, fixture.Width, fixture.Height, bits, default, isPalette) && screen &&
                    !GradientRejection.Measure(fixture.Pixels, pixels, fixture.Width, fixture.Height).Rejected,
                    $"RGB{bits} {fixture.Name}: independent quality screen");
                if (output.SequenceEqual(fixture.Png)) unchanged++;
                else
                {
                    successes++;
                    check(output.Length < fixture.Png.Length &&
                        (pixels.SequenceEqual(fixture.Pixels) || isPalette || pixels.SequenceEqual(QuantizationCandidate.ReducePrecision(fixture.Pixels, 7))),
                        $"Fixed preset {bits} {fixture.Name}: smaller accepted result from only the prescribed recipes");
                }
            }
            check(successes > 0, $"RGB{bits}: useful outputs exercised ({successes}, {unchanged} unchanged)");
        }
        foreach (var fixture in QuantizationFixtures.Create())
        {
            var path = Path.Combine(root, "auto-" + fixture.Name + ".png");
            File.WriteAllBytes(path, fixture.Png);
            var facts = adapter.ProbeOptimization(new(Guid.NewGuid(), path), default);
            var container = new PngLosslessContainer(fixture.Png);
            var lossless = container.RestoreAndValidate(await PngOptimizer.EncodeAsync(fixture.Png, default), default);
            var expected = lossless.Length < fixture.Png.Length ? lossless : fixture.Png;
            var lossy = false;
            if (facts.PngLossyBlockReason is null && fixture.Width * fixture.Height >= 4096)
            {
                var reduced = QuantizationCandidate.ReducePrecision(fixture.Pixels, 7);
                var raw = container.EncodePrecision(reduced, default);
                var candidate = new PngLosslessContainer(raw).RestoreAndValidate(await PngOptimizer.EncodeAsync(raw, default), default);
                var metrics = QuantizationMetrics.Measure(fixture.Pixels, reduced, fixture.Width, fixture.Height);
                if ((long)candidate.Length * 100 <= (long)fixture.Png.Length * 95 && metrics.CompositeRmse <= 2 &&
                    metrics.WorstTileRmse <= 6 && metrics.MinimumMeanSsim >= .98 &&
                    !GradientRejection.Measure(fixture.Pixels, reduced, fixture.Width, fixture.Height).Rejected)
                { expected = candidate; lossy = true; }
            }
            var temporary = Path.Combine(root, $".context-suite-{facts.ItemId:N}.tmp");
            File.WriteAllBytes(temporary, []);
            var result = await adapter.OptimizeAsync(new(facts, temporary, PngOptimizationPlan.AutoPolicy), default);
            check(File.ReadAllBytes(temporary).SequenceEqual(expected) && result.OptimizationMethod == (lossy ? "RGB7 (lossy)" : "Lossless"),
                "Auto: independent quality/5% benefit oracle and actual method: " + fixture.Name);
            check(File.ReadAllBytes(path).SequenceEqual(fixture.Png), "Auto: original unchanged: " + fixture.Name);
            check(result.OptimizationAttempts == (facts.PngLossyBlockReason is null && fixture.Width * fixture.Height >= 4096 && !lossy ? 2 : 1),
                "Auto: exact attempt count, no lossless-baseline tournament: " + fixture.Name);
        }
        byte[] original = [80, 90, 100, 255, 10, 20, 30, 0];
        foreach (var index in new[] { 0, 3, 4 })
        {
            var altered = original.ToArray(); altered[index] += 5;
            check(!PngPrecision.MeetsQuality(original, altered, 2, 1, 6, default), "PNG quality rejects RGB/alpha/hidden RGB corruption: " + index);
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { PngPrecision.Reduce(original, 7, cancelled.Token); throw new InvalidOperationException("Cancelled precision transform succeeded"); }
        catch (OperationCanceledException) { check(true, "PNG precision transformation honors cancellation"); }
    }

    internal static bool MetadataRetained(byte[] source, byte[] output, bool palette)
    {
        var before = QuantizationFixtures.Parse(source).ToArray();
        var after = QuantizationFixtures.Parse(output).ToArray();
        if (palette)
        {
            var expectedHeader = before.Single(c => c.Type == "IHDR").Data.ToArray();
            expectedHeader[9] = 3;
            if (!after.Single(c => c.Type == "IHDR").Data.SequenceEqual(expectedHeader) ||
                after.Count(c => c.Type == "PLTE") != 1 || after.Count(c => c.Type == "tRNS") > 1) return false;
        }
        var originalChunks = before.Where(c => c.Type != "IDAT" && (!palette || c.Type != "IHDR")).ToArray();
        var outputChunks = after.Where(c => c.Type != "IDAT" && (!palette || c.Type is not ("IHDR" or "PLTE" or "tRNS"))).ToArray();
        return originalChunks.Length == outputChunks.Length && originalChunks.Zip(outputChunks).All(pair =>
            pair.First.Type == pair.Second.Type && pair.First.Data.SequenceEqual(pair.Second.Data));
    }
}
