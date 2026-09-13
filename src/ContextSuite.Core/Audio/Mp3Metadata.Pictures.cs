using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace ContextSuite.Core.Audio;

public static partial class Mp3Metadata
{
    private static FlacMetadataBlock ReadPicture(ReadOnlySpan<byte> data, int version, ref int textBytes)
    {
        var encoding = data[0]; var offset = 1;
        string mime;
        if (version == 2)
        {
            if (data.Length < 6) throw new InvalidDataException("ID3 picture header is truncated.");
            mime = Encoding.ASCII.GetString(data.Slice(1, 3)) switch
            {
                "PNG" => "image/png", "JPG" => "image/jpeg",
                _ => throw new NotSupportedException("This ID3 picture format needs a preservation handler.")
            };
            offset = 4;
        }
        else
        {
            var end = data[1..].IndexOf((byte)0);
            if (end < 0 || end > 127 || end > data.Length - 3) throw new InvalidDataException("ID3 picture MIME type is truncated or oversized.");
            mime = Encoding.Latin1.GetString(data.Slice(1, end)); offset = end + 2;
            if (mime is not ("image/png" or "image/jpeg"))
                throw new NotSupportedException("Linked or other ID3 picture formats need a preservation handler.");
        }
        var type = data[offset++];
        var step = encoding is 1 or 2 ? 2 : 1;
        var start = offset;
        while (offset <= data.Length - step && (data[offset] != 0 || step == 2 && data[offset + 1] != 0)) offset += step;
        if (offset > data.Length - step) throw new InvalidDataException("ID3 picture description lacks its terminator.");
        var descriptionBytes = data[start..offset];
        var description = descriptionBytes.IsEmpty ? "" : Decode(descriptionBytes, encoding, ref textBytes, true);
        if (description.Length > 64) throw new NotSupportedException("ID3 picture descriptions exceed the reviewed length.");
        offset += step;
        var image = data[offset..];
        if (type > 20 || image.IsEmpty) throw new InvalidDataException("ID3 picture type or image extent is invalid.");
        if (mime == "image/png" ? !image.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) : !image.StartsWith(new byte[] { 255, 216, 255 }))
            throw new InvalidDataException("ID3 picture format and signature disagree.");
        if (type == 1 && (mime != "image/png" || image.Length < 24 ||
            !image.Slice(12, 4).SequenceEqual("IHDR"u8) || BinaryPrimitives.ReadUInt32BigEndian(image[16..]) != 32 || BinaryPrimitives.ReadUInt32BigEndian(image[20..]) != 32))
            throw new InvalidDataException("The ID3 file icon requires a declared 32-square PNG.");
        // ID3 has no width/height/depth fields. FLAC/Xiph explicitly allow zero
        // declarations; never invent dimensions or decode an image in this reader.
        var mimeBytes = Encoding.ASCII.GetBytes(mime); var descriptionUtf8 = Encoding.UTF8.GetBytes(description);
        var result = new byte[32 + mimeBytes.Length + descriptionUtf8.Length + image.Length]; var position = 0;
        Number(type); Number((uint)mimeBytes.Length); mimeBytes.CopyTo(result, position); position += mimeBytes.Length;
        Number((uint)descriptionUtf8.Length); descriptionUtf8.CopyTo(result, position); position += descriptionUtf8.Length;
        Number(0); Number(0); Number(0); Number(0); Number((uint)image.Length); image.CopyTo(result.AsSpan(position));
        return new(6, result.ToImmutableArray());
        void Number(uint number) { BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(position), number); position += 4; }
    }
}
