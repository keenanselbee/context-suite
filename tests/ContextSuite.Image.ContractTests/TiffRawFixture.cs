using System.Buffers.Binary;

// Small independent classic-TIFF writer for byte order, sample and alpha
// boundary tests. It never calls an image SDK.
internal static class TiffRawFixture
{
    public static byte[] Create(bool little, ushort photo, ushort bits, ushort samples, ushort extra,
        ushort fillOrder, byte[] pixels, byte[]? icc = null, ushort resolutionUnit = 2, ushort compression = 1, ushort predictor = 1,
        uint rowsPerStrip = 4)
    {
        var tags = new SortedDictionary<ushort, (ushort Type, uint Count, byte[] Bytes)>();
        byte[] Short(ushort value)
        {
            var result = new byte[2];
            if (little) BinaryPrimitives.WriteUInt16LittleEndian(result, value); else BinaryPrimitives.WriteUInt16BigEndian(result, value);
            return result;
        }
        byte[] Long(uint value)
        {
            var result = new byte[4];
            if (little) BinaryPrimitives.WriteUInt32LittleEndian(result, value); else BinaryPrimitives.WriteUInt32BigEndian(result, value);
            return result;
        }
        void S(ushort tag, ushort value) => tags.Add(tag, (3, 1, Short(value)));
        void L(ushort tag, uint value) => tags.Add(tag, (4, 1, Long(value)));
        L(256, 8); L(257, 4); tags.Add(258, (3, samples, Enumerable.Range(0, samples).SelectMany(_ => Short(bits)).ToArray()));
        if (rowsPerStrip is not (2 or 4) || rowsPerStrip != 4 && compression != 1) throw new ArgumentException("Unsupported fixture strips.");
        var strips = 4 / rowsPerStrip;
        S(259, compression); S(262, photo); S(266, fillOrder);
        tags.Add(273, (4, strips, new byte[strips * 4])); S(274, 1); S(277, samples); L(278, rowsPerStrip);
        tags.Add(279, (4, strips, Enumerable.Range(0, (int)strips).SelectMany(_ => Long((uint)pixels.Length / strips)).ToArray()));
        if (predictor != 1) S(317, predictor);
        tags.Add(282, (5, 1, Long(150).Concat(Long(1)).ToArray()));
        tags.Add(283, (5, 1, Long(75).Concat(Long(1)).ToArray()));
        S(284, 1); S(296, resolutionUnit);
        if (samples == 4) S(338, extra);
        if (icc is not null) tags.Add(34675, (7, (uint)icc.Length, icc));
        var offset = 8 + 2 + tags.Count * 12 + 4;
        var payload = offset + tags.Values.Where(field => field.Bytes.Length > 4).Sum(field => field.Bytes.Length + (field.Bytes.Length & 1));
        tags[273] = (4, strips, Enumerable.Range(0, (int)strips).SelectMany(index => Long((uint)(payload + index * pixels.Length / strips))).ToArray());
        var body = new List<byte>(); var entries = new List<byte>();
        foreach (var (tag, field) in tags)
        {
            entries.AddRange(Short(tag)); entries.AddRange(Short(field.Type)); entries.AddRange(Long(field.Count));
            if (field.Bytes.Length <= 4) { entries.AddRange(field.Bytes); entries.AddRange(new byte[4 - field.Bytes.Length]); }
            else
            {
                entries.AddRange(Long((uint)(offset + body.Count))); body.AddRange(field.Bytes);
                if ((body.Count & 1) != 0) body.Add(0);
            }
        }
        return (little ? "II"u8.ToArray() : "MM"u8.ToArray()).Concat(Short(42)).Concat(Long(8))
            .Concat(Short((ushort)tags.Count)).Concat(entries).Concat(Long(0)).Concat(body).Concat(pixels).ToArray();
    }
}
