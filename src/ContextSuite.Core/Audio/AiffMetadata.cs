using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace ContextSuite.Core.Audio;

public sealed record AiffMetadataInventory(string Codec, int SampleRate, int Channels, int SampleBits,
    long SampleFrames, ImmutableDictionary<string, string> Tags, ImmutableArray<FlacMetadataBlock> Pictures,
    ImmutableArray<string> UnsupportedChunks)
{
    public void RequireConversionSupport()
    {
        if (!UnsupportedChunks.IsEmpty)
            throw new NotSupportedException("AIFF information needs a preservation handler: " + string.Join(", ", UnsupportedChunks));
    }
}

// Walk the complete FORM, seeking past sound bytes. Native decoding must still
// verify the declared codec, samples and output. Unknown chunks never authorize
// a conversion that silently drops their contents.
public static class AiffMetadata
{
    public const int MaximumChunks = 4096;
    public const int MaximumTextBytes = 256 * 1024;

    public static async Task<AiffMetadataInventory> ReadAsync(Stream source, CancellationToken token)
    {
        if (!source.CanRead || !source.CanSeek || source.Length is < 12 or > AudioFileSource.MaximumFileBytes)
            throw new InvalidDataException("AIFF inventory requires a bounded seekable input.");
        var position = source.Position;
        var common = Array.Empty<byte>();
        long sampleBytes = -1;
        var version = false;
        var chunks = 0;
        var textBytes = 0;
        var tags = new List<AudioComment>();
        var unsupported = new HashSet<string>(StringComparer.Ordinal);
        Id3TagInventory? id3 = null;
        try
        {
            token.ThrowIfCancellationRequested();
            source.Position = 0;
            var header = new byte[12];
            await source.ReadExactlyAsync(header, token);
            var compressed = header.AsSpan(8, 4).SequenceEqual("AIFC"u8);
            if (!header.AsSpan(0, 4).SequenceEqual("FORM"u8) ||
                !compressed && !header.AsSpan(8, 4).SequenceEqual("AIFF"u8) ||
                8L + BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)) != source.Length)
                throw new InvalidDataException("AIFF FORM extent is incomplete, unsupported or has trailing bytes.");
            while (source.Position < source.Length)
            {
                token.ThrowIfCancellationRequested();
                if (++chunks > MaximumChunks || source.Length - source.Position < 8)
                    throw new InvalidDataException("AIFF chunks exceed their count or framing budget.");
                await source.ReadExactlyAsync(header.AsMemory(0, 8), token);
                var kind = Encoding.ASCII.GetString(header, 0, 4);
                if (header.AsSpan(0, 4).IndexOfAnyExceptInRange((byte)32, (byte)126) >= 0)
                    throw new InvalidDataException("AIFF chunk identifier is invalid.");
                var size = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
                var end = source.Position + size;
                var paddedEnd = end + (size & 1);
                if (paddedEnd > source.Length) throw new InvalidDataException("AIFF chunk exceeds its FORM extent.");
                switch (kind)
                {
                    case "COMM":
                        if (common.Length != 0 || (compressed ? size is < 24 or > 278 : size != 18))
                            throw new InvalidDataException("AIFF common chunk is duplicated or unsupported.");
                        common = new byte[size]; await source.ReadExactlyAsync(common, token);
                        if (compressed && 22 + ((common[22] + 2) & ~1) != common.Length)
                            throw new InvalidDataException("AIFF-C compression name framing is invalid.");
                        break;
                    case "FVER":
                        if (!compressed || version || size != 4 || common.Length != 0)
                            throw new InvalidDataException("AIFF-C version is duplicated, out of order or unsupported.");
                        await source.ReadExactlyAsync(header.AsMemory(0, 4), token);
                        if (BinaryPrimitives.ReadUInt32BigEndian(header) != 0xa2805140)
                            throw new NotSupportedException("This AIFF-C version has no conversion policy.");
                        version = true; break;
                    case "SSND":
                        if (sampleBytes >= 0 || size < 8)
                            throw new InvalidDataException("AIFF sound chunk is duplicated or truncated.");
                        await source.ReadExactlyAsync(header.AsMemory(0, 8), token);
                        var offset = BinaryPrimitives.ReadUInt32BigEndian(header);
                        var blockSize = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
                        if (offset > size - 8) throw new InvalidDataException("AIFF sound offset exceeds its chunk.");
                        if (blockSize != 0) unsupported.Add("SSND/block alignment");
                        sampleBytes = size - 8L - offset; break;
                    case "NAME": case "AUTH": case "(c) ": case "ANNO":
                        if (size > MaximumTextBytes - textBytes)
                            throw new InvalidDataException("AIFF text exceeds its aggregate budget.");
                        textBytes += (int)size;
                        var text = new byte[size]; await source.ReadExactlyAsync(text, token);
                        if (text.Any(value => value > 126 || value < 32 && value is not (9 or 10 or 13)))
                        { unsupported.Add(kind + "/text encoding or controls"); break; }
                        var name = kind switch { "NAME" => "title", "AUTH" => "author", "(c) " => "copyright", _ => "comment" };
                        tags.Add(new(name, Encoding.ASCII.GetString(text))); break;
                    case "ID3 ":
                        if (id3 is not null || size is < 10 or > Mp3Metadata.MaximumTagBytes + 20)
                            throw new InvalidDataException("AIFF ID3 is duplicated or exceeds its tag budget.");
                        var data = new byte[size]; await source.ReadExactlyAsync(data, token);
                        id3 = Mp3Metadata.ReadId3Tag(data, true); break;
                    default: unsupported.Add(kind); break;
                }
                source.Position = paddedEnd;
            }
            if (common.Length == 0 || sampleBytes <= 0 || compressed && !version)
                throw new InvalidDataException("AIFF requires complete common, sound and applicable version chunks.");
            var channels = BinaryPrimitives.ReadUInt16BigEndian(common);
            var declaredFrames = BinaryPrimitives.ReadUInt32BigEndian(common.AsSpan(2));
            var bits = BinaryPrimitives.ReadUInt16BigEndian(common.AsSpan(6));
            var exponent = BinaryPrimitives.ReadUInt16BigEndian(common.AsSpan(8));
            var mantissa = BinaryPrimitives.ReadUInt64BigEndian(common.AsSpan(10));
            // Compare the complete 80-bit representation; rounding a fractional
            // rate to an integer would silently change timing.
            var shift = 16383 + 63 - exponent;
            if (exponent is 0 or >= 0x7fff || shift is < 0 or > 63 || (mantissa & (1UL << 63)) == 0 ||
                (mantissa & ((1UL << shift) - 1)) != 0)
                throw new NotSupportedException("AIFF requires an exact supported integer sample rate.");
            var rate = mantissa >> shift;
            if (rate is < 8000 or > 192000 || channels is < 1 or > 8 || declaredFrames == 0)
                throw new NotSupportedException("AIFF rate, channels or sample count is outside the conversion policy.");
            var compression = compressed ? Encoding.ASCII.GetString(common, 18, 4) : "NONE";
            var codec = (compression, bits) switch
            {
                ("NONE", 8) => "pcm_s8", ("NONE", 16) => "pcm_s16be",
                ("NONE", 24) => "pcm_s24be", ("NONE", 32) => "pcm_s32be",
                ("sowt", 16) => "pcm_s16le",
                ("fl32" or "FL32", 32) => "pcm_f32be", ("fl64" or "FL64", 64) => "pcm_f64be",
                ("alaw" or "ALAW", 8 or 16) => "pcm_alaw", ("ulaw" or "ULAW", 8 or 16) => "pcm_mulaw",
                ("ima4", 4 or 16) => "adpcm_ima_qt",
                _ => throw new NotSupportedException("This AIFF-C codec or precision needs a verified conversion policy.")
            };
            long frames;
            if (codec == "adpcm_ima_qt")
            {
                var block = 34 * channels;
                if (sampleBytes % block != 0) throw new InvalidDataException("AIFF IMA4 packet extent is incomplete.");
                frames = checked(sampleBytes / block * 64);
                // Common writers store packet count here. Do not infer partial
                // final packets or discard a decoded tail to match a declaration.
                if (declaredFrames != sampleBytes / block && declaredFrames != frames)
                    throw new NotSupportedException("AIFF IMA4 declared duration needs a verified framing policy.");
            }
            else
            {
                var width = codec is "pcm_alaw" or "pcm_mulaw" ? 1 : bits / 8;
                var block = channels * width;
                if (sampleBytes % block != 0 || sampleBytes / block != declaredFrames)
                    throw new InvalidDataException("AIFF sound extent and declared sample frames disagree.");
                frames = declaredFrames;
            }
            if (id3 is not null) tags.AddRange(id3.Tags.Select(tag => new AudioComment(tag.Key, tag.Value)));
            var comments = AudioCommentConversion.Read(tags, false);
            return new(codec, (int)rate, channels, bits, frames, comments, id3?.Pictures ?? [],
                unsupported.Order(StringComparer.Ordinal).ToImmutableArray());
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("AIFF chunk data is truncated.", error); }
        finally { source.Position = position; }
    }
}
