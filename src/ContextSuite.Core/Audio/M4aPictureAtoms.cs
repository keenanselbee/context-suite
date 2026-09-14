using System.Buffers.Binary;
using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

// covr has ordered image data but no picture designation, description or
// geometry declarations. Admit only pictures representable without losing them.
public static class M4aPictureAtoms
{
    public static ImmutableArray<FlacMetadataBlock> ForOutput(ImmutableArray<FlacMetadataBlock> pictures)
    {
        if (pictures.IsDefault || pictures.Length > FlacDescriptiveMetadata.MaximumPictures ||
            pictures.Any(block => block.Type != 6 || block.Data.IsDefault) ||
            pictures.Sum(block => (long)block.Data.Length) > VorbisComments.MaximumPictureTextBytes)
            throw new NotSupportedException("M4A artwork exceeds its count or byte budget.");
        var facts = FlacDescriptiveMetadata.ReadBlocks(pictures).Pictures;
        var result = ImmutableArray.CreateBuilder<FlacMetadataBlock>(); var transportBytes = 0;
        for (var index = 0; index < pictures.Length; index++)
        {
            var picture = facts[index];
            if (picture.Type != 0 || picture.Description.Length != 0)
                throw new NotSupportedException("M4A cannot keep picture labels or descriptions. Choose FLAC or Ogg to preserve them.");
            if (picture.MediaType is not ("image/png" or "image/jpeg"))
                throw new NotSupportedException("This picture format needs an M4A preservation handler.");
            var image = pictures[index].Data.AsSpan()[^picture.DataBytes..];
            AudioPictureGeometry.Require(picture, image);
            var data = new byte[8 + image.Length];
            BinaryPrimitives.WriteUInt32BigEndian(data, picture.MediaType == "image/jpeg" ? 13u : 14u);
            image.CopyTo(data.AsSpan(8));
            result.Add(M4aPictures.Read(data, ref transportBytes));
        }
        return result.ToImmutable();
    }

    public static byte[] CreateCover(ImmutableArray<FlacMetadataBlock> pictures)
    {
        var normalized = ForOutput(pictures);
        var facts = FlacDescriptiveMetadata.ReadBlocks(normalized).Pictures;
        if (facts.IsEmpty) return [];
        var output = new byte[checked(8 + facts.Sum(picture => 16 + picture.DataBytes))];
        BinaryPrimitives.WriteUInt32BigEndian(output, (uint)output.Length); "covr"u8.CopyTo(output.AsSpan(4));
        var offset = 8;
        for (var index = 0; index < facts.Length; index++)
        {
            var picture = facts[index];
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(offset), (uint)(16 + picture.DataBytes));
            "data"u8.CopyTo(output.AsSpan(offset + 4));
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(offset + 8), picture.MediaType == "image/jpeg" ? 13u : 14u);
            normalized[index].Data.AsSpan()[^picture.DataBytes..].CopyTo(output.AsSpan(offset + 16));
            offset += 16 + picture.DataBytes;
        }
        return output;
    }
}
