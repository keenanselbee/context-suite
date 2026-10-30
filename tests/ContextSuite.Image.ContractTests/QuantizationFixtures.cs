using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

// Authored, deterministic synthetic corpus. No third-party art, fonts, reference binaries or downloads.
internal static class QuantizationFixtures
{
    internal sealed record Fixture(string Name, int Width, int Height, byte[] Pixels, byte[] Png,
        string Provenance = "Context Suite authored synthetic generator v1; no external media");
    internal sealed record Chunk(string Type, byte[] Data);

    internal static IEnumerable<Fixture> Create(bool presetResearch = false)
    {
        var names = new[] { "flat-icons", "small-text", "gray-gradient", "color-gradient",
            "glow-alpha", "soft-edge-alpha", "hidden-rgb", "textured-scene", "noise", "tiny" };
        if (presetResearch) names = names.Concat(new[] { "dark-gray-gradient", "dark-color-gradient", "translucent-gradient" }).ToArray();
        foreach (var name in names)
        {
            int width = name == "tiny" ? 3 : 512, height = name == "tiny" ? 2 : 320;
            var pixels = new byte[width * height * 4];
            uint random = 0x12345678;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                double r = 0, g = 0, b = 0, a = 255;
                double u = x / (double)(width - 1), v = y / (double)(height - 1);
                double radius = Math.Sqrt(Math.Pow((x - width / 2.0) / 120, 2) + Math.Pow((y - height / 2.0) / 120, 2));
                switch (name)
                {
                    case "flat-icons":
                        int color = (x / 32 + y / 32) % 8;
                        r = (color & 1) * 220; g = ((color >> 1) & 1) * 210; b = ((color >> 2) & 1) * 240;
                        if (x % 32 < 2 || y % 32 < 2) r = g = b = 24;
                        break;
                    case "small-text":
                        // Repeated authored 5x7 glyphs; no system font rasterizer variability.
                        string[] glyphs = ["01110100011000111111100011000110001", "11110100011000111110100011000111110",
                            "01111100001000010000100001000001111", "11111001000010000100001000010000100"];
                        var glyph = glyphs[(x / 36 + y / 24) % glyphs.Length];
                        int gx = x % 12, gy = y % 16;
                        bool ink = gx < 10 && gy < 14 && glyph[(gy / 2) * 5 + gx / 2] == '1';
                        double background = 35 + 205 * v;
                        r = ink ? 240 - 210 * v : background; g = r; b = ink ? 185 : background;
                        break;
                    case "gray-gradient": r = g = b = 255 * u; break;
                    case "dark-gray-gradient": r = g = b = 48 * u; break;
                    case "dark-color-gradient": r = 48 * u; g = 40 * v; b = 32 * (1 - u * v); break;
                    case "translucent-gradient": r = 255 * u; g = 255 * v; b = 160 * (1 - u); a = 255 * v; break;
                    case "color-gradient": r = 255 * u; g = 255 * v; b = 255 * (1 - u * v); break;
                    case "glow-alpha": r = 60 + 150 * u; g = 100 + 130 * v; b = 255; a = 255 * Math.Exp(-4 * radius * radius); break;
                    case "soft-edge-alpha": r = 230; g = 50 + 180 * u; b = 40; a = 255 * Math.Clamp((1 - radius) * 15, 0, 1); break;
                    case "hidden-rgb": r = random & 255; g = (random >> 8) & 255; b = (random >> 16) & 255; a = x < width / 2 ? 0 : 255; break;
                    case "textured-scene":
                        // Synthetic terrain/sky proxy, explicitly NOT a photographic fixture.
                        double texture = ((random & 255) / 255.0 - .5) * 25;
                        bool sky = v < .46 + .07 * Math.Sin(u * 19);
                        r = sky ? 70 + 130 * v : 80 + 55 * Math.Sin(u * 13) + texture;
                        g = sky ? 120 + 130 * v : 100 + 55 * v + texture;
                        b = sky ? 180 + 60 * v : 45 + 20 * Math.Cos(v * 23) + texture;
                        break;
                    case "noise": case "tiny": r = random & 255; g = (random >> 8) & 255; b = (random >> 16) & 255; break;
                }
                int i = (y * width + x) * 4;
                pixels[i] = Byte(r); pixels[i + 1] = Byte(g); pixels[i + 2] = Byte(b); pixels[i + 3] = Byte(a);
            }
            var png = Encode(width, height, pixels);
            yield return new(name, width, height, pixels, png);
            if (name == "flat-icons")
            {
                var palette = new List<uint>(); var indices = new byte[width * height];
                for (int i = 0; i < indices.Length; i++)
                {
                    uint rgba = BinaryPrimitives.ReadUInt32BigEndian(pixels.AsSpan(i * 4));
                    int index = palette.IndexOf(rgba);
                    if (index < 0) { index = palette.Count; palette.Add(rgba); }
                    indices[i] = checked((byte)index);
                }
                using var compressed = new MemoryStream();
                using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
                for (int y = 0; y < height; y++) { zlib.WriteByte(0); zlib.Write(indices.AsSpan(y * width, width)); }
                var chunks = Parse(png); chunks[0].Data[9] = 3;
                chunks.RemoveAll(c => c.Type == "IDAT");
                chunks.Insert(chunks.Count - 1, new("PLTE", palette.SelectMany(c => new[] { (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8) }).ToArray()));
                chunks.Insert(chunks.Count - 1, new("IDAT", compressed.ToArray()));
                yield return new("indexed-icons", width, height, pixels.ToArray(), Pack(chunks));
            }
        }
    }

    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    internal static byte[] Encode(int width, int height, byte[] rgba)
    {
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
        for (var y = 0; y < height; y++)
        {
            zlib.WriteByte(0);
            zlib.Write(rgba.AsSpan(y * width * 4, width * 4));
        }
        byte[] gamma = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(gamma, 45455);
        byte[] density = new byte[9]; BinaryPrimitives.WriteInt32BigEndian(density, 3780); BinaryPrimitives.WriteInt32BigEndian(density.AsSpan(4), 3780); density[8] = 1;
        return Pack([new("IHDR", header), new("sRGB", [0]), new("gAMA", gamma), new("pHYs", density),
            new("tEXt", Encoding.ASCII.GetBytes("Comment\0Context Suite authored synthetic evaluation v1")),
            new("IDAT", compressed.ToArray()), new("IEND", [])]);
    }

    // Restricted fixture/report PNG utilities, not a new production parser.
    internal static List<Chunk> Parse(byte[] png)
    {
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < 20 || !png.AsSpan(0, 8).SequenceEqual(signature)) throw new InvalidDataException("PNG signature");
        var chunks = new List<Chunk>(); int offset = 8;
        while (offset < png.Length)
        {
            if (png.Length - offset < 12) throw new InvalidDataException("Truncated chunk");
            int count = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            if (count < 0 || count > png.Length - offset - 12) throw new InvalidDataException("Chunk length");
            var body = png.AsSpan(offset + 4, count + 4);
            if (Crc(body) != BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + count + 8))) throw new InvalidDataException("Chunk CRC");
            chunks.Add(new(Encoding.ASCII.GetString(body[..4]), body[4..].ToArray())); offset += count + 12;
        }
        if (chunks[0].Type != "IHDR" || chunks[^1].Type != "IEND") throw new InvalidDataException("PNG structure");
        return chunks;
    }

    internal static byte[] RestoreFixtureMetadata(byte[] source, byte[] output)
    {
        var result = Parse(output).Where(c => !MetadataTypes.Contains(c.Type)).ToList();
        result.InsertRange(1, Parse(source).Where(c => MetadataTypes.Contains(c.Type)));
        return Pack(result);
    }

    internal static readonly string[] MetadataTypes = ["sRGB", "gAMA", "pHYs", "tEXt"];

    internal static byte[] Pack(IEnumerable<Chunk> chunks)
    {
        using var stream = new MemoryStream(); stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        foreach (var chunk in chunks)
        {
            var body = Encoding.ASCII.GetBytes(chunk.Type).Concat(chunk.Data).ToArray();
            byte[] number = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, chunk.Data.Length); stream.Write(number);
            stream.Write(body); BinaryPrimitives.WriteUInt32BigEndian(number, Crc(body)); stream.Write(number);
        }
        return stream.ToArray();
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        return ~crc;
    }
}
