using System.Buffers.Binary;
using System.Text;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Images;

internal sealed class WebPLosslessEnvelope
{
    private sealed record Chunk(string Type, byte[] Bytes);
    private readonly List<Chunk> _chunks = [];
    private readonly byte[] _header;
    private readonly uint _width;
    private readonly uint _height;

    public WebPLosslessEnvelope(byte[] bytes)
    {
        var container = ImageContainer.Read(bytes);
        if (container.Format != ImageFormat.WebP) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        _header = bytes[..12];
        _width = container.Width;
        _height = container.Height;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var phase = 0;
        for (var position = 12; position < bytes.Length;)
        {
            var type = Encoding.ASCII.GetString(bytes, position, 4);
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 4)));
            var next = checked(position + 8 + length + (length & 1));
            if (!seen.Add(type) || type is not ("VP8X" or "ICCP" or "ALPH" or "VP8 " or "VP8L" or "EXIF" or "XMP "))
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            var order = type switch { "VP8X" => 0, "ICCP" => 1, "ALPH" => 2, "VP8L" or "VP8 " => 3, "EXIF" => 4, _ => 5 };
            if (order < phase) throw new InvalidDataException("Invalid WebP chunk order.");
            phase = order;
            if (type == "VP8L" && (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 9)) >> 29) != 0)
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            if (type == "ALPH" && (length < 1 || (bytes[position + 8] & 0xc0) != 0 ||
                (bytes[position + 8] & 3) > 1 || ((bytes[position + 8] >> 4) & 3) > 1))
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            if (type == "VP8 " && (bytes[position + 8] & 1) != 0)
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            _chunks.Add(new(type, bytes[position..next]));
            position = next;
        }
        if ((seen.Contains("VP8L") == seen.Contains("VP8 ")) || (seen.Contains("ALPH") && !seen.Contains("VP8 ")) ||
            (seen.Count > 1 && !seen.Contains("VP8X")))
            throw new ImageFailureException(ImageFailure.UnsupportedInput);
        if (seen.Contains("VP8X"))
        {
            var flag = _chunks.Single(chunk => chunk.Type == "VP8X").Bytes[8];
            var alpha = seen.Contains("ALPH") || (seen.Contains("VP8L") &&
                (_chunks.Single(chunk => chunk.Type == "VP8L").Bytes[12] & 0x10) != 0);
            if (((flag & 16) != 0) != alpha) throw new InvalidDataException("WebP alpha flags disagree with its coding chunks.");
        }
    }

    public byte[] Restore(byte[] encoded)
    {
        var candidate = new WebPLosslessEnvelope(encoded);
        var candidateContainer = ImageContainer.Read(encoded);
        if (_width != candidateContainer.Width || _height != candidateContainer.Height)
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        using var output = new MemoryStream();
        output.Write(_header);
        var coding = candidate._chunks.Where(chunk => chunk.Type is "ALPH" or "VP8 " or "VP8L").ToArray();
        var alpha = coding.Any(chunk => chunk.Type == "ALPH" || chunk.Type == "VP8L" && (chunk.Bytes[12] & 0x10) != 0);
        if (_chunks.Any(chunk => chunk.Type == "VP8X") || coding.Any(chunk => chunk.Type == "ALPH") || _chunks.Any(chunk => IsMetadata(chunk.Type)))
        {
            var extended = _chunks.FirstOrDefault(chunk => chunk.Type == "VP8X")?.Bytes.ToArray() ?? new byte[18];
            "VP8X"u8.CopyTo(extended);
            BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(4), 10);
            extended[8] = (byte)((_chunks.Any(chunk => chunk.Type == "ICCP") ? 32 : 0) | (alpha ? 16 : 0) |
                (_chunks.Any(chunk => chunk.Type == "EXIF") ? 8 : 0) | (_chunks.Any(chunk => chunk.Type == "XMP ") ? 4 : 0));
            WriteUInt24(extended.AsSpan(12), _width - 1);
            WriteUInt24(extended.AsSpan(15), _height - 1);
            output.Write(extended);
        }
        foreach (var chunk in _chunks.Where(chunk => chunk.Type == "ICCP")) output.Write(chunk.Bytes);
        foreach (var chunk in coding) output.Write(chunk.Bytes);
        foreach (var chunk in _chunks.Where(chunk => chunk.Type is "EXIF" or "XMP ")) output.Write(chunk.Bytes);
        if (output.Length > ImageContainer.MaximumFileBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        var result = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)result.Length - 8));
        var restored = new WebPLosslessEnvelope(result);
        if (!MetadataBytes().SequenceEqual(restored.MetadataBytes())) throw new ImageFailureException(ImageFailure.ValidationFailed);
        return result;
    }

    // VP8X describes the current coding payload; only descriptive/color chunks
    // remain byte-identical when switching between VP8 and VP8L.
    internal byte[] MetadataBytes() => _chunks.Where(chunk => IsMetadata(chunk.Type)).SelectMany(chunk => chunk.Bytes).ToArray();

    private static bool IsMetadata(string type) => type is "ICCP" or "EXIF" or "XMP ";

    private static void WriteUInt24(Span<byte> output, uint value)
    {
        output[0] = (byte)value;
        output[1] = (byte)(value >> 8);
        output[2] = (byte)(value >> 16);
    }
}
