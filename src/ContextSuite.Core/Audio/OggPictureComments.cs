using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace ContextSuite.Core.Audio;

// Xiph's picture comment carries the complete FLAC PICTURE payload as base64.
// This is exact transport, not image decoding or permission to omit other data.
public static class OggPictureComments
{
    public static bool IsPicture(string name) => name.Equals("METADATA_BLOCK_PICTURE", StringComparison.OrdinalIgnoreCase);

    public static ImmutableArray<FlacMetadataBlock> Read(VorbisCommentList source)
    {
        if (source.Comments.IsDefault || source.Comments.Length > VorbisComments.MaximumComments)
            throw new InvalidDataException("Expected a bounded Ogg comment inventory.");
        var pictures = ImmutableArray.CreateBuilder<FlacMetadataBlock>(); var textBytes = 0;
        foreach (var comment in source.Comments)
        {
            if (comment is null || comment.Name is null || comment.Value is null) throw new InvalidDataException("Invalid Ogg comment field.");
            if (!IsPicture(comment.Name)) continue;
            if (pictures.Count >= FlacDescriptiveMetadata.MaximumPictures || comment.Value.Length > VorbisComments.MaximumPictureTextBytes - textBytes - 23)
                throw new InvalidDataException("Ogg pictures exceed their count or byte budget.");
            textBytes += 23 + comment.Value.Length;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(comment.Value); }
            catch (FormatException error) { throw new InvalidDataException("Invalid base64 Ogg picture.", error); }
            if (Convert.ToBase64String(bytes) != comment.Value)
                throw new NotSupportedException("Noncanonical Ogg picture encoding needs a preservation policy.");
            pictures.Add(new(6, bytes.ToImmutableArray()));
        }
        var result = pictures.ToImmutable();
        RequireFits(result);
        return result;
    }

    public static byte[] AppendToPacket(ReadOnlySpan<byte> packet, ImmutableArray<FlacMetadataBlock> pictures)
    {
        var prefix = packet.StartsWith("OpusTags"u8) ? 8 : packet.StartsWith("\x03vorbis"u8) ? 7 :
            throw new InvalidDataException("Expected an encoded Ogg comment packet.");
        var textBytes = 0;
        var descriptions = VorbisComments.Read(packet[prefix..], ref textBytes, out var consumed);
        if (descriptions.Comments.Any(comment => IsPicture(comment.Name))) throw new InvalidDataException("Encoded candidate already contains artwork.");
        var fields = Fields(pictures);
        var countOffset = prefix + 4 + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(packet[prefix..]));
        if (descriptions.Comments.Length + fields.Length > VorbisComments.MaximumComments) throw new InvalidDataException("Too many Ogg comments after adding artwork.");
        var finalBytes = packet.Length + fields.Sum(field => 4L + field.Length);
        if (finalBytes > OggMetadata.MaximumPicturePacketBytes) throw new InvalidDataException("Artwork comment packet exceeds its byte budget.");
        using var result = new MemoryStream((int)finalBytes);
        result.Write(packet[..(prefix + consumed)]);
        Span<byte> size = stackalloc byte[4];
        foreach (var field in fields)
        {
            BinaryPrimitives.WriteInt32LittleEndian(size, field.Length);
            result.Write(size); result.Write(field);
        }
        result.Write(packet[(prefix + consumed)..]); // Keep framing/padding from the encoded candidate.
        var bytes = result.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(countOffset), descriptions.Comments.Length + fields.Length);
        return bytes;
    }

    public static void RequirePreserved(ImmutableArray<FlacMetadataBlock> expected, VorbisCommentList output)
    {
        var fields = Fields(expected);
        var actual = output.Comments.Where(comment => IsPicture(comment.Name)).ToArray();
        if (actual.Length != fields.Length || actual.Where((comment, index) =>
            comment.Value != Convert.ToBase64String(expected[index].Data.AsSpan())).Any())
            throw new InvalidDataException("Ogg artwork was omitted, changed or reordered.");
    }

    public static void RequireFits(ImmutableArray<FlacMetadataBlock> pictures)
    {
        if (pictures.IsDefault || pictures.Length > FlacDescriptiveMetadata.MaximumPictures || pictures.Any(picture => picture.Type != 6))
            throw new InvalidDataException("Expected a bounded FLAC picture inventory.");
        var bytes = pictures.Sum(picture => 23L + 4L * ((picture.Data.Length + 2L) / 3));
        if (bytes > VorbisComments.MaximumPictureTextBytes) throw new InvalidDataException("Artwork exceeds the Ogg picture budget.");
        if (FlacDescriptiveMetadata.ReadBlocks(pictures).Pictures.Any(picture => picture.IsLinked))
            throw new NotSupportedException("Linked artwork needs a location-preservation policy before conversion.");
    }
    private static byte[][] Fields(ImmutableArray<FlacMetadataBlock> pictures)
    {
        RequireFits(pictures);
        return pictures.Select(picture => Encoding.ASCII.GetBytes("METADATA_BLOCK_PICTURE=" + Convert.ToBase64String(picture.Data.AsSpan()))).ToArray();
    }
}
