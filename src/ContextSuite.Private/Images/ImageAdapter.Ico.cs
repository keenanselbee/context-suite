using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Private.IO;
using ImageMagick;

namespace ContextSuite.Private.Images;

public sealed partial class ImageAdapter
{
    private static ImageWorkResult ConvertIcon(ImageWork request, CancellationToken token)
    {
        request.Options.Validate();
        var source = request.Plan.Source;
        source.Validate();
        var bytes = ReadSource(source.Path, token);
        using var image = Load(bytes, token, out var container);
        var facts = Facts(new(source.ItemId, source.Path), bytes, container, image);
        ValidateSource(source, facts);
        var expected = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], request.Options, new("convert", new())).Items[0];
        if (!expected.CanExecute || JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(request.Plan))
            throw new InvalidDataException(expected.BlockReason ?? "The icon plan changed before execution.");
        PrepareIcon(image, token);
        var frames = IconFrameLayout.Create(image.Width, image.Height).Select(layout => CreateIconFrame(image, layout, token)).ToArray();
        ValidatePath(request.TemporaryPath);
        if (Path.GetFileName(request.TemporaryPath) != $".context-suite-{source.ItemId:N}.tmp" ||
            string.Equals(Path.GetFullPath(source.Path), Path.GetFullPath(request.TemporaryPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only the application's reserved temporary output may be written.");
        using (var output = new ReservedMediaOutput(request.TemporaryPath))
        {
            if (output.Length != 0) throw new InvalidDataException("Reserved output is not empty.");
            IcoOutput.Write(output, frames, token);
            output.Flush(true);
        }
        var encoded = ReadSource(request.TemporaryPath, token);
        IcoOutput.Validate(encoded, frames, token);
        return new(new(source.ItemId, HexHash(encoded), true), expected.OutputWidth, expected.OutputHeight, 8, encoded.LongLength);
    }

    private static ImagePreview PreviewIcon(ImagePreviewRequest request, CancellationToken token)
    {
        request.Source.Validate();
        var options = request.Options!;
        options.Validate();
        var bytes = ReadSource(request.Source.Path, token);
        using var image = Load(bytes, token, out var container);
        var facts = Facts(new(request.Source.ItemId, request.Source.Path), bytes, container, image);
        ValidateSource(request.Source, facts);
        var planned = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new())).Items[0];
        if (!planned.CanExecute) throw new InvalidDataException(planned.BlockReason);
        PrepareIcon(image, token);
        var frame = CreateIconFrame(image, IconFrameLayout.Create(image.Width, image.Height)[^1], token);
        // The ICO stores these exact straight-alpha bytes without quantization or
        // compression; preview and output share the same bounded frame renderer.
        return new(frame.Size, frame.Size, frame.Bgra);
    }

    private static void PrepareIcon(MagickImage image, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        image.AutoOrient();
        image.Orientation = OrientationType.TopLeft;
        if (image.GetColorProfile() is { } color)
            image.TransformColorSpace(color, ColorProfiles.SRGB, ColorTransformMode.HighRes);
        image.ColorSpace = ColorSpace.sRGB;
        foreach (var name in image.ProfileNames.ToArray()) image.RemoveProfile(name);
        foreach (var name in image.AttributeNames.ToArray()) image.RemoveAttribute(name);
        image.Density = new Density(0, 0, DensityUnit.Undefined);
        image.Depth = 8;
        image.Alpha(AlphaOption.Set);
        image.ColorType = ColorType.TrueColorAlpha;
        token.ThrowIfCancellationRequested();
    }

    private static IconPixels CreateIconFrame(MagickImage source, IconFrameLayout layout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var image = source.Clone();
        image.Progress += (_, progress) => { if (token.IsCancellationRequested) progress.Cancel = true; };
        if (image.Width != layout.Width || image.Height != layout.Height)
            image.Resize(new MagickGeometry(layout.Width, layout.Height) { IgnoreAspectRatio = true });
        var pixels = image.GetPixels().ToByteArray(PixelMapping.BGRA)
            ?? throw new InvalidDataException("Icon has no pixels.");
        if (pixels.Length != checked((int)(layout.Width * layout.Height * 4)))
            throw new InvalidDataException("Invalid icon pixel buffer.");
        var padded = new byte[checked((int)(layout.Size * layout.Size * 4))];
        for (var y = 0; y < layout.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            pixels.AsSpan(checked((int)(y * layout.Width * 4)), checked((int)(layout.Width * 4)))
                .CopyTo(padded.AsSpan(checked((int)(((y + layout.Top) * layout.Size + layout.Left) * 4))));
        }
        return new(layout.Size, padded);
    }
}
