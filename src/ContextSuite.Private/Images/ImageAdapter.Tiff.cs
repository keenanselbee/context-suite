using ContextSuite.Core.Images;
using ContextSuite.Core.Tiff;
using ContextSuite.Private.Tiff;
using ImageMagick;

namespace ContextSuite.Private.Images;

public sealed partial class ImageAdapter
{
    internal static MagickImage PrepareTiffPixels(TiffDirectory directory, byte[] bgra, CancellationToken token)
    {
        var facts = directory.Facts;
        facts.Validate();
        if (facts.UnsupportedReason is not null) throw new NotSupportedException(facts.UnsupportedReason);
        if (bgra.Length != checked((int)(facts.Width * facts.Height * 4))) throw new InvalidDataException("Invalid decoded TIFF pixel count.");
        var image = new MagickImage();
        image.Progress += (_, progress) => { if (token.IsCancellationRequested) progress.Cancel = true; };
        try
        {
            token.ThrowIfCancellationRequested();
            var grayProfile = directory.IccProfile is { } icc && icc.AsSpan(16, 4).SequenceEqual("GRAY"u8);
            if (directory.IccProfile is { } profile)
            {
                ValidateIcc(profile);
                if (grayProfile && directory.Photometric > 1)
                    throw new NotSupportedException("A gray TIFF profile cannot describe RGB samples.");
            }
            if (grayProfile)
            {
                var gray = new byte[facts.Width * facts.Height];
                for (var i = 0; i < gray.Length; i++)
                {
                    if (i % 65536 == 0) token.ThrowIfCancellationRequested();
                    if (bgra[i * 4] != bgra[i * 4 + 1] || bgra[i * 4] != bgra[i * 4 + 2] || bgra[i * 4 + 3] != 255)
                        throw new InvalidDataException("TIFF grayscale decoder returned non-gray pixels.");
                    gray[i] = bgra[i * 4];
                }
                image.ReadPixels(gray, new PixelReadSettings(facts.Width, facts.Height, StorageType.Char, "I"));
                image.ColorSpace = ColorSpace.Gray;
            }
            else
            {
                image.ReadPixels(bgra, new PixelReadSettings(facts.Width, facts.Height, StorageType.Char, PixelMapping.BGRA));
                image.ColorSpace = ColorSpace.sRGB;
            }
            if (directory.IccProfile is { } color)
                image.TransformColorSpace(new ColorProfile(color), ColorProfiles.SRGB, ColorTransformMode.HighRes);
            image.SetProfile(ColorProfiles.SRGB);
            image.Orientation = (OrientationType)facts.Orientation;
            image.AutoOrient(); image.Orientation = OrientationType.TopLeft;
            image.Depth = 8;
            // Materialize the agreed eight-bit sRGB samples once. A Q16 ICC
            // transform can retain fractional sample precision despite Depth=8;
            // explicit samples keep PNG, JPEG and PDF quantization consistent.
            var normalized = image.GetPixels().ToByteArray(PixelMapping.RGBA)!;
            image.ReadPixels(normalized, new PixelReadSettings(image.Width, image.Height, StorageType.Char, PixelMapping.RGBA));
            image.ColorSpace = ColorSpace.sRGB;
            image.SetProfile(ColorProfiles.SRGB);
            image.Orientation = OrientationType.TopLeft; image.Depth = 8;
            token.ThrowIfCancellationRequested();
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    internal static byte[] EncodeTiffPixels(MagickImage image, TiffPageFacts page, TiffConversionOptions options, CancellationToken token)
    {
        var target = options.Target == TiffTarget.Png ? ImageFormat.Png : ImageFormat.Jpeg;
        var resolution = page.Orientation is >= 5 and <= 8 && page.Resolution is { } original
            ? original with { X = original.Y, Y = original.X } : page.Resolution;
        var conversion = new ImageConversionOptions(target, Quality: 90, MatteRgb: options.MatteRgb, Metadata: ImageMetadataMode.Automatic);
        Apply(image, conversion, page.OutputWidth, page.OutputHeight, 8, resolution);
        var profiles = image.ProfileNames.ToDictionary(name => name, name => image.GetProfile(name)!.ToByteArray(), StringComparer.OrdinalIgnoreCase);
        using var output = new MemoryStream();
        token.ThrowIfCancellationRequested();
        image.Write(output, Format(target));
        ImageMetadataEnvelope.Restore(output, target, profiles);
        if (output.Length > TiffWork.MaximumOutputBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        var bytes = output.ToArray();
        var header = ImageContainer.Read(bytes);
        using var decoded = Decode(bytes, header, token);
        if (header.Format != target || decoded.Width != page.OutputWidth || decoded.Height != page.OutputHeight ||
            decoded.Orientation is not (OrientationType.Undefined or OrientationType.TopLeft) ||
            options.Target == TiffTarget.Jpeg && !decoded.IsOpaque || options.Target == TiffTarget.Png && !image.IsOpaque && decoded.IsOpaque)
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        foreach (var profile in profiles)
            if (decoded.GetProfile(profile.Key)?.ToByteArray() is not { } actual || !actual.SequenceEqual(profile.Value))
                throw new ImageFailureException(ImageFailure.ValidationFailed);
        if (options.Target == TiffTarget.Png &&
            !image.GetPixels().ToByteArray(PixelMapping.RGBA)!.SequenceEqual(decoded.GetPixels().ToByteArray(PixelMapping.RGBA)!))
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        token.ThrowIfCancellationRequested();
        return bytes;
    }

    internal static ImagePreview PreviewTiffPixels(MagickImage image, uint matte, CancellationToken token)
    {
        image.Resize(new MagickGeometry(512, 512) { Greater = true });
        Apply(image, new(ImageFormat.Jpeg, MatteRgb: matte, Metadata: ImageMetadataMode.RemoveDescriptive), image.Width, image.Height, 8, null);
        using var output = new MemoryStream(); image.Write(output, MagickFormat.Jpeg);
        var bytes = output.ToArray();
        using var decoded = Decode(bytes, ImageContainer.Read(bytes), token);
        return DisplayPixels(decoded, token);
    }
}
