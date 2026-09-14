using System.Buffers.Binary;
using System.Collections.Immutable;
using ContextSuite.Core.Analysis;

namespace ContextSuite.Core.Audio;

internal static class AudioPictureGeometry
{
    internal static void Require(FlacPicture picture, ReadOnlySpan<byte> image)
    {
        uint width = 0, height = 0, bits = 0, colors = 0;
        if (picture.MediaType == "image/png")
        {
            if (image.Length < 33 || !image[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                BinaryPrimitives.ReadUInt32BigEndian(image[8..]) != 13 || !image.Slice(12, 4).SequenceEqual("IHDR"u8))
                throw new InvalidDataException("Audio cover lacks a complete PNG header.");
            width = BinaryPrimitives.ReadUInt32BigEndian(image[16..]); height = BinaryPrimitives.ReadUInt32BigEndian(image[20..]);
            var depth = image[24]; var color = image[25];
            var channels = color switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
            if (channels == 0 || (color is 0 or 3 ? depth is not (1 or 2 or 4 or 8) && !(color == 0 && depth == 16) : depth is not (8 or 16)) ||
                image[26] != 0 || image[27] != 0 || image[28] > 1)
                throw new InvalidDataException("Audio cover has unsupported PNG header fields.");
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
            if (!image.StartsWith(new byte[] { 255, 216, 255 })) throw new InvalidDataException("Audio cover is not a JPEG.");
            var facts = ImmutableArray.CreateBuilder<AnalysisFact>(); var warnings = ImmutableArray.CreateBuilder<string>();
            ImageHeaderFacts.Add("jpeg", image, image.Length, facts, warnings);
            uint Number(string name) => checked((uint)(facts.SingleOrDefault(fact => fact.Id == name)?.Integer ?? 0));
            width = Number("image.width"); height = Number("image.height");
            bits = Number("jpeg.precision") * Number("jpeg.components");
        }
        if (width == 0 || height == 0 || bits == 0 ||
            picture.Type == 1 && (picture.MediaType != "image/png" || width != 32 || height != 32))
            throw new NotSupportedException("The cover geometry or file icon cannot be verified for audio conversion.");
        if (picture.Width != 0 && picture.Width != width || picture.Height != 0 && picture.Height != height ||
            picture.BitsPerPixel != 0 && picture.BitsPerPixel != bits || picture.Colors != 0 && picture.Colors != colors)
            throw new NotSupportedException("Picture declarations disagree with its encoded image. Choose FLAC or Ogg to preserve both.");
    }

}
