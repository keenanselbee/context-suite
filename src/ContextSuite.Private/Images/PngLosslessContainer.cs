using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Images;

// Independent of oxipng: retain original chunks and verify unfiltered sample bytes, including
// hidden RGB and all 16-bit precision. Lossless permits only recompression; the bounded precision
// path constructs an explicit transformed sample target while retaining the source representation.
internal sealed class PngLosslessContainer
{
    private sealed record Chunk(string Type, byte[] Bytes);
    private readonly List<Chunk> _chunks = [];
    private readonly byte[] _header;
    private readonly int _rowBytes;
    private readonly int _pixelBytes;
    private readonly int _height;
    private readonly int _width;
    private readonly int _bitsPerPixel;
    private readonly bool _hasIcc;

    // Only the researched fdEC exception is removed. Validate the complete source,
    // including every CRC, before making a byte-exact copy of all other chunks.
    internal static byte[] RemoveFdEC(byte[] bytes, CancellationToken token, out bool removed)
    {
        if (ImageContainer.Read(bytes).Format != ImageFormat.Png)
            throw new ImageFailureException(ImageFailure.UnsupportedInput);
        removed = false;
        using var output = new MemoryStream(bytes.Length);
        output.Write(bytes.AsSpan(0, 8));
        for (var offset = 8; offset < bytes.Length;)
        {
            token.ThrowIfCancellationRequested();
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)) + 12);
            if (bytes.AsSpan(offset + 4, 4).SequenceEqual("fdEC"u8)) removed = true;
            else output.Write(bytes.AsSpan(offset, length));
            offset += length;
        }
        if (!removed) return bytes;
        // Do not let normalization conceal invalid chunk ordering or other
        // unsupported metadata in the original container.
        _ = new PngLosslessContainer(bytes, allowFdEC: true);
        return output.ToArray();
    }

    public PngLosslessContainer(byte[] bytes, bool allowFdEC = false)
    {
        var info = ImageContainer.Read(bytes);
        if (info.Format != ImageFormat.Png) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        _hasIcc = info.ExpectedProfiles?.ContainsKey("icc") == true;
        _header = bytes.AsSpan(16, 13).ToArray();
        var depth = _header[8];
        var channels = _header[9] switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        if (channels == 0 || _header[10] != 0 || _header[11] != 0 || _header[12] > 1 ||
            (_header[9] == 3 && depth == 16) || (_header[9] is 2 or 4 or 6 && depth < 8))
            throw new InvalidDataException("Invalid PNG encoding.");
        _width = (int)info.Width;
        _bitsPerPixel = channels * depth;
        _rowBytes = checked((int)(((long)info.Width * channels * depth + 7) / 8));
        _pixelBytes = Math.Max(1, (channels * depth + 7) / 8);
        _height = (int)info.Height;
        var seenData = false;
        var endedData = false;
        var seen = new HashSet<string>();
        for (var offset = 8; offset < bytes.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)));
            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (type.Any(c => !char.IsAsciiLetter(c)) || !char.IsAsciiLetterUpper(type[2]))
                throw new InvalidDataException("Invalid PNG chunk type.");
            if (!(allowFdEC && type == "fdEC") && (type is "iDOT" or "caBX" || (char.IsAsciiLetterUpper(type[0]) && type is not ("IHDR" or "PLTE" or "IDAT" or "IEND")) ||
                (char.IsAsciiLetterUpper(type[3]) && type is not ("IHDR" or "PLTE" or "IDAT" or "IEND" or
                    "tRNS" or "cHRM" or "gAMA" or "iCCP" or "sRGB" or "sBIT" or "bKGD" or "hIST" or "pHYs" or "tIME"))))
                throw new ImageFailureException(ImageFailure.UnsupportedInput,
                    new NotSupportedException($"PNG metadata '{type}' cannot yet be safely preserved during optimization. The original is unchanged. Use another export, or keep this file as-is."));
            if (type == "IDAT")
            {
                if (endedData) throw new InvalidDataException("PNG data chunks must be consecutive.");
                seenData = true;
            }
            else if (seenData) endedData = true;
            if (type is "PLTE" or "tRNS")
            {
                if (seenData || !seen.Add(type)) throw new InvalidDataException("Invalid PNG palette/transparency ordering.");
                if (type == "PLTE" && (length == 0 || length > 768 || length % 3 != 0))
                    throw new InvalidDataException("Invalid PNG palette size.");
            }
            _chunks.Add(new(type, bytes.AsSpan(offset, length + 12).ToArray()));
            offset += length + 12;
        }
        if (_header[9] == 3 && !seen.Contains("PLTE")) throw new InvalidDataException("Indexed PNG has no palette.");
    }

    public string? LossyBlockReason
    {
        get
        {
            if (_header[12] == 1)
                return "This interlaced PNG will use lossless optimization, preserving its original samples and interlacing.";
            if (_header[8] != 8 || _header[9] is not (2 or 6))
                return "Lossy presets require 8-bit RGB/RGBA PNG. Choose Lossless for this representation.";
            if ((long)(_rowBytes / _pixelBytes) * _height > 16_000_000)
                return "Lossy PNG presets currently support at most 16 million pixels. Choose Lossless.";
            // Do not retain stale palette/significant-bit/transparency-key or unknown metadata after changing samples.
            if (_hasIcc || _chunks.Any(c => c.Type is not ("IHDR" or "IDAT" or "IEND" or "sRGB" or "gAMA" or "cHRM" or
                "pHYs" or "tIME" or "tEXt" or "zTXt" or "iTXt" or "eXIf")))
                return "This PNG has metadata or a color profile not yet supported by lossy presets. Choose Lossless.";
            return null;
        }
    }

    public byte[] RestoreAndValidate(byte[] encoded, CancellationToken cancellationToken)
    {
        var candidate = new PngLosslessContainer(encoded);
        if (!_header.SequenceEqual(candidate._header) || !ChunkData("PLTE").SequenceEqual(candidate.ChunkData("PLTE")) ||
            !ChunkData("tRNS").SequenceEqual(candidate.ChunkData("tRNS")) ||
            !SampleDigest(cancellationToken).SequenceEqual(candidate.SampleDigest(cancellationToken)))
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        using var result = new MemoryStream();
        result.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var inserted = false;
        foreach (var chunk in _chunks)
        {
            if (chunk.Type != "IDAT") result.Write(chunk.Bytes);
            else if (!inserted)
            {
                foreach (var data in candidate._chunks.Where(c => c.Type == "IDAT")) result.Write(data.Bytes);
                inserted = true;
            }
            if (result.Length > ImageContainer.MaximumFileBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        }
        return result.ToArray();
    }

    private byte[] ChunkData(string type) => _chunks.Where(c => c.Type == type).SelectMany(c => c.Bytes).ToArray();

    public byte[] SampleDigest(CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ReadRows(row => hash.AppendData(row), cancellationToken);
        return hash.GetHashAndReset();
    }

    public byte[] ReadRgba(CancellationToken cancellationToken)
    {
        if (LossyBlockReason is not null) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var pixels = new byte[checked(_rowBytes / _pixelBytes * _height * 4)];
        var offset = 0;
        ReadRows(row =>
        {
            for (var x = 0; x < row.Length; x += _pixelBytes)
            {
                row.AsSpan(x, 3).CopyTo(pixels.AsSpan(offset, 3));
                pixels[offset + 3] = _pixelBytes == 4 ? row[x + 3] : (byte)255;
                offset += 4;
            }
        }, cancellationToken);
        return pixels;
    }

    public byte[] EncodePrecision(byte[] rgba, CancellationToken cancellationToken)
    {
        if (LossyBlockReason is not null || rgba.Length != checked(_rowBytes / _pixelBytes * _height * 4))
            throw new ImageFailureException(ImageFailure.UnsupportedInput);
        using var compressed = new MemoryStream();
        using (var deflater = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            var row = new byte[_rowBytes];
            for (var y = 0; y < _height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < row.Length / _pixelBytes; x++)
                    rgba.AsSpan((y * (row.Length / _pixelBytes) + x) * 4, _pixelBytes).CopyTo(row.AsSpan(x * _pixelBytes));
                deflater.WriteByte(0);
                deflater.Write(row);
            }
        }
        using var result = new MemoryStream();
        result.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var inserted = false;
        foreach (var chunk in _chunks)
        {
            if (chunk.Type != "IDAT") result.Write(chunk.Bytes);
            else if (!inserted)
            {
                var data = compressed.ToArray();
                var idat = new byte[checked(data.Length + 12)];
                BinaryPrimitives.WriteUInt32BigEndian(idat, (uint)data.Length);
                "IDAT"u8.CopyTo(idat.AsSpan(4));
                data.CopyTo(idat, 8);
                uint crc = uint.MaxValue;
                foreach (var value in idat.AsSpan(4, data.Length + 4))
                {
                    crc ^= value;
                    for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
                }
                BinaryPrimitives.WriteUInt32BigEndian(idat.AsSpan(idat.Length - 4), ~crc);
                result.Write(idat);
                inserted = true;
            }
            if (result.Length > ImageContainer.MaximumFileBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        }
        return result.ToArray();
    }

    public byte[] EncodePalette(byte[] rgba, CancellationToken cancellationToken)
    {
        if (LossyBlockReason is not null || rgba.Length != checked(_width * _height * 4))
            throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var colors = new Dictionary<uint, byte>(); var indices = new byte[_width * _height];
        for (var i = 0; i < indices.Length; i++)
        {
            if (i % 16384 == 0) cancellationToken.ThrowIfCancellationRequested();
            var value = BinaryPrimitives.ReadUInt32BigEndian(rgba.AsSpan(i * 4, 4));
            if (!colors.TryGetValue(value, out var index))
            {
                if (colors.Count == 256) throw new ImageFailureException(ImageFailure.ValidationFailed);
                index = (byte)colors.Count; colors.Add(value, index);
            }
            indices[i] = index;
        }
        using var compressed = new MemoryStream();
        using (var deflater = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            for (var y = 0; y < _height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                deflater.WriteByte(0); deflater.Write(indices.AsSpan(y * _width, _width));
            }
        var palette = colors.OrderBy(c => c.Value).Select(c => c.Key).ToArray();
        using var result = new MemoryStream(); result.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var inserted = false;
        foreach (var chunk in _chunks)
        {
            if (chunk.Type == "IHDR")
            {
                var header = _header.ToArray(); header[9] = 3; WriteChunk("IHDR", header);
            }
            else if (chunk.Type != "IDAT") result.Write(chunk.Bytes);
            else if (!inserted)
            {
                WriteChunk("PLTE", palette.SelectMany(v => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8) }).ToArray());
                  var alphaLength = Array.FindLastIndex(palette, v => (byte)v != 255) + 1;
                  if (alphaLength > 0) WriteChunk("tRNS", palette.Take(alphaLength).Select(v => (byte)v).ToArray());
                WriteChunk("IDAT", compressed.ToArray()); inserted = true;
            }
            if (result.Length > ImageContainer.MaximumFileBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        }
        return result.ToArray();

        void WriteChunk(string type, byte[] data)
        {
            var chunk = new byte[checked(data.Length + 12)];
            BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length); Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4); data.CopyTo(chunk, 8);
            uint crc = uint.MaxValue;
            foreach (var value in chunk.AsSpan(4, data.Length + 4))
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
            }
            BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ~crc); result.Write(chunk);
        }
    }

    private void ReadRows(Action<byte[]> consume, CancellationToken cancellationToken)
    {
        using var compressed = new MemoryStream();
        foreach (var chunk in _chunks.Where(c => c.Type == "IDAT")) compressed.Write(chunk.Bytes, 8, chunk.Bytes.Length - 12);
        compressed.Position = 0;
        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        // PNG Adam7: each nonempty pass is filtered independently. Header equality in
        // RestoreAndValidate makes this pass-ordered digest comparable without deinterlacing.
        (int X, int Y, int Dx, int Dy)[] passes = _header[12] == 0
            ? [(0, 0, 1, 1)]
            : [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
               (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];
        foreach (var pass in passes)
        {
            if (_width <= pass.X || _height <= pass.Y) continue;
            var width = (_width - pass.X + pass.Dx - 1) / pass.Dx;
            var height = (_height - pass.Y + pass.Dy - 1) / pass.Dy;
            var previous = new byte[checked((int)(((long)width * _bitsPerPixel + 7) / 8))];
            var row = new byte[previous.Length];
            for (var y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filter = inflater.ReadByte();
                if (filter is < 0 or > 4) throw new InvalidDataException("Invalid PNG row filter.");
                inflater.ReadExactly(row);
                for (var x = 0; x < row.Length; x++)
                {
                    var left = x >= _pixelBytes ? row[x - _pixelBytes] : 0;
                    var above = previous[x];
                    var upperLeft = x >= _pixelBytes ? previous[x - _pixelBytes] : 0;
                    row[x] = unchecked((byte)(row[x] + (filter switch
                    {
                        0 => 0, 1 => left, 2 => above, 3 => (left + above) / 2,
                        _ => Paeth(left, above, upperLeft)
                    })));
                }
                // Padding is not a sample. Mask a copy only: unfiltering the next row
                // must still use the complete, unmodified preceding byte sequence.
                var unusedBits = (8 - width * _bitsPerPixel % 8) % 8;
                if (unusedBits == 0) consume(row);
                else
                {
                    var samples = row.ToArray();
                    samples[^1] &= (byte)(255 << unusedBits);
                    consume(samples);
                }
                (row, previous) = (previous, row);
            }
        }
        if (inflater.ReadByte() != -1) throw new InvalidDataException("PNG pixel stream exceeds its dimensions.");
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        var predictor = left + above - upperLeft;
        var dl = Math.Abs(predictor - left);
        var da = Math.Abs(predictor - above);
        var du = Math.Abs(predictor - upperLeft);
        return dl <= da && dl <= du ? left : da <= du ? above : upperLeft;
    }
}
