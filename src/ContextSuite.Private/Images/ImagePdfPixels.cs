using System.Buffers.Binary;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ImageMagick;

namespace ContextSuite.Private.Images;

public sealed record ImagePdfPixels(ImagePdfPage Page, byte[] Color, byte[]? Alpha, byte[] IccProfile);

public sealed partial class ImageAdapter
{
    // Full-resolution samples, not a display preview. Preserve the source ICC
    // and 16-bit precision; orientation is applied once before page placement.
    public ImagePdfPixels ReadPdfPixels(ImagePdfPage page, CancellationToken token)
    {
        page.Validate();
        var bytes = ReadSource(page.Source.Path, token);
        var container = ImageContainer.Read(bytes);
        using var image = Decode(bytes, container, token);
        var facts = Facts(new(page.Source.ItemId, page.Source.Path), bytes, container, image);
        ValidateSource(page.Source, facts);
        var profile = image.GetColorProfile()?.ToByteArray();
        if (profile is null && image.ColorSpace is ColorSpace.RGB or ColorSpace.LinearGray)
            throw new NotSupportedException("Unprofiled linear samples need a separately verified PDF color interpretation.");
        profile ??= ColorProfiles.SRGB.ToByteArray();
        image.AutoOrient();
        if (image.Width != page.Width || image.Height != page.Height)
            throw new InvalidDataException("Oriented PDF image dimensions changed.");
        using var pixels = image.GetPixels();
        var color = Export(page.ColorComponents == 1 ? "R" : "RGB");
        var alpha = facts.HasTransparency ? Export("A") : null;
        if (color.Length != checked((int)((long)page.Width * page.Height * page.ColorComponents * page.BitsPerComponent / 8)) ||
            alpha is not null && alpha.Length != checked((int)((long)page.Width * page.Height * page.BitsPerComponent / 8)))
            throw new InvalidDataException("PDF image sample lengths do not match their plan.");
        return new(page, color, alpha, profile);

        byte[] Export(string mapping)
        {
            token.ThrowIfCancellationRequested();
            if (page.BitsPerComponent == 8) return pixels.ToByteArray(mapping) ?? throw new InvalidDataException("No PDF image samples.");
            var values = pixels.ToShortArray(mapping) ?? throw new InvalidDataException("No 16-bit PDF image samples.");
            var result = new byte[checked(values.Length * 2)];
            for (var i = 0; i < values.Length; i++)
            {
                if (i % 65536 == 0) token.ThrowIfCancellationRequested();
                BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(i * 2), values[i]);
            }
            return result;
        }
    }
}
