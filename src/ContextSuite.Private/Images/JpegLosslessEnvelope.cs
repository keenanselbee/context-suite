using System.Buffers.Binary;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Images;

// The native coefficient optimizer combines APP markers of the same type. Keep
// those markers outside that operation, and restore their original framing.
internal sealed class JpegLosslessEnvelope
{
    private sealed record Segment(byte Marker, byte[] Bytes);
    private readonly List<Segment> _segments = [];
    private readonly byte[] _scan;
    private readonly byte[] _frame;
    private readonly SortedDictionary<byte, byte[]> _tables = [];

    public JpegLosslessEnvelope(byte[] bytes)
    {
        if (ImageContainer.Read(bytes).Format != ImageFormat.Jpeg) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var position = 2;
        byte[]? frame = null;
        while (position < bytes.Length - 2)
        {
            var start = position;
            if (bytes[position++] != 255) throw new InvalidDataException("Invalid JPEG marker.");
            while (position < bytes.Length && bytes[position] == 255) position++;
            if (position >= bytes.Length) throw new InvalidDataException("Truncated JPEG marker.");
            var marker = bytes[position++];
            if (position > bytes.Length - 2) throw new InvalidDataException("Truncated JPEG segment.");
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position));
            if (length < 2 || length > bytes.Length - position) throw new InvalidDataException("Invalid JPEG segment length.");
            var data = bytes.AsSpan(position + 2, length - 2);
            if (marker == 218)
            {
                _scan = bytes[start..];
                _frame = frame ?? throw new InvalidDataException("Missing JPEG frame.");
                ValidateScanTail(_scan);
                return;
            }
            if (marker is 192 or 194) frame = data.ToArray();
            if (marker == 219)
            {
                for (var offset = 0; offset < data.Length;)
                {
                    var key = data[offset];
                    var count = (key >> 4) switch { 0 => 65, 1 => 129, _ => throw new InvalidDataException("Invalid JPEG quantization table.") };
                    if ((key & 15) > 3 || count > data.Length - offset || !_tables.TryAdd((byte)(key & 15), data.Slice(offset, count).ToArray()))
                        throw new ImageFailureException(ImageFailure.UnsupportedInput);
                    offset += count;
                }
            }
            if (marker is >= 224 and <= 239 && !KnownApplication(marker, data))
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            _segments.Add(new(marker, bytes[start..(position + length)]));
            position += length;
        }
        throw new InvalidDataException("Missing JPEG scan.");
    }

    private static bool KnownApplication(byte marker, ReadOnlySpan<byte> data) => marker switch
    {
        224 => data.StartsWith("JFIF\0"u8),
        225 => data.StartsWith("Exif\0\0"u8) || data.StartsWith("http://ns.adobe.com/xap/1.0/\0"u8),
        226 => data.StartsWith("ICC_PROFILE\0"u8),
        238 => data.StartsWith("Adobe"u8),
        _ => false
    };

    private static void ValidateScanTail(byte[] bytes)
    {
        // Quantization changes between progressive scans are outside this recipe.
        for (var position = 0; position < bytes.Length - 1; position++)
        {
            if (bytes[position] != 255) continue;
            var next = position + 1;
            while (next < bytes.Length && bytes[next] == 255) next++;
            if (next >= bytes.Length) throw new InvalidDataException("Truncated JPEG scan.");
            var marker = bytes[next];
            if (marker is 0 or >= 208 and <= 215) { position = next; continue; }
            if (marker == 217) return;
            if (marker == 219 || marker is >= 224 and <= 239 || marker == 254)
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            if (next + 2 >= bytes.Length) throw new InvalidDataException("Truncated JPEG scan segment.");
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(next + 1));
            if (length < 2 || length > bytes.Length - next - 1) throw new InvalidDataException("Invalid JPEG scan segment.");
            position = next + length;
        }
        throw new InvalidDataException("Missing JPEG ending.");
    }

    public byte[] WorkingBytes()
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 255, 216 });
        foreach (var segment in _segments)
            if (!IsMetadata(segment.Marker) || segment.Marker is 224 or 238) output.Write(segment.Bytes);
        output.Write(_scan);
        return output.ToArray();
    }

    internal bool CanReencode => _frame[5] == 1 ||
        (_frame.Length == 15 && _frame[5] == 3 && _frame[6] == 1 && _frame[9] == 2 && _frame[12] == 3 &&
         _segments.Where(segment => segment.Marker == 238).All(segment =>
             segment.Bytes.Length == 16 && segment.Bytes[^1] == 1));

    internal bool IsGrayscale => _frame[5] == 1;

    public byte[] Restore(byte[] encoded, bool exactCoefficients = true)
    {
        var candidate = new JpegLosslessEnvelope(encoded);
        if (exactCoefficients ? !_frame.SequenceEqual(candidate._frame) || _tables.Count != candidate._tables.Count ||
            _tables.Any(table => !candidate._tables.TryGetValue(table.Key, out var value) || !table.Value.SequenceEqual(value)) :
            !CanReencode || !candidate.CanReencode || !_frame.AsSpan(0, 6).SequenceEqual(candidate._frame.AsSpan(0, 6)))
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        using var output = new MemoryStream();
        output.Write(new byte[] { 255, 216 });
        foreach (var segment in _segments.Where(segment => IsMetadata(segment.Marker))) output.Write(segment.Bytes);
        foreach (var segment in candidate._segments.Where(segment => !IsMetadata(segment.Marker))) output.Write(segment.Bytes);
        output.Write(candidate._scan);
        if (output.Length > ImageContainer.MaximumFileBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        var result = output.ToArray();
        var restored = new JpegLosslessEnvelope(result);
        if (!MetadataBytes().SequenceEqual(restored.MetadataBytes())) throw new ImageFailureException(ImageFailure.ValidationFailed);
        return result;
    }

    internal byte[] MetadataBytes() => _segments.Where(segment => IsMetadata(segment.Marker)).SelectMany(segment => segment.Bytes).ToArray();
    private static bool IsMetadata(byte marker) => marker is >= 224 and <= 239 or 254;
}
