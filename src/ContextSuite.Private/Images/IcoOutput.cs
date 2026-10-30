using System.Buffers.Binary;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Images;

internal sealed record IconPixels(uint Size, byte[] Bgra);

// Deliberately output-only: ordinary 32-bit BI_RGB icons with straight alpha,
// bottom-up color rows and a complete one-bit transparency mask. No ICO decoder
// or additional ImageMagick coder is enabled for arbitrary source files.
internal static class IcoOutput
{
    private const int MaximumBytes = 512 * 1024;

    public static void Write(Stream output, IReadOnlyList<IconPixels> frames, CancellationToken token)
    {
        if (frames.Count is < 1 or > 7) throw new InvalidDataException("Invalid icon frame count.");
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)frames.Count);
        var offset = 6 + frames.Count * 16;
        foreach (var frame in frames)
        {
            if (frame.Size is 0 or > 256 || frame.Bgra.Length != checked((int)(frame.Size * frame.Size * 4)))
                throw new InvalidDataException("Invalid icon frame.");
            var size = (int)frame.Size;
            var length = checked(40 + size * size * 4 + ((size + 31) / 32 * 4) * size);
            writer.Write((byte)(size == 256 ? 0 : size)); writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(length); writer.Write(offset);
            offset += length;
        }
        if (offset > MaximumBytes) throw new InvalidDataException("Icon output exceeds its bound.");
        foreach (var frame in frames)
        {
            token.ThrowIfCancellationRequested();
            var size = (int)frame.Size;
            var stride = (size + 31) / 32 * 4;
            writer.Write(40); writer.Write(size); writer.Write(size * 2);
            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0);
            writer.Write(size * size * 4 + stride * size);
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (var y = size - 1; y >= 0; y--)
            {
                token.ThrowIfCancellationRequested();
                writer.Write(frame.Bgra, y * size * 4, size * 4);
            }
            var mask = new byte[stride];
            for (var y = size - 1; y >= 0; y--)
            {
                token.ThrowIfCancellationRequested();
                Array.Clear(mask);
                for (var x = 0; x < size; x++)
                    if (frame.Bgra[(y * size + x) * 4 + 3] == 0) mask[x / 8] |= (byte)(128 >> (x % 8));
                writer.Write(mask);
            }
        }
    }

    public static void Validate(byte[] bytes, IReadOnlyList<IconPixels> expected, CancellationToken token)
    {
        void Require(bool valid)
        {
            if (!valid) throw new ImageFailureException(ImageFailure.ValidationFailed);
        }
        Require(bytes.Length is >= 22 and <= MaximumBytes && expected.Count is >= 1 and <= 7);
        ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2));
        uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4));
        Require(U16(0) == 0 && U16(2) == 1 && U16(4) == expected.Count && bytes.Length >= 6 + expected.Count * 16);
        var end = 6 + expected.Count * 16;
        for (var index = 0; index < expected.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var entry = 6 + index * 16;
            var width = bytes[entry] == 0 ? 256 : bytes[entry];
            var height = bytes[entry + 1] == 0 ? 256 : bytes[entry + 1];
            var length = U32(entry + 8);
            var offset = U32(entry + 12);
            Require(width == height && width == expected[index].Size && bytes[entry + 2] == 0 && bytes[entry + 3] == 0 &&
                U16(entry + 4) == 1 && U16(entry + 6) == 32 && offset == end && length >= 40 &&
                offset <= bytes.Length && length <= bytes.Length - offset);
            var header = checked((int)offset);
            var maskStride = (width + 31) / 32 * 4;
            Require(U32(header) == 40 && U32(header + 4) == width && U32(header + 8) == height * 2 &&
                U16(header + 12) == 1 && U16(header + 14) == 32 && U32(header + 16) == 0 &&
                U32(header + 20) == width * height * 4 + maskStride * height &&
                U32(header + 24) == 0 && U32(header + 28) == 0 && U32(header + 32) == 0 && U32(header + 36) == 0 &&
                length == 40 + width * height * 4 + maskStride * height);
            var maskStart = header + 40 + width * height * 4;
            for (var row = 0; row < height; row++)
            {
                token.ThrowIfCancellationRequested();
                var original = expected[index].Bgra.AsSpan((height - row - 1) * width * 4, width * 4);
                Require(bytes.AsSpan(header + 40 + row * width * 4, width * 4).SequenceEqual(original));
                for (var x = 0; x < maskStride * 8; x++)
                {
                    var transparent = x < width && original[x * 4 + 3] == 0;
                    var bit = (bytes[maskStart + row * maskStride + x / 8] & (128 >> (x % 8))) != 0;
                    Require(bit == transparent);
                }
            }
            end = checked((int)(offset + length));
        }
        Require(end == bytes.Length);
    }
}
