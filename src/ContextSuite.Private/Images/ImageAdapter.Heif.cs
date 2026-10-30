using System.Xml.Linq;
using ContextSuite.Core.Images;
using ImageMagick;

namespace ContextSuite.Private.Images;

public sealed partial class ImageAdapter
{
    private static MagickImage Load(byte[] bytes, CancellationToken cancellationToken, out ImageContainer container)
    {
        if (!bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8))
        {
            container = ImageContainer.Read(bytes);
            return Decode(bytes, container, cancellationToken);
        }
        var decoded = HeifCodec.Decode(bytes, cancellationToken);
        var info = decoded.Info;
        if (info.ItemType is not (0x68766331 or 0x61763031 or 0x67726964))
            throw new NotSupportedException("This HEIF image composition has not been verified yet.");
        var format = info.Codec switch
        {
            0x68766331 => ImageFormat.Heic,
            0x61763031 => ImageFormat.Avif,
            _ => throw new NotSupportedException("This HEIF image composition has not been verified yet.")
        };
        if ((info.Flags & (2 | 4 | 128 | 256 | 512)) != 0 || info.Transfer is 16 or 18 || info.EffectiveTransfer is 16 or 18)
            throw new NotSupportedException("This image uses HDR, multiple images or an auxiliary representation that cannot yet be preserved.");
        if (info.EffectiveMatrix is not (0 or 1 or 5 or 6 or 9) || info.EffectiveFullRange > 1)
            throw new NotSupportedException("This image's color interpretation has not been verified yet.");
        var image = new MagickImage();
        try
        {
            image.ReadPixels(decoded.Pixels, new PixelReadSettings(info.Width, info.Height, StorageType.Short, PixelMapping.RGBA));
            image.ColorSpace = ColorSpace.sRGB;
            image.Depth = info.Bits;
            if (decoded.Icc.Length > 0)
            {
                ValidateIcc(decoded.Icc);
                image.SetProfile(new ColorProfile(decoded.Icc));
            }
            else if (info.EffectivePrimaries == 1 && info.EffectiveTransfer == 13) image.SetProfile(ColorProfiles.SRGB);
            else
            {
                var profileName = (info.EffectivePrimaries, info.EffectiveTransfer) switch
                {
                    (1, 1) => "Bt709", (12, 13) => "DisplayP3",
                    _ => throw new NotSupportedException("This SDR color profile has not been verified yet.")
                };
                using var profileStream = typeof(ImageAdapter).Assembly.GetManifestResourceStream("ContextSuite.Private.Images.Profiles." + profileName + ".icc")
                    ?? throw new InvalidDataException("Required HEIF color profile is missing.");
                using var buffer = new MemoryStream();
                profileStream.CopyTo(buffer);
                image.SetProfile(new ColorProfile(buffer.ToArray()));
            }
            if (decoded.Exif.Length > 0)
            {
                // The native ABI returns the raw TIFF payload. Magick's EXIF
                // reader requires its six-byte identifier to parse those tags.
                if (!(decoded.Exif.AsSpan().StartsWith("II*\0"u8) || decoded.Exif.AsSpan().StartsWith("MM\0*"u8)))
                    throw new InvalidDataException("The HEIF EXIF payload is not a classic TIFF directory.");
                var exif = new ExifProfile("Exif\0\0"u8.ToArray().Concat(decoded.Exif).ToArray());
                if (exif.GetValue(ExifTag.Orientation)?.Value is ushort orientation && orientation != 1)
                    throw new NotSupportedException("Conflicting HEIF and EXIF orientation requires review.");
                image.SetProfile(exif);
            }
            if (decoded.Xmp.Length > 0)
            {
                var xmp = ParseXmp(decoded.Xmp);
                var orientationName = XName.Get("Orientation", "http://ns.adobe.com/tiff/1.0/");
                if (xmp.Descendants().SelectMany(e => e.Attributes().Where(a => a.Name == orientationName).Select(a => a.Value)
                    .Concat(e.Name == orientationName ? [e.Value] : [])).Any(value => value != "1"))
                    throw new NotSupportedException("Conflicting HEIF and XMP orientation requires review.");
                image.SetProfile(new XmpProfile(decoded.Xmp));
            }
            // Container geometry is already applied. Do not orient the decoded samples twice.
            image.Orientation = OrientationType.TopLeft;
            var profiles = image.ProfileNames.ToDictionary(name => name, name => image.GetProfile(name)!.ToByteArray(), StringComparer.OrdinalIgnoreCase);
            // A color assumption must preserve the original even when the
            // user's standing preference permits overwrite.
            container = new(format, info.Width, info.Height, info.Bits, true, (info.Flags & (8 | 64 | 2048)) != 0, profiles);
            return image;
        }
        catch { image.Dispose(); throw; }
    }
}
