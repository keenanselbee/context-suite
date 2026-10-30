namespace ContextSuite.Private.Office;

// DeflateStream verifies decoded content but permits truncated/trailing streams
// in some cases. Inspect framing separately before trusting a ZIP member extent.
internal static class OfficePackageDeflate
{
    private static readonly int[] LengthBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    private static readonly int[] LengthBits = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static readonly int[] DistanceBase = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
    private static readonly int[] DistanceBits = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];

    public static void Validate(ReadOnlySpan<byte> bytes, int expected, CancellationToken token)
    {
        var reader = new Bits(bytes); var written = 0; var operations = 0; var blocks = 0;
        var final = false;
        while (!final)
        {
            if (++blocks > 65536) throw new InvalidDataException("Deflate block count exceeds its budget.");
            Check(); final = reader.Read(1) != 0;
            var type = reader.Read(2);
            if (type == 0)
            {
                reader.Align(); var length = reader.Read(16);
                if ((length ^ reader.Read(16)) != 65535) throw new InvalidDataException("Invalid stored Deflate block.");
                written = checked(written + length); reader.SkipBytes(length);
            }
            else if (type is 1 or 2)
            {
                int[] lengths, distances;
                if (type == 1)
                {
                    lengths = new int[288]; distances = Enumerable.Repeat(5, 32).ToArray();
                    for (var index = 0; index < lengths.Length; index++) lengths[index] = index <= 143 ? 8 : index <= 255 ? 9 : index <= 279 ? 7 : 8;
                }
                else
                {
                    var literalCount = reader.Read(5) + 257; var distanceCount = reader.Read(5) + 1; var codeCount = reader.Read(4) + 4;
                    if (literalCount > 286) throw new InvalidDataException("Reserved Deflate literal code count.");
                    int[] order = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];
                    var codes = new int[19];
                    for (var index = 0; index < codeCount; index++) codes[order[index]] = reader.Read(3);
                    var codeTree = new Huffman(codes); var combined = new int[literalCount + distanceCount]; var count = 0;
                    while (count < combined.Length)
                    {
                        Check(); var value = codeTree.Decode(ref reader);
                        if (value < 16) combined[count++] = value;
                        else
                        {
                            if (value == 16 && count == 0) throw new InvalidDataException("Invalid Deflate code repeat.");
                            var repeat = value == 16 ? reader.Read(2) + 3 : value == 17 ? reader.Read(3) + 3 : reader.Read(7) + 11;
                            var length = value == 16 ? combined[count - 1] : 0;
                            if (repeat > combined.Length - count) throw new InvalidDataException("Deflate code repeat exceeds table.");
                            Array.Fill(combined, length, count, repeat); count += repeat;
                        }
                    }
                    lengths = combined[..literalCount]; distances = combined[literalCount..];
                }
                if (lengths[256] == 0) throw new InvalidDataException("Deflate end code is missing.");
                var literals = new Huffman(lengths); var distanceTree = new Huffman(distances);
                while (true)
                {
                    Check(); var symbol = literals.Decode(ref reader);
                    if (symbol == 256) break;
                    if (symbol < 256) written++;
                    else
                    {
                        if (symbol > 285) throw new InvalidDataException("Reserved Deflate length symbol.");
                        var length = LengthBase[symbol - 257] + reader.Read(LengthBits[symbol - 257]);
                        var distanceCode = distanceTree.Decode(ref reader);
                        if (distanceCode >= 30) throw new InvalidDataException("Reserved Deflate distance symbol.");
                        var distance = DistanceBase[distanceCode] + reader.Read(DistanceBits[distanceCode]);
                        if (distance > written) throw new InvalidDataException("Deflate distance precedes decoded history.");
                        written = checked(written + length);
                    }
                    if (written > expected) throw new InvalidDataException("Deflate expansion exceeds its declared extent.");
                }
            }
            else throw new InvalidDataException("Reserved Deflate block type.");
            if (written > expected) throw new InvalidDataException("Deflate expansion exceeds its declared extent.");
        }
        if (written != expected || reader.ConsumedBytes != bytes.Length) throw new InvalidDataException("Truncated or trailing Deflate member data.");

        void Check()
        {
            if ((operations++ & 1023) == 0) token.ThrowIfCancellationRequested();
            // Nonempty output is bounded separately; also bound empty-block work.
            if (operations > 128 * 1024 * 1024) throw new InvalidDataException("Deflate framing work exceeds its budget.");
        }
    }

    private sealed class Huffman
    {
        private readonly Dictionary<int, int> _symbols = [];
        public Huffman(int[] lengths)
        {
            var counts = new int[16];
            foreach (var length in lengths)
            {
                if (length is < 0 or > 15) throw new InvalidDataException("Invalid Deflate code length.");
                if (length != 0) counts[length]++;
            }
            var next = new int[16]; var code = 0; var remaining = 1;
            for (var length = 1; length <= 15; length++)
            {
                remaining = (remaining << 1) - counts[length];
                if (remaining < 0) throw new InvalidDataException("Oversubscribed Deflate code table.");
                code = (code + counts[length - 1]) << 1; next[length] = code;
            }
            for (var symbol = 0; symbol < lengths.Length; symbol++)
            {
                var length = lengths[symbol]; if (length == 0) continue;
                var normal = next[length]++; var reversed = 0;
                for (var bit = 0; bit < length; bit++) reversed = (reversed << 1) | ((normal >> bit) & 1);
                _symbols.Add((1 << length) | reversed, symbol);
            }
        }
        public int Decode(ref Bits reader)
        {
            var code = 0;
            for (var length = 1; length <= 15; length++)
            {
                code |= reader.Read(1) << (length - 1);
                if (_symbols.TryGetValue((1 << length) | code, out var symbol)) return symbol;
            }
            throw new InvalidDataException("Invalid Deflate Huffman symbol.");
        }
    }

    private ref struct Bits(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _position;
        public readonly int ConsumedBytes => (_position + 7) / 8;
        public int Read(int count)
        {
            if ((long)_position + count > _bytes.Length * 8L) throw new InvalidDataException("Truncated Deflate framing.");
            var value = 0;
            for (var bit = 0; bit < count; bit++, _position++) value |= ((_bytes[_position >> 3] >> (_position & 7)) & 1) << bit;
            return value;
        }
        public void Align() { _position = (_position + 7) & ~7; }
        public void SkipBytes(int count)
        {
            if ((long)_position + count * 8L > _bytes.Length * 8L) throw new InvalidDataException("Truncated stored Deflate payload.");
            _position += count * 8;
        }
    }
}
