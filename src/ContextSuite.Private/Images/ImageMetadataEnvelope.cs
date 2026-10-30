using System.Buffers.Binary;
using System.Text;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Images;

// ImageMagick synchronizes EXIF resolution to rounded integers at write time.
// Restore the already normalized, confirmed metadata in standard container fields,
// without re-encoding pixels. The complete result is then parsed and decoded again.
internal static class ImageMetadataEnvelope
{
    public static void Restore(Stream output, ImageFormat format, IReadOnlyDictionary<string, byte[]> profiles)
    {
        profiles.TryGetValue("exif", out var exif);
        profiles.TryGetValue("xmp", out var xmp);
        if (exif is null && xmp is null) return;
        if (output.Length > ImageContainer.MaximumFileBytes) throw new InvalidDataException("Encoded image exceeds its limit.");
        var bytes = new byte[checked((int)output.Length)];
        output.Position = 0;
        output.ReadExactly(bytes);
        _ = ImageContainer.Read(bytes);
        output.Position = 0;
        switch (format)
        {
            case ImageFormat.Jpeg:
                output.Write(bytes.AsSpan(0, 2));
                var offset = 2;
                // Retain the JFIF APP0 marker immediately after SOI when present.
                if (bytes[2] == 255 && bytes[3] == 224 && bytes.AsSpan(6).StartsWith("JFIF\0"u8))
                {
                    var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
                    output.Write(bytes.AsSpan(2, length + 2));
                    offset += length + 2;
                }
                if (exif is not null) JpegSegment(output, exif);
                if (xmp is not null) JpegSegment(output, "http://ns.adobe.com/xap/1.0/\0"u8.ToArray().Concat(xmp).ToArray());
                while (offset < bytes.Length - 2)
                {
                    var start = offset++;
                    while (bytes[offset] == 255) offset++;
                    var marker = bytes[offset++];
                    if (marker == 218) { output.Write(bytes.AsSpan(start)); break; }
                    var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset));
                    var data = bytes.AsSpan(offset + 2, length - 2);
                    if (!(marker == 225 && (data.StartsWith("Exif\0\0"u8) || data.StartsWith("http://ns.adobe.com/xap/1.0/\0"u8))))
                        output.Write(bytes.AsSpan(start, offset + length - start));
                    offset += length;
                }
                break;
            case ImageFormat.Png:
                output.Write(bytes.AsSpan(0, 33)); // Signature and validated IHDR.
                if (exif is not null) PngChunk(output, "eXIf", exif.AsSpan(6));
                if (xmp is not null) PngChunk(output, "iTXt", "XML:com.adobe.xmp\0\0\0\0\0"u8.ToArray().Concat(xmp).ToArray());
                for (var position = 33; position < bytes.Length;)
                {
                    var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position)));
                    var type = Encoding.ASCII.GetString(bytes, position + 4, 4);
                    var data = bytes.AsSpan(position + 8, length);
                    var replaced = type == "eXIf" || (type == "iTXt" && data.StartsWith("XML:com.adobe.xmp\0"u8)) ||
                        (type is "zTXt" or "tEXt" && (data.StartsWith("Raw profile type exif\0"u8) || data.StartsWith("Raw profile type xmp\0"u8)));
                    if (!replaced) output.Write(bytes.AsSpan(position, length + 12));
                    position += length + 12;
                }
                break;
            case ImageFormat.WebP:
                output.Write(bytes.AsSpan(0, 12));
                for (var position = 12; position < bytes.Length;)
                {
                    var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 4)));
                    var type = Encoding.ASCII.GetString(bytes, position, 4);
                    if (type is not ("EXIF" or "XMP ")) output.Write(bytes.AsSpan(position, length + 8 + (length & 1)));
                    position += length + 8 + (length & 1);
                }
                if (exif is not null) WebPChunk(output, "EXIF", exif);
                if (xmp is not null) WebPChunk(output, "XMP ", xmp);
                var end = output.Position;
                output.Position = 4;
                Span<byte> size = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(size, checked((uint)end - 8));
                output.Write(size);
                output.Position = end;
                break;
            default: throw new InvalidDataException("Unknown metadata container.");
        }
        if (output.Position > ImageContainer.MaximumFileBytes) throw new InvalidDataException("Encoded metadata exceeds the output limit.");
        output.SetLength(output.Position);
    }

    private static void JpegSegment(Stream output, ReadOnlySpan<byte> data)
    {
        if (data.Length > 65533) throw new InvalidDataException("Metadata exceeds JPEG's supported single-segment limit.");
        Span<byte> header = stackalloc byte[4];
        header[0] = 255; header[1] = 225;
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], (ushort)(data.Length + 2));
        output.Write(header); output.Write(data);
    }

    private static void PngChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        var chunk = new byte[data.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk.AsSpan(8));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ImageContainer.PngCrc(chunk.AsSpan(4, data.Length + 4)));
        output.Write(chunk);
    }

    private static void WebPChunk(Stream output, string type, byte[] data)
    {
        Span<byte> header = stackalloc byte[8];
        Encoding.ASCII.GetBytes(type, header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)data.Length);
        output.Write(header); output.Write(data);
        if ((data.Length & 1) != 0) output.WriteByte(0);
    }
}
