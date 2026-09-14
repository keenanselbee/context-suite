using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace ContextSuite.Core.Audio;

internal static class M4aPictures
{
    internal static FlacMetadataBlock Read(ReadOnlySpan<byte> data, ref int pictureTextBytes)
    {
        if (data.Length < 8 || M4aBoxes.Number(data, 4) != 0)
            throw new NotSupportedException("Localized or truncated M4A artwork needs a preservation handler.");
        var mime = M4aBoxes.Number(data, 0) switch
        {
            13 => "image/jpeg", 14 => "image/png",
            _ => throw new NotSupportedException("This M4A artwork data type needs a preservation handler.")
        };
        var image = data[8..];
        if (mime == "image/png" ? !image.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) : !image.StartsWith(new byte[] { 255, 216, 255 }))
            throw new InvalidDataException("M4A artwork type and image signature disagree.");
        var length = 32L + mime.Length + image.Length;
        var fieldBytes = 23L + 4L * ((length + 2) / 3);
        if (fieldBytes > VorbisComments.MaximumPictureTextBytes - pictureTextBytes)
            throw new InvalidDataException("M4A artwork exceeds its picture transport budget.");
        pictureTextBytes += (int)fieldBytes;
        // covr contains an image type and bytes, without a front/back designation,
        // description or dimensions. Use Other and empty/zero declarations.
        var result = new byte[(int)length]; var offset = 0;
        Number(0); Number((uint)mime.Length); Encoding.ASCII.GetBytes(mime).CopyTo(result, offset); offset += mime.Length;
        Number(0); Number(0); Number(0); Number(0); Number(0); Number((uint)image.Length); image.CopyTo(result.AsSpan(offset));
        return new(6, result.ToImmutableArray());
        void Number(uint value) { BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(offset), value); offset += 4; }
    }
}
