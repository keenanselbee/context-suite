using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ContextSuite.Core.Images;
using ContextSuite.Private.IO;
using ImageMagick;

namespace ContextSuite.Private.Images;

public sealed partial class ImageAdapter
{
    private static ImageWorkResult ConvertAvif(ImageWork request, CancellationToken token)
    {
        request.Options.Validate(); request.Plan.Source.Validate();
        AvifCodec.EnsureAvailable();
        using var budget = new ProcessMemoryScope(3UL * 1024 * 1024 * 1024);
        var source = request.Plan.Source;
        using var lease = MediaFiles.OpenRead(source.Path);
        var bytes = ReadSource(source.Path, token);
        using var image = Load(bytes, token, out var container);
        var facts = Facts(new(source.ItemId, source.Path), bytes, container, image);
        ValidateSource(source, facts);
        var expected = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], request.Options, new("convert", new())).Items[0];
        if (!expected.CanExecute || JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(request.Plan))
            throw new InvalidDataException(expected.BlockReason ?? "The AVIF plan changed before execution.");
        var prepared = PrepareAvif(image, source, request.Options, expected.OutputWidth, expected.OutputHeight, token);
        var encoded = AvifCodec.Encode(expected.OutputWidth, expected.OutputHeight, prepared.Rgba, prepared.Icc, prepared.Exif, prepared.Xmp, token);
        _ = ValidateAvif(encoded, prepared, expected.OutputWidth, expected.OutputHeight, token);
        ValidatePath(request.TemporaryPath);
        if (Path.GetFileName(request.TemporaryPath) != $".context-suite-{source.ItemId:N}.tmp" ||
            string.Equals(Path.GetFullPath(source.Path), Path.GetFullPath(request.TemporaryPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only the application's reserved temporary output may be written.");
        using (var output = new ReservedMediaOutput(request.TemporaryPath))
        {
            for (var at = 0; at < encoded.Length; at += 65536)
            { token.ThrowIfCancellationRequested(); output.Write(encoded.AsSpan(at, Math.Min(65536, encoded.Length - at))); }
            output.Flush(true); output.Position = 0;
            if (output.Length != encoded.Length || System.Convert.ToHexString(SHA256.HashData(output)) != HexHash(encoded))
                throw new ImageFailureException(ImageFailure.ValidationFailed);
        }
        MediaFiles.CheckHandle(lease, source.Path); lease.Position = 0;
        if (System.Convert.ToHexString(SHA256.HashData(lease)) != source.Sha256) throw new ImageFailureException(ImageFailure.SourceChanged);
        token.ThrowIfCancellationRequested();
        return new(new(source.ItemId, HexHash(encoded), true), expected.OutputWidth, expected.OutputHeight, 8, encoded.LongLength);
    }

    private static ImagePreview PreviewAvif(ImagePreviewRequest request, CancellationToken token)
    {
        request.Source.Validate(); request.Options!.Validate();
        AvifCodec.EnsureAvailable();
        using var budget = new ProcessMemoryScope(3UL * 1024 * 1024 * 1024);
        using var lease = MediaFiles.OpenRead(request.Source.Path);
        var bytes = ReadSource(request.Source.Path, token);
        using var image = Load(bytes, token, out var container);
        var facts = Facts(new(request.Source.ItemId, request.Source.Path), bytes, container, image);
        ValidateSource(request.Source, facts);
        var options = request.Options with { MaximumDimension = Math.Min(request.Options.MaximumDimension ?? 512, 512) };
        var planned = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new())).Items[0];
        if (!planned.CanExecute) throw new InvalidDataException(planned.BlockReason);
        var prepared = PrepareAvif(image, facts, options, planned.OutputWidth, planned.OutputHeight, token);
        var encoded = AvifCodec.Encode(planned.OutputWidth, planned.OutputHeight, prepared.Rgba, prepared.Icc, prepared.Exif, prepared.Xmp, token);
        var decoded = ValidateAvif(encoded, prepared, planned.OutputWidth, planned.OutputHeight, token);
        using var simulated = new MagickImage();
        simulated.ReadPixels(decoded.Pixels, new PixelReadSettings(planned.OutputWidth, planned.OutputHeight, StorageType.Short, PixelMapping.RGBA));
        simulated.ColorSpace = ColorSpace.sRGB;
        MediaFiles.CheckHandle(lease, facts.Path);
        return DisplayPixels(simulated, token);
    }

    private sealed record AvifPixels(byte[] Rgba, byte[] Icc, byte[] Exif, byte[] Xmp);

    private static AvifPixels PrepareAvif(MagickImage image, ImageSourceFacts source, ImageConversionOptions options,
        uint width, uint height, CancellationToken token)
    {
        image.Progress += (_, progress) => { if (token.IsCancellationRequested) progress.Cancel = true; };
        token.ThrowIfCancellationRequested();
        if (image.GetColorProfile() is { } color)
            image.TransformColorSpace(color, ColorProfiles.SRGB, ColorTransformMode.HighRes);
        image.ColorSpace = ColorSpace.sRGB;
        image.SetProfile(ColorProfiles.SRGB);
        Apply(image, options, width, height, 8, source.Resolution);
        // The ICC transform changes these interpretation hints too. Keeping an
        // Adobe-RGB declaration next to sRGB pixels would create stale metadata.
        if (image.GetExifProfile() is { } exif)
        {
            exif.SetValue(ExifTag.ColorSpace, (ushort)1);
            image.SetProfile(exif);
        }
        if (image.GetXmpProfile() is { } xmp)
        {
            var xml = ParseXmp(xmp.ToByteArray());
            var colorName = XName.Get("ColorSpace", "http://ns.adobe.com/exif/1.0/");
            var profileName = XName.Get("ICCProfile", "http://ns.adobe.com/photoshop/1.0/");
            foreach (var element in xml.Descendants())
            {
                if (element.Name == colorName) element.Value = "1";
                else if (element.Name == profileName) element.Value = "sRGB IEC61966-2.1";
                foreach (var attribute in element.Attributes())
                {
                    if (attribute.Name == colorName) attribute.Value = "1";
                    else if (attribute.Name == profileName) attribute.Value = "sRGB IEC61966-2.1";
                }
            }
            image.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting))));
        }
        token.ThrowIfCancellationRequested();
        return new(image.GetPixels().ToByteArray(PixelMapping.RGBA)!, ColorProfiles.SRGB.ToByteArray(),
            image.GetExifProfile()?.ToByteArray() ?? [], image.GetXmpProfile()?.ToByteArray() ?? []);
    }

    private static HeifCodec.Decoded ValidateAvif(byte[] encoded, AvifPixels expected, uint width, uint height, CancellationToken token)
    {
        var decoded = HeifCodec.Decode(encoded, token);
        var info = decoded.Info;
        var exif = expected.Exif.AsSpan();
        if (exif.StartsWith("Exif\0\0"u8)) exif = exif[6..];
        if (info.Width != width || info.Height != height || info.Bits != 8 || info.ItemType != 0x61763031 || info.Codec != 0x61763031 ||
            (info.Flags & ~(1u | 16u)) != 0 || (info.Flags & 16) == 0 ||
            info.Primaries != 1 || info.Transfer != 13 || info.Matrix != 1 || info.FullRange != 1 ||
            info.EffectivePrimaries != 1 || info.EffectiveTransfer != 13 || info.EffectiveMatrix != 1 || info.EffectiveFullRange != 1 ||
            !decoded.Icc.SequenceEqual(expected.Icc) || !decoded.Exif.AsSpan().SequenceEqual(exif) || !decoded.Xmp.SequenceEqual(expected.Xmp))
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        // Apply the benchmark's fidelity bound to every encoded output, not
        // just the test corpus. Alpha remains exact; fully invisible RGB
        // samples do not establish visible error.
        double squaredError = 0; long samples = 0;
        for (var pixel = 0; pixel < expected.Rgba.Length / 4; pixel++)
        {
            if ((pixel & 16383) == 0) token.ThrowIfCancellationRequested();
            var output = decoded.Pixels.AsSpan(pixel * 8, 8);
            var alpha = expected.Rgba[pixel * 4 + 3];
            if (BinaryPrimitives.ReadUInt16LittleEndian(output[6..]) != alpha * 257)
                throw new ImageFailureException(ImageFailure.ValidationFailed);
            if (alpha == 0) continue;
            for (var channel = 0; channel < 3; channel++)
            {
                var actual = (BinaryPrimitives.ReadUInt16LittleEndian(output[(channel * 2)..]) + 128) / 257;
                var difference = actual - expected.Rgba[pixel * 4 + channel];
                squaredError += difference * difference; samples++;
            }
        }
        if (samples != 0 && squaredError / samples > 6 * 6) throw new ImageFailureException(ImageFailure.ValidationFailed);
        token.ThrowIfCancellationRequested();
        return decoded;
    }
}
