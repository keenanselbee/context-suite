using ContextSuite.Private.IO;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ImageMagick;

namespace ContextSuite.Private.Images;

public sealed partial class ImageAdapter
{
    public ImageAdapter(string scratchDirectory) { ImageEngineRuntime.Initialize(scratchDirectory); }

    public ImageSourceFacts Probe(ImageProbe request, CancellationToken cancellationToken)
    {
        var bytes = ReadSource(request.SourcePath, cancellationToken);
        if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 1, 0 })) return ProbeIcoInput(request, cancellationToken);
        if (bytes.AsSpan().StartsWith("DDS "u8)) return ProbeDds(request, bytes, cancellationToken);
        using var image = Load(bytes, cancellationToken, out var container);
        return Facts(request, bytes, container, image);
    }

    public ImageWorkResult Convert(ImageWork request, CancellationToken cancellationToken)
    {
        if (request.Plan.Source.Format == ImageFormat.Ico)
            return ConvertIcoInput(request, cancellationToken);
        if (request.Options.Target == ImageFormat.Avif)
            return ConvertAvif(request, cancellationToken);
        if (request.Options.Target == ImageFormat.Ico)
            return ConvertIcon(request, cancellationToken);
        if (request.Plan.Source.Format == ImageFormat.Dds || request.Options.Target == ImageFormat.Dds)
            return ConvertWithDds(request, cancellationToken);
        ArgumentNullException.ThrowIfNull(request.Plan);
        request.Options.Validate();
        var source = request.Plan.Source;
        source.Validate();
        var bytes = ReadSource(source.Path, cancellationToken);
        using var image = Load(bytes, cancellationToken, out var container);
        var facts = Facts(new(source.ItemId, source.Path), bytes, container, image);
        ValidateSource(source, facts);
        var expected = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], request.Options, new("convert", new())).Items[0];
        if (!expected.CanExecute || JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(request.Plan))
            throw new InvalidDataException(expected.BlockReason ?? "The image plan changed before execution.");
        Apply(image, request.Options, expected.OutputWidth, expected.OutputHeight, expected.OutputDepth, source.Resolution);
        var profiles = image.ProfileNames.ToDictionary(name => name, name => image.GetProfile(name)!.ToByteArray(), StringComparer.OrdinalIgnoreCase);
        ValidatePath(request.TemporaryPath);
        if (Path.GetFileName(request.TemporaryPath) != $".context-suite-{source.ItemId:N}.tmp" ||
            string.Equals(Path.GetFullPath(source.Path), Path.GetFullPath(request.TemporaryPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only the application's reserved temporary output may be written.");
        using (var output = new ReservedMediaOutput(request.TemporaryPath))
        {
            if (output.Length != 0) throw new InvalidDataException("Reserved output is not empty.");
            cancellationToken.ThrowIfCancellationRequested();
            image.Write(output, Format(request.Options.Target));
            ImageMetadataEnvelope.Restore(output, request.Options.Target, profiles);
            output.Flush(true);
            if (output.Length > ImageContainer.MaximumFileBytes) throw new InvalidDataException("Output exceeds the supported file-size limit.");
        }
        try
        {
            var outputBytes = ReadSource(request.TemporaryPath, cancellationToken);
            var outputContainer = ImageContainer.Read(outputBytes);
            using var reopened = Decode(outputBytes, outputContainer, cancellationToken);
            if (outputContainer.Format != request.Options.Target || reopened.Width != expected.OutputWidth || reopened.Height != expected.OutputHeight ||
                (request.Options.Target == ImageFormat.Png && outputContainer.Depth != expected.OutputDepth) ||
                reopened.Orientation is not (OrientationType.Undefined or OrientationType.TopLeft) ||
                (!request.Options.SupportsAlpha && !reopened.IsOpaque) ||
                (request.Options.SupportsAlpha && !image.IsOpaque && reopened.IsOpaque))
                throw new ImageFailureException(ImageFailure.ValidationFailed);
            foreach (var profile in profiles)
            {
                var actual = reopened.GetProfile(profile.Key)?.ToByteArray();
                if (actual is null || !actual.SequenceEqual(profile.Value))
                    throw new ImageFailureException(ImageFailure.ValidationFailed);
            }
            if (request.Options.Metadata == ImageMetadataMode.RemoveDescriptive &&
                reopened.ProfileNames.Any(name => name is not ("icc" or "icm")))
                throw new ImageFailureException(ImageFailure.ValidationFailed);
            cancellationToken.ThrowIfCancellationRequested();
            return new(new(source.ItemId, HexHash(outputBytes), true), reopened.Width, reopened.Height, outputContainer.Depth, outputBytes.LongLength);
        }
        catch (Exception error) when (error is InvalidDataException or MagickException)
        {
            throw new ImageFailureException(ImageFailure.ValidationFailed, error);
        }
    }

    public ImagePreview Preview(ImagePreviewRequest request, CancellationToken cancellationToken)
    {
        if (request.Source.Format == ImageFormat.Ico)
            return PreviewIcoInput(request, cancellationToken);
        if (request.Options?.Target == ImageFormat.Avif)
            return PreviewAvif(request, cancellationToken);
        if (request.Options?.Target == ImageFormat.Ico)
            return PreviewIcon(request, cancellationToken);
        if (request.Source.Format == ImageFormat.Dds || request.Options?.Target == ImageFormat.Dds)
            return PreviewWithDds(request, cancellationToken);
        request.Source.Validate();
        var bytes = ReadSource(request.Source.Path, cancellationToken);
        using var image = Load(bytes, cancellationToken, out var container);
        var facts = Facts(new(request.Source.ItemId, request.Source.Path), bytes, container, image);
        ValidateSource(request.Source, facts);
        if (facts.UnsupportedReason is not null) throw new InvalidDataException(facts.UnsupportedReason);
        if (request.Options is { } options)
        {
            options.Validate();
            var previewOptions = options with { MaximumDimension = Math.Min(options.MaximumDimension ?? 512, 512) };
            var planned = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], previewOptions, new("convert", new())).Items[0];
            if (!planned.CanExecute) throw new InvalidDataException(planned.BlockReason);
            Apply(image, options, planned.OutputWidth, planned.OutputHeight, planned.OutputDepth, request.Source.Resolution);
            var profiles = image.ProfileNames.ToDictionary(name => name, name => image.GetProfile(name)!.ToByteArray(), StringComparer.OrdinalIgnoreCase);
            using var encoded = new MemoryStream();
            image.Write(encoded, Format(options.Target));
            ImageMetadataEnvelope.Restore(encoded, options.Target, profiles);
            using var simulated = Decode(encoded.ToArray(), ImageContainer.Read(encoded.ToArray()), cancellationToken);
            return DisplayPixels(simulated, cancellationToken);
        }
        image.AutoOrient();
        image.Resize(new MagickGeometry(512, 512) { Greater = true });
        return DisplayPixels(image, cancellationToken);
    }

    private static ImagePreview DisplayPixels(MagickImage image, CancellationToken cancellationToken)
    {
        if (image.GetColorProfile() is { } color) image.TransformColorSpace(color, ColorProfiles.SRGB, ColorTransformMode.HighRes);
        else image.ColorSpace = ColorSpace.sRGB;
        image.Depth = 8;
        if (image.Width > 512 || image.Height > 512) throw new InvalidDataException("Preview exceeds its display limit.");
        cancellationToken.ThrowIfCancellationRequested();
        var pixels = image.GetPixels().ToByteArray(PixelMapping.BGRA) ?? throw new InvalidDataException("Preview has no pixels.");
        if (pixels.Length != checked((int)(image.Width * image.Height * 4))) throw new InvalidDataException("Invalid preview pixel buffer.");
        return new(image.Width, image.Height, pixels);
    }

    private static void Apply(MagickImage image, ImageConversionOptions options, uint width, uint height, uint depth, ImageResolution? resolution)
    {
        if (resolution is not null && image.Orientation is >= OrientationType.LeftTop and <= OrientationType.LeftBottom)
            resolution = resolution with { X = resolution.Y, Y = resolution.X };
        image.AutoOrient();
        image.Orientation = OrientationType.TopLeft;
        if (options.Metadata == ImageMetadataMode.Automatic)
        {
            // Keep normalized EXIF/XMP and color profiles where supported. Drop
            // unsupported application extras, not the color/orientation meaning.
            foreach (var name in image.ProfileNames.ToArray())
                if (name.ToLowerInvariant() is not ("icc" or "icm" or "exif" or "xmp")) image.RemoveProfile(name);
            foreach (var name in image.AttributeNames.ToArray()) image.RemoveAttribute(name);
            if (options.Target is ImageFormat.Bmp or ImageFormat.Tga) resolution = null;
        }
        if (image.Width != width || image.Height != height)
            image.Resize(new MagickGeometry(width, height) { IgnoreAspectRatio = true });
        if (!options.SupportsAlpha && !image.IsOpaque)
        {
            if (options.MatteRgb is not uint rgb) throw new InvalidDataException("A background color is required.");
            if (image.GetColorProfile() is { } gray && gray.ToByteArray().AsSpan(16, 4).SequenceEqual("GRAY"u8))
                image.TransformColorSpace(gray, ColorProfiles.SRGB);
            image.BackgroundColor = new MagickColor($"#{rgb:X6}");
            if (image.GetColorProfile() is { } profile)
            {
                // Build a controlled pixel without enabling ImageMagick's XC pseudo-file decoder.
                using var matte = image.Clone();
                foreach (var name in matte.ProfileNames.ToArray()) matte.RemoveProfile(name);
                matte.Resize(1, 1);
                matte.ColorSpace = ColorSpace.sRGB;
                matte.HasAlpha = false;
                matte.GetPixels().SetPixel(0, 0, new ushort[] { (ushort)(((rgb >> 16) & 255) * 257),
                    (ushort)(((rgb >> 8) & 255) * 257), (ushort)((rgb & 255) * 257) });
                matte.TransformColorSpace(ColorProfiles.SRGB, profile);
                image.BackgroundColor = matte.GetPixels().GetPixel(0, 0).ToColor();
            }
            image.Alpha(AlphaOption.Remove);
        }
        if (options.Metadata == ImageMetadataMode.RemoveDescriptive)
        {
            foreach (var name in image.ProfileNames.ToArray())
                if (name is not ("icc" or "icm")) image.RemoveProfile(name);
            foreach (var name in image.AttributeNames.ToArray()) image.RemoveAttribute(name);
            image.Density = new Density(0, 0, DensityUnit.Undefined);
        }
        else
        {
            var exif = image.GetExifProfile();
            if (resolution is not null)
            {
                exif ??= new ExifProfile();
                exif.SetValue(ExifTag.XResolution, new Rational(resolution.X, resolution.Divisor));
                exif.SetValue(ExifTag.YResolution, new Rational(resolution.Y, resolution.Divisor));
                exif.SetValue(ExifTag.ResolutionUnit, resolution.Unit);
                image.Density = new Density((double)resolution.X / resolution.Divisor, (double)resolution.Y / resolution.Divisor,
                    resolution.Unit switch { 2 => DensityUnit.PixelsPerInch, 3 => DensityUnit.PixelsPerCentimeter, _ => DensityUnit.Undefined });
            }
            if (exif is not null)
            {
                exif.SetValue(ExifTag.Orientation, (ushort)1);
                exif.SetValue(ExifTag.PixelXDimension, new Number(width));
                exif.SetValue(ExifTag.PixelYDimension, new Number(height));
                if (exif.GetValue(ExifTag.ImageWidth) is not null) exif.SetValue(ExifTag.ImageWidth, new Number(width));
                if (exif.GetValue(ExifTag.ImageLength) is not null) exif.SetValue(ExifTag.ImageLength, new Number(height));
                // Stale embedded thumbnail removal is a named planner consequence.
                exif.RemoveThumbnail();
                image.SetProfile(exif);
            }
            if (image.GetXmpProfile() is { } xmp)
            {
                var xml = ParseXmp(xmp.ToByteArray());
                var thumbnails = XName.Get("Thumbnails", "http://ns.adobe.com/xap/1.0/");
                xml.Descendants(thumbnails).Remove();
                xml.Descendants().Attributes(thumbnails).Remove();
                foreach (var element in xml.Descendants())
                {
                    foreach (var attribute in element.Attributes().ToArray())
                        if (NormalizedXmp(attribute.Name, width, height, resolution) is { } value) attribute.Value = value;
                    if (NormalizedXmp(element.Name, width, height, resolution) is { } content) element.Value = content;
                }
                image.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting))));
            }
        }
        image.Quality = options.Quality;
        image.Depth = depth;
        if (options.Target is ImageFormat.Png or ImageFormat.Jpeg && image.GetColorProfile() is { } outputProfile &&
            outputProfile.ToByteArray().AsSpan(16, 4).SequenceEqual("RGB "u8))
        {
            // Gray-looking RGB samples still require RGB storage when retaining
            // an RGB ICC profile. Encoder grayscale inference creates an invalid pair.
            image.ColorType = image.HasAlpha ? ColorType.TrueColorAlpha : ColorType.TrueColor;
            image.Settings.ColorType = image.ColorType;
            if (options.Target == ImageFormat.Png) image.Settings.SetDefine(MagickFormat.Png, "color-type", image.HasAlpha ? "6" : "2");
        }
        if (options.Target is ImageFormat.Bmp or ImageFormat.Tga)
        {
            if (image.GetColorProfile() is { } color) image.TransformColorSpace(color, ColorProfiles.SRGB);
            foreach (var name in image.ProfileNames.ToArray()) image.RemoveProfile(name);
            foreach (var name in image.AttributeNames.ToArray()) image.RemoveAttribute(name);
            image.ColorSpace = ColorSpace.sRGB;
            image.Density = new Density(0, 0, DensityUnit.Undefined);
            image.ColorType = options.Target == ImageFormat.Tga && !image.IsOpaque ? ColorType.TrueColorAlpha : ColorType.TrueColor;
            image.Settings.ColorType = image.ColorType;
            image.Settings.Compression = CompressionMethod.NoCompression;
        }
        image.Settings.SetDefine(MagickFormat.Png, "bit-depth", depth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "date,time");
        image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
        image.Settings.SetDefine(MagickFormat.WebP, "lossless", options.WebPLossless);
        image.Settings.SetDefine(MagickFormat.WebP, "exact", true);
    }

    private static string? NormalizedXmp(XName name, uint width, uint height, ImageResolution? resolution)
    {
        if (name.NamespaceName is not ("http://ns.adobe.com/tiff/1.0/" or "http://ns.adobe.com/exif/1.0/")) return null;
        return name.LocalName switch
        {
            "Orientation" => "1", "ImageWidth" or "PixelXDimension" => width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "ImageLength" or "PixelYDimension" => height.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "XResolution" when resolution is not null => FormattableString.Invariant($"{resolution.X}/{resolution.Divisor}"),
            "YResolution" when resolution is not null => FormattableString.Invariant($"{resolution.Y}/{resolution.Divisor}"),
            "ResolutionUnit" when resolution is not null => resolution.Unit.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => null
        };
    }

    private static ImageSourceFacts Facts(ImageProbe request, byte[] bytes, ImageContainer container, MagickImage image)
    {
        string? unsupported = null;
        XDocument? xmpDocument = null;
        var grayscaleProfile = false;
        if (image.ColorSpace is not (ColorSpace.sRGB or ColorSpace.RGB or ColorSpace.Gray or ColorSpace.LinearGray))
            unsupported = "This color model is not supported by the first image adapter.";
        if (image.GetColorProfile() is { } profile)
        {
            var data = profile.ToByteArray();
            ValidateIcc(data);
            grayscaleProfile = data.AsSpan(16, 4).SequenceEqual("GRAY"u8);
            if (grayscaleProfile && image.DetermineColorType() is not (ColorType.Bilevel or ColorType.Grayscale or ColorType.GrayscaleAlpha))
                throw new InvalidDataException("A grayscale profile cannot describe colored pixels.");
            using var validation = image.Clone();
            validation.Resize(1, 1);
            validation.TransformColorSpace(profile, ColorProfiles.SRGB);
        }
        else if (image.Gamma != 0 && Math.Abs(image.Gamma - 0.45455) > 0.001)
            unsupported = "This unprofiled gamma encoding requires a color transform not yet verified.";
        if (image.GetXmpProfile() is { } xmp)
        {
            var text = Encoding.UTF8.GetString(xmp.ToByteArray());
            if (text.Contains("hdrgm", StringComparison.OrdinalIgnoreCase) || text.Contains("gainmap", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("hdr-gain-map", StringComparison.OrdinalIgnoreCase) || text.Contains("urn:iso:std:iso:ts:21496", StringComparison.OrdinalIgnoreCase))
                unsupported = "HDR/gain-map metadata is not supported yet.";
            var xml = xmpDocument = ParseXmp(xmp.ToByteArray());
            var orientations = xml.Descendants().SelectMany(element =>
                element.Attributes().Where(attribute => attribute.Name == XName.Get("Orientation", "http://ns.adobe.com/tiff/1.0/"))
                    .Select(attribute => attribute.Value)
                    .Concat(element.Name == XName.Get("Orientation", "http://ns.adobe.com/tiff/1.0/") ? [element.Value] : []))
                .Select(value => ushort.TryParse(value, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var orientation) && orientation is >= 1 and <= 8
                        ? orientation : throw new InvalidDataException("Invalid XMP orientation."))
                .Distinct().ToArray();
            if (orientations.Length > 1 || (orientations.Length == 1 &&
                image.GetExifProfile()?.GetValue(ExifTag.Orientation)?.Value is { } exifOrientation && exifOrientation != orientations[0]))
                throw new InvalidDataException("Conflicting image orientations.");
            if (orientations.Length == 1) image.Orientation = (OrientationType)orientations[0];
        }
        // Validate structured EXIF rather than copying a broken opaque profile.
        _ = image.GetExifProfile()?.Values.Count();
        var facts = new ImageSourceFacts(request.ItemId, Path.GetFullPath(request.SourcePath), HexHash(bytes), bytes.LongLength,
            container.Format, image.Width, image.Height, container.Depth, (uint)image.Orientation, !image.IsOpaque, container.IsLossy,
            image.ColorSpace + (image.GetColorProfile() is null ? " (untagged/default interpretation)" : " (ICC profile)"),
            image.ProfileNames.Order(StringComparer.Ordinal).ToImmutableArray(), container.OtherMetadata, unsupported,
            ImageResolutionMetadata.Read(image.GetExifProfile(), container.Resolution, xmpDocument), grayscaleProfile);
        facts.Validate();
        return facts;
    }

    private static void ValidateIcc(byte[] profile)
    {
        if (profile.Length is < 132 or > 4 * 1024 * 1024 || BinaryPrimitives.ReadUInt32BigEndian(profile) != profile.Length ||
            !profile.AsSpan(36, 4).SequenceEqual("acsp"u8)) throw new InvalidDataException("The ICC profile is malformed.");
        var color = Encoding.ASCII.GetString(profile, 16, 4);
        if (color is not ("RGB " or "GRAY")) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var count = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(128));
        if (count > 4096 || 132L + count * 12L > profile.Length) throw new InvalidDataException("Invalid ICC tag table.");
        for (var index = 0; index < count; index++)
        {
            if (profile.AsSpan(132 + index * 12, 4).SequenceEqual("cicp"u8))
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(136 + index * 12));
            var length = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(140 + index * 12));
            if (offset > profile.Length || length > profile.Length - offset) throw new InvalidDataException("ICC tag exceeds the profile.");
        }
    }

    private static XDocument ParseXmp(byte[] bytes)
    {
        using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
        return XDocument.Load(reader);
    }

    private static MagickImage Decode(byte[] bytes, ImageContainer header, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var image = new MagickImage();
        try
        {
            var warned = false;
            // Retain an explicitly embedded sRGB ICC too; the decoder otherwise may replace
            // recognized profiles with an implicit sRGB declaration before our preservation check.
            if (header.Format == ImageFormat.Png) image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
            image.Warning += (_, _) => warned = true;
            image.Progress += (_, progress) => { if (cancellationToken.IsCancellationRequested) progress.Cancel = true; };
            image.Read(bytes, Format(header.Format));
            cancellationToken.ThrowIfCancellationRequested();
            if (warned || image.Width != header.Width || image.Height != header.Height)
                throw new InvalidDataException("The decoder reported damaged or inconsistent image contents.");
            if (header.ExpectedProfiles is not null)
                foreach (var profile in header.ExpectedProfiles)
                    if (image.GetProfile(profile.Key)?.ToByteArray() is not { } decoded || !decoded.SequenceEqual(profile.Value))
                        throw new InvalidDataException("The decoder did not retain the image's complete metadata/color profile.");
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    private static byte[] ReadSource(string path, CancellationToken cancellationToken)
    {
        ValidatePath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 12) throw new InvalidDataException("Image file is incomplete.");
        if (stream.Length > ImageContainer.MaximumFileBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        var bytes = new byte[(int)stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(bytes, offset, Math.Min(65536, bytes.Length - offset));
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        if (Path.GetExtension(path).Equals(".jfif", StringComparison.OrdinalIgnoreCase) &&
            !(bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff))
            throw new ImageFailureException(ImageFailure.UnsupportedInput);
        return bytes;
    }

    private static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal) || path.Length > 32700 ||
            path[Path.GetPathRoot(path)!.Length..].IndexOfAny([':', '\0', '\r', '\n']) >= 0)
            throw new InvalidDataException("Use an ordinary absolute file path.");
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
    }

    private static void ValidateSource(ImageSourceFacts planned, ImageSourceFacts actual)
    {
        if (JsonSerializer.Serialize(planned) != JsonSerializer.Serialize(actual))
            throw new ImageFailureException(ImageFailure.SourceChanged);
    }

    private static MagickFormat Format(ImageFormat format) => format switch
    {
        ImageFormat.Png => MagickFormat.Png, ImageFormat.Jpeg => MagickFormat.Jpeg, ImageFormat.WebP => MagickFormat.WebP,
        ImageFormat.Bmp => MagickFormat.Bmp3, ImageFormat.Tga => MagickFormat.Tga,
        _ => throw new InvalidDataException("Unsupported image format.")
    };

    private static string HexHash(byte[] bytes) => System.Convert.ToHexString(SHA256.HashData(bytes));
}
