using ContextSuite.Core.Images;
using ContextSuite.Private.IO;
using ImageMagick;
using ImageMagick.ImageOptimizers;

namespace ContextSuite.Private.Images;

internal sealed record ImageOptimizationBytesResult(byte[] Bytes, string Method, string Reason, int Attempts);

public sealed partial class ImageAdapter
{
    public ImageSourceFacts ProbeLosslessOptimization(ImageProbe request, CancellationToken token)
    {
        var bytes = ReadSource(request.SourcePath, token);
        AdmitLosslessImage(bytes, ImageContainer.Read(bytes).Format);
        using var image = Load(bytes, token, out var container);
        var facts = Facts(request, bytes, container, image);
        if (facts.UnsupportedReason is not null) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        return facts;
    }

    public ImageWorkResult OptimizeLosslessImage(LosslessImageOptimizationWork request, CancellationToken token)
    {
        request.Validate();
        var source = request.Source;
        var original = ReadSource(source.Path, token);
        AdmitLosslessImage(original, ImageContainer.Read(original).Format);
        using var image = Load(original, token, out var container);
        var facts = Facts(new(source.ItemId, source.Path), original, container, image);
        ValidateSource(source, facts);
        if (facts.UnsupportedReason is not null) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var optimized = OptimizeImageBytesCore(original, image, container, facts,
            LosslessImageOptimizationPlan.PresetFor(container.Format, request.Policy), token);
        var output = optimized.Bytes;
        ValidatePath(request.TemporaryPath);
        if (Path.GetFileName(request.TemporaryPath) != $".context-suite-{source.ItemId:N}.tmp" ||
            string.Equals(Path.GetFullPath(source.Path), Path.GetFullPath(request.TemporaryPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only the application's reserved temporary output may be written.");
        token.ThrowIfCancellationRequested();
        using (var file = new ReservedMediaOutput(request.TemporaryPath))
        {
            file.Write(output);
            file.Flush(true);
        }
        var written = ReadSource(request.TemporaryPath, token);
        if (!written.SequenceEqual(output)) throw new ImageFailureException(ImageFailure.ValidationFailed);
        return new(new(source.ItemId, HexHash(output), true), source.Width, source.Height, source.BitDepth, output.LongLength,
            "Curated Magick.NET / " + request.Policy, optimized.Method, optimized.Reason, optimized.Attempts);
    }

    internal static ImageOptimizationBytesResult OptimizeImageBytes(byte[] original, ImageOptimizationPreset preset, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        AdmitLosslessImage(original, ImageContainer.Read(original).Format);
        using var image = Load(original, token, out var container);
        var facts = Facts(new(Guid.NewGuid(), Path.Combine(Path.GetTempPath(), "context-suite-image-bytes")), original, container, image);
        if (facts.UnsupportedReason is not null) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        return OptimizeImageBytesCore(original, image, container, facts, preset, token);
    }

    private static ImageOptimizationBytesResult OptimizeImageBytesCore(byte[] original, MagickImage image, ImageContainer container,
        ImageSourceFacts facts, ImageOptimizationPreset preset, CancellationToken token)
    {
        if (!Enum.IsDefined(preset)) throw new InvalidDataException("Unknown image optimization preset.");
        if (container.Format == ImageFormat.Tga)
        {
            if (preset is not (ImageOptimizationPreset.Auto or ImageOptimizationPreset.Lossless))
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            using var tgaDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            tgaDeadline.CancelAfter(TimeSpan.FromSeconds(110));
            try
            {
                var candidate = TgaOptimization.Optimize(original, tgaDeadline.Token);
                using var reopened = Decode(candidate, ImageContainer.Read(candidate), tgaDeadline.Token);
                if (!ExactOptimizationSamples(image, reopened, tgaDeadline.Token))
                    throw new ImageFailureException(ImageFailure.ValidationFailed);
                tgaDeadline.Token.ThrowIfCancellationRequested();
                return new(candidate, "Lossless", "Exact TGA samples, image ID, origin, and supported footer retained.", 1);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new ImageFailureException(ImageFailure.TimedOut); }
            catch (MagickException) when (tgaDeadline.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
                throw new ImageFailureException(ImageFailure.TimedOut);
            }
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(110));
        var cancellation = deadline.Token;
        byte[]? rgba = null;
        var selected = preset;
        var recipe = selected;
        var attempts = 0;
        var refusal = "";
        var primaryRefusal = "";
        try
        {
            if (preset == ImageOptimizationPreset.Auto)
            {
                selected = ImageOptimizationPreset.Lossless;
                if (!facts.IsLossy && !facts.HasTransparency)
                {
                    rgba = image.GetPixels().ToByteArray(PixelMapping.RGBA) ?? throw new ImageFailureException(ImageFailure.ValidationFailed);
                    if (ImageOptimizationQuality.CanChooseBalanced(rgba, (int)facts.Width, (int)facts.Height, cancellation))
                        selected = ImageOptimizationPreset.Balanced;
                }
            }
            if (selected != ImageOptimizationPreset.Lossless)
                rgba ??= image.GetPixels().ToByteArray(PixelMapping.RGBA) ?? throw new ImageFailureException(ImageFailure.ValidationFailed);
            recipe = selected;
            for (var index = 0; index < (selected == ImageOptimizationPreset.Lossless ? 1 : 2); index++)
            {
                cancellation.ThrowIfCancellationRequested();
                attempts++;
                var canEncode = recipe == ImageOptimizationPreset.Lossless ||
                    (!facts.HasTransparency && (container.Format != ImageFormat.Jpeg || new JpegLosslessEnvelope(original).CanReencode));
                if (canEncode)
                {
                    var candidate = EncodeOptimization(original, image, container.Format, recipe, cancellation);
                    var candidateContainer = ImageContainer.Read(candidate);
                    using var reopened = Decode(candidate, candidateContainer, cancellation);
                    var actual = Facts(new(facts.ItemId, facts.Path), candidate, candidateContainer, reopened);
                    if (actual.UnsupportedReason is not null || actual.Format != facts.Format || actual.Width != facts.Width ||
                        actual.Height != facts.Height || actual.BitDepth != facts.BitDepth || actual.Orientation != facts.Orientation ||
                        actual.HasTransparency != facts.HasTransparency || actual.Resolution != facts.Resolution ||
                        actual.ColorDescription != facts.ColorDescription || actual.HasGrayscaleProfile != facts.HasGrayscaleProfile)
                        throw new ImageFailureException(ImageFailure.ValidationFailed);
                    var quality = recipe == ImageOptimizationPreset.Lossless
                        ? ExactOptimizationSamples(image, reopened, cancellation)
                        : ImageOptimizationQuality.Accept(rgba!, reopened.GetPixels().ToByteArray(PixelMapping.RGBA)!,
                            (int)facts.Width, (int)facts.Height, recipe, cancellation);
                    if (recipe == ImageOptimizationPreset.Lossless && !quality)
                        throw new ImageFailureException(ImageFailure.ValidationFailed);
                    if (quality && candidate.Length < original.Length)
                        return new(candidate, Method(recipe), Prefix() +
                            (recipe == ImageOptimizationPreset.Lossless ? "Exact decoded samples and original metadata validated." :
                                "Fixed local, edge, and gradient quality limits and original metadata validated."), attempts);
                    refusal = quality ? "no smaller result" : "quality limits rejected the candidate";
                }
                else refusal = "the representation requires exact preservation";
                if (index == 0) primaryRefusal = refusal;
                // One predetermined fallback, always re-encoding the original.
                // Smallest never proceeds from Balanced to Lossless.
                recipe = selected == ImageOptimizationPreset.Smallest ? ImageOptimizationPreset.Balanced : ImageOptimizationPreset.Lossless;
            }
            var retainedReason = attempts > 1
                ? $"Original retained. Selected {selected}: {primaryRefusal}; fallback to {recipe}: {refusal}. {attempts} bounded attempt(s)."
                : $"Original retained: {refusal}. Selected {selected}; {attempts} bounded attempt(s).";
            return new(original, "Lossless", retainedReason, attempts);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new ImageFailureException(ImageFailure.TimedOut); }
        catch (MagickException) when (cancellation.IsCancellationRequested)
        {
            token.ThrowIfCancellationRequested();
            throw new ImageFailureException(ImageFailure.TimedOut);
        }

        string Prefix() => (preset == ImageOptimizationPreset.Auto ? $"Auto selected {selected}. " : "") +
            (attempts > 1 ? $"{selected} fallback to {recipe}: {refusal}. " : "");
    }

    private static string Method(ImageOptimizationPreset preset) => preset switch
    {
        ImageOptimizationPreset.Balanced => "Balanced (lossy)",
        ImageOptimizationPreset.Smallest => "Smallest (lossy)",
        _ => "Lossless"
    };

    private static bool ExactOptimizationSamples(MagickImage original, MagickImage candidate, CancellationToken token)
    {
        using var before = original.GetPixels();
        using var after = candidate.GetPixels();
        // Compare full Q16 samples in bounded strips, including hidden RGB,
        // without allocating two complete additional decoded images.
        for (var row = 0; row < original.Height; row += 16)
        {
            token.ThrowIfCancellationRequested();
            var rows = Math.Min(16u, original.Height - (uint)row);
            if (!before.GetArea(0, row, original.Width, rows).SequenceEqual(after.GetArea(0, row, candidate.Width, rows))) return false;
        }
        return true;
    }

    private static byte[] EncodeOptimization(byte[] original, MagickImage image, ImageFormat format,
        ImageOptimizationPreset recipe, CancellationToken cancellation)
    {
        if (format == ImageFormat.Jpeg && recipe == ImageOptimizationPreset.Lossless)
        {
            var envelope = new JpegLosslessEnvelope(original);
            using var working = new LosslessEncodingStream(cancellation);
            working.Write(envelope.WorkingBytes());
            working.Position = 0;
            new JpegOptimizer { OptimalCompression = false, Progressive = false }.LosslessCompress(working);
            cancellation.ThrowIfCancellationRequested();
            return envelope.Restore(working.ToArray());
        }
        using var encodedImage = image.Clone();
        foreach (var name in encodedImage.ProfileNames.ToArray()) encodedImage.RemoveProfile(name);
        foreach (var name in encodedImage.AttributeNames.ToArray()) encodedImage.RemoveAttribute(name);
        encodedImage.Quality = recipe == ImageOptimizationPreset.Lossless ? 100u : recipe == ImageOptimizationPreset.Balanced ? 92u : 84u;
        if (format == ImageFormat.Jpeg)
        {
            encodedImage.ColorType = new JpegLosslessEnvelope(original).IsGrayscale ? ColorType.Grayscale : ColorType.TrueColor;
            encodedImage.Settings.ColorType = encodedImage.ColorType;
            encodedImage.Settings.Interlace = Interlace.NoInterlace;
            encodedImage.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "1x1");
            encodedImage.Settings.SetDefine(MagickFormat.Jpeg, "optimize-coding", true);
        }
        else
        {
            encodedImage.Settings.SetDefine(MagickFormat.WebP, "lossless", recipe == ImageOptimizationPreset.Lossless);
            encodedImage.Settings.SetDefine(MagickFormat.WebP, "exact", true);
            encodedImage.Settings.SetDefine(MagickFormat.WebP, "alpha-quality", "100");
            encodedImage.Settings.SetDefine(MagickFormat.WebP, "method", "6");
        }
        encodedImage.Progress += (_, progress) => { if (cancellation.IsCancellationRequested) progress.Cancel = true; };
        using var encoded = new LosslessEncodingStream(cancellation);
        encodedImage.Write(encoded, format == ImageFormat.Jpeg ? MagickFormat.Jpeg : MagickFormat.WebP);
        cancellation.ThrowIfCancellationRequested();
        return format == ImageFormat.Jpeg ? new JpegLosslessEnvelope(original).Restore(encoded.ToArray(), false) :
            new WebPLosslessEnvelope(original).Restore(encoded.ToArray());
    }

    private static void AdmitLosslessImage(byte[] bytes, ImageFormat format)
    {
        if (format == ImageFormat.Jpeg) _ = new JpegLosslessEnvelope(bytes);
        else if (format == ImageFormat.WebP)
        {
            var header = ImageContainer.Read(bytes);
            if (header.Width > 16383 || header.Height > 16383) throw new ImageFailureException(ImageFailure.UnsupportedInput);
            _ = new WebPLosslessEnvelope(bytes);
        }
        else if (format != ImageFormat.Tga) throw new ImageFailureException(ImageFailure.UnsupportedInput);
    }

    private sealed class LosslessEncodingStream(CancellationToken token) : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            token.ThrowIfCancellationRequested();
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            token.ThrowIfCancellationRequested();
            return base.Read(buffer);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            CheckWrite(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            CheckWrite(buffer.Length);
            base.Write(buffer);
        }

        public override void SetLength(long value)
        {
            token.ThrowIfCancellationRequested();
            if (value > ImageContainer.MaximumFileBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
            base.SetLength(value);
        }

        private void CheckWrite(int count)
        {
            token.ThrowIfCancellationRequested();
            if (count < 0 || Position > ImageContainer.MaximumFileBytes - count)
                throw new ImageFailureException(ImageFailure.ResourceLimit);
        }
    }
}
