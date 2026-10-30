using System.Collections.Immutable;
using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Private.IO;
using ImageMagick;

namespace ContextSuite.Private.Images;

public sealed partial class ImageAdapter
{
    internal static ImageSourceFacts ProbeIcoInput(ImageProbe request, CancellationToken token)
    {
        var bytes = ReadSource(request.SourcePath, token);
        var frame = IcoInput.Read(bytes, token);
        return IcoFacts(request, bytes, frame);
    }

    internal static ImageWorkResult ConvertIcoInput(ImageWork request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request.Plan);
        request.Options.Validate();
        var source = request.Plan.Source;
        source.Validate();
        if (source.Format != ImageFormat.Ico || request.Options.Target != ImageFormat.Png)
            throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var bytes = ReadSource(source.Path, token);
        var frame = IcoInput.Read(bytes, token);
        var facts = IcoFacts(new(source.ItemId, source.Path), bytes, frame);
        ValidateSource(source, facts);
        var expected = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], request.Options, new("convert", new())).Items[0];
        if (!expected.CanExecute || expected.OutputWidth > frame.Width || expected.OutputHeight > frame.Height ||
            JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(request.Plan))
            throw new InvalidDataException(expected.BlockReason ?? "The icon plan changed before execution.");
        using var image = CreateIcoImage(frame, token);
        Apply(image, request.Options, expected.OutputWidth, expected.OutputHeight, expected.OutputDepth, null);
        ValidatePath(request.TemporaryPath);
        if (Path.GetFileName(request.TemporaryPath) != $".context-suite-{source.ItemId:N}.tmp" ||
            string.Equals(Path.GetFullPath(source.Path), Path.GetFullPath(request.TemporaryPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only the application's reserved temporary output may be written.");
        using (var output = new ReservedMediaOutput(request.TemporaryPath))
        {
            if (output.Length != 0) throw new InvalidDataException("Reserved output is not empty.");
            token.ThrowIfCancellationRequested();
            image.Write(output, MagickFormat.Png);
            output.Flush(true);
            if (output.Length > ImageContainer.MaximumFileBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        }
        try
        {
            var outputBytes = ReadSource(request.TemporaryPath, token);
            var header = ImageContainer.Read(outputBytes);
            using var decoded = Decode(outputBytes, header, token);
            if (header.Format != ImageFormat.Png || decoded.Width != expected.OutputWidth || decoded.Height != expected.OutputHeight ||
                header.Depth != expected.OutputDepth || decoded.Orientation is not (OrientationType.Undefined or OrientationType.TopLeft) ||
                !image.GetPixels().ToByteArray(PixelMapping.RGBA)!.SequenceEqual(decoded.GetPixels().ToByteArray(PixelMapping.RGBA)!))
                throw new ImageFailureException(ImageFailure.ValidationFailed);
            token.ThrowIfCancellationRequested();
            return new(new(source.ItemId, HexHash(outputBytes), true), decoded.Width, decoded.Height, header.Depth, outputBytes.LongLength);
        }
        catch (Exception error) when (error is InvalidDataException or MagickException)
        {
            throw new ImageFailureException(ImageFailure.ValidationFailed, error);
        }
    }

    internal static ImagePreview PreviewIcoInput(ImagePreviewRequest request, CancellationToken token)
    {
        request.Source.Validate();
        var bytes = ReadSource(request.Source.Path, token);
        var frame = IcoInput.Read(bytes, token);
        var facts = IcoFacts(new(request.Source.ItemId, request.Source.Path), bytes, frame);
        ValidateSource(request.Source, facts);
        using var image = CreateIcoImage(frame, token);
        if (request.Options is { } options)
        {
            options.Validate();
            if (options.Target != ImageFormat.Png) throw new ImageFailureException(ImageFailure.UnsupportedInput);
            var previewOptions = options with { MaximumDimension = Math.Min(options.MaximumDimension ?? 512, 512) };
            var planned = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], previewOptions, new("convert", new())).Items[0];
            if (!planned.CanExecute || planned.OutputWidth > frame.Width || planned.OutputHeight > frame.Height)
                throw new InvalidDataException(planned.BlockReason ?? "ICO input cannot be enlarged.");
            Apply(image, options, planned.OutputWidth, planned.OutputHeight, planned.OutputDepth, null);
        }
        return DisplayPixels(image, token);
    }

    private static ImageSourceFacts IcoFacts(ImageProbe request, byte[] bytes, IcoInput.Frame frame)
    {
        var transparent = false;
        for (var index = 3; index < frame.Bgra.Length; index += 4) transparent |= frame.Bgra[index] != 255;
        var facts = new ImageSourceFacts(request.ItemId, Path.GetFullPath(request.SourcePath), HexHash(bytes), bytes.LongLength,
            ImageFormat.Ico, frame.Width, frame.Height, frame.Depth, 1, transparent, false,
            frame.HadColorProfile ? "sRGB (embedded ICC converted)" : "sRGB (untagged/default interpretation)",
            frame.HadColorProfile ? ImmutableArray.Create("icc") : ImmutableArray<string>.Empty,
            HasOtherMetadata: frame.HadColorProfile);
        facts.Validate();
        return facts;
    }

    private static MagickImage CreateIcoImage(IcoInput.Frame frame, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var image = new MagickImage();
        try
        {
            image.Progress += (_, progress) => { if (token.IsCancellationRequested) progress.Cancel = true; };
            image.ReadPixels(frame.Bgra, new PixelReadSettings(frame.Width, frame.Height, StorageType.Char, PixelMapping.BGRA));
            image.ColorSpace = ColorSpace.sRGB;
            if (frame.HadColorProfile) image.SetProfile(ColorProfiles.SRGB);
            image.Orientation = OrientationType.TopLeft;
            image.Depth = 8;
            token.ThrowIfCancellationRequested();
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    internal static void ValidateIcoColorProfile(byte[] profile) => ValidateIcc(profile);
}
