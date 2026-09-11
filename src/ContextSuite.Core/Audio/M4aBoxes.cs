using System.Buffers.Binary;
using System.Text;

namespace ContextSuite.Core.Audio;

internal sealed record M4aBox(string Type, ReadOnlyMemory<byte> Data);

// All children retain slices of one bounded movie buffer. No nested payload copies.
internal sealed class M4aBoxes
{
    private int _count;
    public const int MaximumBoxes = 16384;

    public List<M4aBox> Read(ReadOnlyMemory<byte> memory, params string[] allowed)
    {
        var boxes = new List<M4aBox>(); var offset = 0;
        while (offset < memory.Length)
        {
            if (++_count > MaximumBoxes || offset > memory.Length - 8) throw new InvalidDataException("M4A atom count or header extent is invalid.");
            var data = memory.Span[offset..];
            ulong length = BinaryPrimitives.ReadUInt32BigEndian(data); var header = 8;
            var type = Encoding.Latin1.GetString(data.Slice(4, 4));
            if (length == 1)
            {
                if (data.Length < 16) throw new InvalidDataException("Truncated extended M4A atom.");
                length = BinaryPrimitives.ReadUInt64BigEndian(data[8..]); header = 16;
            }
            if (length < (uint)header || length > (ulong)data.Length) throw new InvalidDataException("M4A atom crosses its parent extent.");
            if (type is not ("free" or "skip"))
            {
                if (allowed.Length != 0 && !allowed.Contains(type)) throw new NotSupportedException("M4A atom needs a preservation handler: " + type);
                boxes.Add(new(type, memory.Slice(offset + header, (int)length - header)));
            }
            offset += (int)length;
        }
        return boxes;
    }

    public static ReadOnlyMemory<byte> One(List<M4aBox> boxes, string type)
    {
        var matches = boxes.Where(box => box.Type == type).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("M4A requires exactly one " + type + " atom.");
        return matches[0].Data;
    }
    public static void FullBox(ReadOnlySpan<byte> data, int minimumBytes, byte version = 0)
    {
        if (data.Length < minimumBytes || data[0] != version || data.Slice(1, 3).IndexOfAnyExcept((byte)0) >= 0)
            throw new NotSupportedException("This M4A full-box version, flags or extent needs a handler.");
    }
    public static uint Number(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset > data.Length - 4) throw new InvalidDataException("Truncated M4A integer.");
        return BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
    }
}
