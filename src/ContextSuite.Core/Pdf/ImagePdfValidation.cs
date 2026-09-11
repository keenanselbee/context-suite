using System.Buffers.Binary;
using System.Collections.Immutable;

namespace ContextSuite.Core.Pdf;

// Expected digests are computed from the original decoded image samples, before
// PDF encoding. They are never inferred from a supposedly successful PDF parse.
public sealed record ImagePdfSamples(int PageIndex, string ColorSha256, string? AlphaSha256, string ProfileSha256, int ProfileBytes)
{
    public void Validate(ImagePdfPage page, int index)
    {
        if (PageIndex != index || !Hash(ColorSha256) || !Hash(ProfileSha256) ||
            (AlphaSha256 is not null) != page.Source.HasTransparency || AlphaSha256 is not null && !Hash(AlphaSha256) ||
            ProfileBytes is < 132 or > 4 * 1024 * 1024)
            throw new InvalidDataException("Incomplete image PDF sample expectations.");
    }
    private static bool Hash(string value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}

public static class ImagePdfValidation
{
    public const int HeaderBytes = 16;
    public const int PageBytes = 160;
    public const int MaximumReplyBytes = HeaderBytes + ImagePdfPlan.MaximumPages * PageBytes;
    private const uint Magic = 0x31564943;

    public static void Verify(ReadOnlySpan<byte> bytes, ConfirmedImagePdf confirmed, ImmutableArray<ImagePdfSamples> samples)
    {
        var plan = confirmed.Plan;
        _ = plan.Confirm(true);
        if (samples.IsDefault || samples.Length != plan.Pages.Length)
            throw new InvalidDataException("Image PDF sample count differs from its plan.");
        if (bytes.Length < HeaderBytes || bytes.Length > MaximumReplyBytes ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != 1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) != plan.Pages.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]) != plan.Pages.Length * PageBytes ||
            bytes.Length != HeaderBytes + plan.Pages.Length * PageBytes)
            throw new InvalidDataException("Incomplete or unsupported image PDF validation reply.");
        for (var index = 0; index < plan.Pages.Length; index++)
        {
            var page = plan.Pages[index]; var expected = samples[index];
            if (expected is null) throw new InvalidDataException("Missing image PDF sample expectations.");
            expected.Validate(page, index);
            var row = bytes.Slice(HeaderBytes + index * PageBytes, PageBytes);
            var colorBytes = (ulong)page.Width * page.Height * (uint)page.ColorComponents * (uint)page.BitsPerComponent / 8;
            var alphaBytes = page.Source.HasTransparency ? (ulong)page.Width * page.Height * (uint)page.BitsPerComponent / 8 : 0;
            var width = BinaryPrimitives.ReadDoubleLittleEndian(row[32..]); var height = BinaryPrimitives.ReadDoubleLittleEndian(row[40..]);
            if (BinaryPrimitives.ReadUInt32LittleEndian(row) != index || BinaryPrimitives.ReadUInt32LittleEndian(row[4..]) != page.Width ||
                BinaryPrimitives.ReadUInt32LittleEndian(row[8..]) != page.Height || BinaryPrimitives.ReadUInt32LittleEndian(row[12..]) != page.BitsPerComponent ||
                BinaryPrimitives.ReadUInt32LittleEndian(row[16..]) != page.ColorComponents ||
                BinaryPrimitives.ReadUInt32LittleEndian(row[20..]) != (page.Source.HasTransparency ? 1u : 0u) ||
                BinaryPrimitives.ReadUInt32LittleEndian(row[24..]) != expected.ProfileBytes || BinaryPrimitives.ReadUInt32LittleEndian(row[28..]) != 0 ||
                !double.IsFinite(width) || !double.IsFinite(height) || Math.Abs(width - page.WidthPoints) > 1e-9 || Math.Abs(height - page.HeightPoints) > 1e-9 ||
                BinaryPrimitives.ReadUInt64LittleEndian(row[48..]) != colorBytes || BinaryPrimitives.ReadUInt64LittleEndian(row[56..]) != alphaBytes ||
                !row.Slice(64, 32).SequenceEqual(Convert.FromHexString(expected.ColorSha256)) ||
                !row.Slice(96, 32).SequenceEqual(expected.AlphaSha256 is null ? new byte[32] : Convert.FromHexString(expected.AlphaSha256)) ||
                !row.Slice(128, 32).SequenceEqual(Convert.FromHexString(expected.ProfileSha256)))
                throw new InvalidDataException("The PDF does not match the reviewed image pages, samples, transparency or color profiles.");
        }
    }
}
