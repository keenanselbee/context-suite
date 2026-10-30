using System.Buffers.Binary;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Images;

// Deliberately bounded raster variants. Do not guess at palettes, masks, color
// profiles, premultiplied alpha, extension areas or embedded alternate codecs.
internal static class BitmapContainers
{
    public static ImageContainer Bmp(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 54) throw new InvalidDataException("Incomplete BMP header.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[14..]) != 40 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) != 24 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[30..]) != 0)
            throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]);
        var signedHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes[22..]);
        var height = Math.Abs((long)signedHeight);
        if (width <= 0 || height == 0 || width > 16384 || height > 16384 || width * height > 40_000_000)
            throw new ImageFailureException(ImageFailure.ResourceLimit);
        var stride = ((long)width * 3 + 3) & ~3L;
        var pixelBytes = stride * height;
        var recordedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[34..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[2..]) != bytes.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[6..]) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[10..]) != 54 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) != 1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[46..]) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[50..]) != 0 ||
            (recordedSize != 0 && recordedSize != pixelBytes) || 54 + pixelBytes != bytes.Length)
            throw new InvalidDataException("BMP layout, length or pixel data is inconsistent.");
        var x = BinaryPrimitives.ReadInt32LittleEndian(bytes[38..]);
        var y = BinaryPrimitives.ReadInt32LittleEndian(bytes[42..]);
        if (x < 0 || y < 0 || (x == 0) != (y == 0)) throw new InvalidDataException("BMP resolution is invalid.");
        return new(ImageFormat.Bmp, (uint)width, (uint)height, 8, false, false,
            Resolution: x == 0 ? null : new((uint)x, (uint)y, 3, 100));
    }

    public static ImageContainer Tga(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 18) throw new InvalidDataException("Incomplete TGA header.");
        var type = bytes[2];
        var depth = bytes[16];
        var descriptor = bytes[17];
        if (bytes[1] != 0 || type is not (2 or 10) || depth is not (24 or 32) || (descriptor & 0xc0) != 0 ||
            (descriptor & 15) != (depth == 32 ? 8 : 0))
            throw new ImageFailureException(ImageFailure.UnsupportedInput);
        if (!bytes.Slice(3, 9).SequenceEqual(new byte[9]))
            throw new ImageFailureException(ImageFailure.UnsupportedInput); // color map and nonzero page origins
        var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]);
        var count = (long)width * height;
        if (width is 0 or > 16384 || height is 0 or > 16384 || count > 40_000_000)
            throw new ImageFailureException(ImageFailure.ResourceLimit);
        var end = bytes.Length;
        if (end >= 44 && bytes[^18..].SequenceEqual("TRUEVISION-XFILE.\0"u8))
        {
            end -= 26;
            if (BinaryPrimitives.ReadUInt64LittleEndian(bytes[end..]) != 0)
                throw new ImageFailureException(ImageFailure.UnsupportedInput); // extension/developer data, including alpha/gamma semantics
        }
        var position = 18 + bytes[0];
        var pixelBytes = depth / 8;
        if (position > end) throw new InvalidDataException("Truncated TGA image ID.");
        if (type == 2)
        {
            if (position + count * pixelBytes != end) throw new InvalidDataException("TGA pixels are incomplete or have unhandled trailing data.");
        }
        else
        {
            long decoded = 0;
            while (decoded < count)
            {
                if (position >= end) throw new InvalidDataException("Truncated TGA RLE packet.");
                var packet = bytes[position++];
                var run = (packet & 127) + 1;
                if (decoded + run > count) throw new InvalidDataException("TGA RLE packet exceeds the raster.");
                position += ((packet & 128) != 0 ? 1 : run) * pixelBytes;
                if (position > end) throw new InvalidDataException("Truncated TGA RLE pixels.");
                decoded += run;
            }
            if (position != end) throw new InvalidDataException("TGA contains unhandled trailing data.");
        }
        return new(ImageFormat.Tga, width, height, 8, false, bytes[0] != 0);
    }
}
