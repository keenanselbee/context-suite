using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using ContextSuite.Core.Analysis;

namespace ContextSuite.Core.Audio;

// ID3v2.4 APIC transport. Image bytes stay intact; redundant FLAC geometry must
// agree with the image header before using a container without those fields.
public static class Mp3PictureFrames
{
    public static ImmutableArray<FlacMetadataBlock> ForOutput(ImmutableArray<FlacMetadataBlock> pictures)
    {
        if (pictures.IsDefault || pictures.Length > FlacDescriptiveMetadata.MaximumPictures ||
            pictures.Any(block => block.Type != 6 || block.Data.IsDefault) ||
            pictures.Sum(block => (long)block.Data.Length) > Mp3Metadata.MaximumTagBytes)
            throw new NotSupportedException("MP3 artwork exceeds its count or byte budget.");
        var facts = FlacDescriptiveMetadata.ReadBlocks(pictures).Pictures;
        if (facts.Select(picture => picture.Description).Distinct(StringComparer.Ordinal).Count() != pictures.Length)
            throw new NotSupportedException("MP3 needs a different description for each cover. Choose FLAC or Ogg to keep these pictures.");
        var result = ImmutableArray.CreateBuilder<FlacMetadataBlock>();
        for (var index = 0; index < pictures.Length; index++)
        {
            var picture = facts[index]; var bytes = pictures[index].Data.AsSpan();
            var image = bytes[^picture.DataBytes..];
            if (picture.MediaType is not ("image/png" or "image/jpeg") || picture.Description.Length > 64 ||
                picture.Description.Contains('\0') || picture.Description.Any(character => character < 32 && character is not ('\t' or '\r' or '\n')))
                throw new NotSupportedException("This picture format or description needs an MP3 preservation handler.");
            RequireGeometry(picture, image);
            var normalized = bytes.ToArray();
            // Four geometry declarations precede the encoded image length.
            normalized.AsSpan(bytes.Length - picture.DataBytes - 20, 16).Clear();
            result.Add(new(6, normalized.ToImmutableArray()));
        }
        return result.ToImmutable();
    }

    public static byte[] AppendToTag(ReadOnlySpan<byte> tag, ImmutableArray<FlacMetadataBlock> pictures)
    {
        if (tag.Length < 10 || !tag[..6].SequenceEqual(new byte[] { 73, 68, 51, 4, 0, 0 }) ||
            tag.Length - 10 > Mp3Metadata.MaximumTagBytes || ReadSize(tag[6..10]) != tag.Length - 10)
            throw new InvalidDataException("Expected the bounded encoder's plain ID3v2.4 tag.");
        var offset = 10; var count = 0;
        while (offset < tag.Length && tag[offset] != 0)
        {
            if (++count > Mp3Metadata.MaximumTagFrames || tag.Length - offset < 10)
                throw new InvalidDataException("Encoded ID3 frame extent or count is invalid.");
            var frame = tag.Slice(offset, 10); var size = ReadSize(frame[4..8]);
            foreach (var value in frame[..4])
                if (value is not (>= 65 and <= 90) and not (>= 48 and <= 57))
                    throw new InvalidDataException("Encoded ID3 frame identifier is invalid.");
            if (frame[..4].SequenceEqual("APIC"u8) || frame[8] != 0 || frame[9] != 0 || size <= 0 || size > tag.Length - offset - 10)
                throw new InvalidDataException("Encoded ID3 tag already has artwork or unsupported frame flags/extents.");
            offset += 10 + size;
        }
        if (tag[offset..].IndexOfAnyExcept((byte)0) >= 0) throw new InvalidDataException("Encoded ID3 padding is not zero.");
        var normalized = ForOutput(pictures);
        var descriptions = FlacDescriptiveMetadata.ReadBlocks(normalized).Pictures;
        if (count + pictures.Length > Mp3Metadata.MaximumTagFrames) throw new InvalidDataException("Too many ID3 frames after adding artwork.");
        using var output = new MemoryStream(); output.Write(tag[..offset]);
        for (var index = 0; index < pictures.Length; index++)
        {
            var picture = descriptions[index];
            var mime = Encoding.ASCII.GetBytes(picture.MediaType); var description = Encoding.UTF8.GetBytes(picture.Description);
            var length = 4 + mime.Length + description.Length + picture.DataBytes;
            if (output.Length - 10 + 10 + length + tag.Length - offset > Mp3Metadata.MaximumTagBytes)
                throw new NotSupportedException("Pictures and audio tags exceed the MP3 metadata budget.");
            output.Write("APIC"u8); output.Write(Size(length)); output.WriteByte(0); output.WriteByte(0);
            output.WriteByte(3); output.Write(mime); output.WriteByte(0); output.WriteByte((byte)picture.Type);
            output.Write(description); output.WriteByte(0); output.Write(normalized[index].Data.AsSpan()[^picture.DataBytes..]);
        }
        output.Write(tag[offset..]); var result = output.ToArray(); Size(result.Length - 10).CopyTo(result, 6);
        return result;
    }

    private static void RequireGeometry(FlacPicture picture, ReadOnlySpan<byte> image)
    {
        uint width = 0, height = 0, bits = 0, colors = 0;
        if (picture.MediaType == "image/png")
        {
            if (image.Length < 33 || !image[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                BinaryPrimitives.ReadUInt32BigEndian(image[8..]) != 13 || !image.Slice(12, 4).SequenceEqual("IHDR"u8))
                throw new InvalidDataException("MP3 cover lacks a complete PNG header.");
            width = BinaryPrimitives.ReadUInt32BigEndian(image[16..]); height = BinaryPrimitives.ReadUInt32BigEndian(image[20..]);
            var depth = image[24]; var color = image[25];
            var channels = color switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
            if (channels == 0 || (color is 0 or 3 ? depth is not (1 or 2 or 4 or 8) && !(color == 0 && depth == 16) : depth is not (8 or 16)) ||
                image[26] != 0 || image[27] != 0 || image[28] > 1)
                throw new InvalidDataException("MP3 cover has unsupported PNG header fields.");
            bits = (uint)(depth * channels);
            if (picture.Colors != 0)
            {
                if (color != 3) throw new NotSupportedException("Picture palette declarations disagree with the embedded PNG.");
                var offset = 33;
                while (offset <= image.Length - 12)
                {
                    var length = BinaryPrimitives.ReadUInt32BigEndian(image[offset..]);
                    if (length > image.Length - offset - 12) throw new InvalidDataException("PNG cover chunk is truncated.");
                    if (image.Slice(offset + 4, 4).SequenceEqual("IDAT"u8)) break;
                    if (image.Slice(offset + 4, 4).SequenceEqual("PLTE"u8))
                    {
                        if (length == 0 || length % 3 != 0 || length > 3 * (1 << depth)) throw new InvalidDataException("PNG cover palette is invalid.");
                        colors = length / 3; break;
                    }
                    offset += 12 + (int)length;
                }
            }
        }
        else
        {
            if (!image.StartsWith(new byte[] { 255, 216, 255 })) throw new InvalidDataException("MP3 cover is not a JPEG.");
            var facts = ImmutableArray.CreateBuilder<AnalysisFact>(); var warnings = ImmutableArray.CreateBuilder<string>();
            ImageHeaderFacts.Add("jpeg", image, image.Length, facts, warnings);
            uint Number(string name) => checked((uint)(facts.SingleOrDefault(fact => fact.Id == name)?.Integer ?? 0));
            width = Number("image.width"); height = Number("image.height");
            bits = Number("jpeg.precision") * Number("jpeg.components");
        }
        if (width == 0 || height == 0 || bits == 0 ||
            picture.Type == 1 && (picture.MediaType != "image/png" || width != 32 || height != 32))
            throw new NotSupportedException("The cover geometry or file icon cannot be verified for MP3.");
        if (picture.Width != 0 && picture.Width != width || picture.Height != 0 && picture.Height != height ||
            picture.BitsPerPixel != 0 && picture.BitsPerPixel != bits || picture.Colors != 0 && picture.Colors != colors)
            throw new NotSupportedException("Picture declarations disagree with its encoded image. Choose FLAC or Ogg to preserve both.");
    }

    private static int ReadSize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4 || bytes.ContainsAnyInRange((byte)128, byte.MaxValue)) throw new InvalidDataException("Invalid ID3 synchsafe size.");
        return bytes[0] << 21 | bytes[1] << 14 | bytes[2] << 7 | bytes[3];
    }
    private static byte[] Size(int value) => [(byte)(value >> 21 & 127), (byte)(value >> 14 & 127), (byte)(value >> 7 & 127), (byte)(value & 127)];
}
