using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

internal static class PngFixture
{
    // Independently authored, unfiltered PNG samples; never use the engine under test to make golden pixels.
    public static byte[] Create(uint width, uint height, byte depth, byte color, Func<int, int, ushort[]> pixel,
        params (string Name, byte[] Data)[] chunks)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), height);
        header[8] = depth; header[9] = color;
        stream.Write(Chunk("IHDR", header));
        foreach (var chunk in chunks) stream.Write(Chunk(chunk.Name, chunk.Data));
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
        {
            Span<byte> sample = stackalloc byte[2];
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                for (var x = 0; x < width; x++)
                foreach (var value in pixel(x, y))
                {
                    if (depth == 16) { BinaryPrimitives.WriteUInt16BigEndian(sample, value); zlib.Write(sample); }
                    else zlib.WriteByte((byte)(value / 257));
                }
            }
        }
        stream.Write(Chunk("IDAT", compressed.ToArray()));
        stream.Write(Chunk("IEND", []));
        return stream.ToArray();
    }

    public static byte[] Chunk(string name, byte[] data)
    {
        var result = new byte[data.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)data.Length);
        Encoding.ASCII.GetBytes(name).CopyTo(result, 4);
        data.CopyTo(result, 8);
        uint crc = uint.MaxValue;
        foreach (var value in result.AsSpan(4, data.Length + 4))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(data.Length + 8), ~crc);
        return result;
    }

    // Assemble a grayscale profile from the selected dependency's sRGB white point/TRC.
    // No external profile asset is copied into the repository.
    public static byte[] GrayProfile(byte[] rgb)
    {
        var tags = new List<(string Name, byte[] Bytes)>();
        var count = BinaryPrimitives.ReadUInt32BigEndian(rgb.AsSpan(128));
        for (var i = 0; i < count; i++)
        {
            var entry = rgb.AsSpan(132 + i * 12, 12);
            var name = Encoding.ASCII.GetString(entry[..4]);
            if (name is not ("wtpt" or "rTRC" or "desc" or "cprt")) continue;
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            tags.Add((name == "rTRC" ? "kTRC" : name, rgb.AsSpan(offset, length).ToArray()));
        }
        if (tags.Count != 4) throw new InvalidDataException("The pinned profile has an unexpected tag layout.");
        var cursor = 132 + tags.Count * 12;
        var result = new byte[cursor + tags.Sum(tag => (tag.Bytes.Length + 3) & ~3)];
        rgb.AsSpan(0, 128).CopyTo(result);
        "GRAY"u8.CopyTo(result.AsSpan(16));
        result.AsSpan(84, 16).Clear(); // The modified profile no longer has the RGB profile's ID.
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)result.Length);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(128), (uint)tags.Count);
        for (var i = 0; i < tags.Count; i++)
        {
            var entry = result.AsSpan(132 + i * 12, 12);
            Encoding.ASCII.GetBytes(tags[i].Name).CopyTo(entry);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)cursor);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)tags[i].Bytes.Length);
            tags[i].Bytes.CopyTo(result, cursor);
            cursor += (tags[i].Bytes.Length + 3) & ~3;
        }
        return result;
    }
}
