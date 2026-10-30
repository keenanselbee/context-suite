using ContextSuite.Private.IO;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Images;

public sealed partial class ImageAdapter
{
    public ImageSourceFacts ProbeOptimization(ImageProbe request, CancellationToken cancellationToken)
    {
        var facts = Probe(request, cancellationToken);
        if (facts.Format != ImageFormat.Png) return facts;
        try
        {
            var bytes = PngLosslessContainer.RemoveFdEC(ReadSource(request.SourcePath, cancellationToken), cancellationToken, out var removed);
            var container = new PngLosslessContainer(bytes);
            _ = container.SampleDigest(cancellationToken);
            facts = facts with { PngLossyBlockReason = container.LossyBlockReason, PngFdECRemovalRequired = removed };
        }
        catch (ImageFailureException error) when (error.Failure == ImageFailure.UnsupportedInput)
        { return facts with { UnsupportedReason = error.InnerException is NotSupportedException feature ? feature.Message : error.Message }; }
        return facts;
    }

    public async Task<ImageWorkResult> OptimizeAsync(PngOptimizationWork request, CancellationToken cancellationToken)
    {
        if (request.Policy is not (PngOptimizationPlan.Policy or PngOptimizationPlan.BalancedPolicy or PngOptimizationPlan.AutoPolicy or PngOptimizationPlan.SmallestPolicy))
            throw new InvalidDataException("Unknown PNG optimization policy.");
        request.Source.Validate();
        var source = request.Source;
        var original = ReadSource(source.Path, cancellationToken);
        var bytes = PngLosslessContainer.RemoveFdEC(original, cancellationToken, out var removed);
        var container = new PngLosslessContainer(bytes);
        using var decoded = Decode(bytes, ImageContainer.Read(bytes), cancellationToken);
        var actual = Facts(new(source.ItemId, source.Path), original, ImageContainer.Read(original), decoded) with
            { PngLossyBlockReason = container.LossyBlockReason, PngFdECRemovalRequired = removed };
        ValidateSource(source, actual);
        if (source.UnsupportedReason is not null) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        // One immutable recipe and at most one safety fallback; never encode a tournament.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(110));
        var token = deadline.Token;
        var canReduce = container.LossyBlockReason is null && source.BitDepth == 8 && (ulong)source.Width * source.Height <= 16_000_000;
        var originalPixels = canReduce ? container.ReadRgba(token) : null;
        var primary = "Lossless";
        string? fallback = null;
        if (canReduce && request.Policy != PngOptimizationPlan.Policy)
        {
            if (request.Policy == PngOptimizationPlan.SmallestPolicy)
            {
                primary = PngPalette.CanUse(originalPixels!, (int)source.Width, (int)source.Height, token) ? PngPalette.Method : "RGB7 (lossy)";
                if (primary == PngPalette.Method) fallback = "RGB7 (lossy)";
            }
            else if (request.Policy != PngOptimizationPlan.AutoPolicy || (ulong)source.Width * source.Height >= 4096)
            { primary = "RGB7 (lossy)"; fallback = "Lossless"; }
        }
        var output = bytes;
        var method = "Lossless";
        var reason = removed ? "Only fdEC metadata removal reduced the file; the image samples are unchanged." :
            "Original retained: the bounded recipe did not produce a smaller acceptable result.";
        var attempts = 0;
        try
        {
            foreach (var recipe in fallback is null ? new[] { primary } : new[] { primary, fallback })
            {
                token.ThrowIfCancellationRequested();
                attempts++;
                byte[] candidate;
                if (recipe == "Lossless")
                {
                    candidate = container.RestoreAndValidate(await PngOptimizer.EncodeAsync(bytes, token), token);
                }
                else
                {
                    var palette = recipe == PngPalette.Method;
                    var reduced = palette
                        ? await PngPalette.GenerateAsync(originalPixels!, (int)source.Width, (int)source.Height, token)
                        : PngPrecision.Reduce(originalPixels!, 7, token);
                    if (!PngPrecision.MeetsQuality(originalPixels!, reduced, (int)source.Width, (int)source.Height, palette ? 6 : 7, token, palette) ||
                        PngGradientQuality.Measure(originalPixels!, reduced, (int)source.Width, (int)source.Height, token).Rejected)
                        continue;
                    var raw = palette ? container.EncodePalette(reduced, token) : container.EncodePrecision(reduced, token);
                    var expected = new PngLosslessContainer(raw);
                    candidate = expected.RestoreAndValidate(await PngOptimizer.EncodeAsync(raw, token), token);
                    using var checkedImage = Decode(candidate, ImageContainer.Read(candidate), token);
                    if (!checkedImage.GetPixels().ToByteArray(ImageMagick.PixelMapping.RGBA)!.SequenceEqual(reduced))
                        throw new ImageFailureException(ImageFailure.ValidationFailed);
                }
                var worthwhile = recipe == "Lossless" || request.Policy != PngOptimizationPlan.AutoPolicy ||
                    (long)candidate.Length * 100 <= (long)bytes.Length * 95;
                if (candidate.Length < bytes.Length && worthwhile)
                {
                    output = candidate; method = recipe;
                    reason = attempts == 1 ? "Fixed recipe passed quality and size checks." : "The single safety fallback passed quality and size checks.";
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ImageFailureException(ImageFailure.TimedOut); }
        using var reopened = Decode(output, ImageContainer.Read(output), cancellationToken);
        ValidatePath(request.TemporaryPath);
        if (Path.GetFileName(request.TemporaryPath) != $".context-suite-{source.ItemId:N}.tmp" ||
            string.Equals(Path.GetFullPath(source.Path), Path.GetFullPath(request.TemporaryPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only the application's reserved temporary output may be written.");
        cancellationToken.ThrowIfCancellationRequested();
        using (var file = new ReservedMediaOutput(request.TemporaryPath))
        {
            file.Write(output);
            file.Flush(true);
        }
        return new(new(source.ItemId, HexHash(output), true), source.Width, source.Height, source.BitDepth, output.LongLength,
            "oxipng 10.1.0 / " + request.Policy, method, reason, attempts, removed);
    }
}
