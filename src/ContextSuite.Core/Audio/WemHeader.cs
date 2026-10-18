using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace ContextSuite.Core.Audio;

public enum WemCodec { Pcm16, Vorbis, Unsupported }
public enum WemCodebooks { Unknown, FullInline }

public sealed record WemLoop(long StartSample, long EndSampleExclusive);
public sealed record WemVorbisInfo(long SeekTableBytes, long AudioOffset, WemCodebooks Codebooks,
    uint? RawCodebookHash);

public sealed record WemHeader(ushort CodecTag, string CodecName, WemCodec Codec, bool BigEndian,
    int Channels, int SampleRate, int BitDepth, long? SampleCount, WemLoop? Loop,
    long DataOffset, long DataSize, WemVorbisInfo? Vorbis)
{
    public ImmutableArray<byte> Hash { get; init; } = [];
    public ImmutableArray<string> UnsupportedChunks { get; init; } = [];
    public bool IsHeaderLayoutRecognized => Codec is WemCodec.Pcm16 or WemCodec.Vorbis;
    public bool HasUnrecognizedChunks => !UnsupportedChunks.IsEmpty;

    public const int MaximumChunks = 4096;
    public const int MaximumMetadataBytes = 256 * 1024;

    // Wwise RIFF fields, smpl loop endpoints and Vorbis version layouts follow
    // vgmstream/src/meta/wwise.c (parse_wwise and its PCM/VORBIS cases):
    // https://github.com/vgmstream/vgmstream/blob/71e2361042531fe767fb98300cf8c1ee95e539a0/src/meta/wwise.c
    // This inventory does not decode or validate Vorbis setup/codebooks.
    public static async Task<WemHeader> ReadAsync(Stream source, CancellationToken token = default)
    {
        if (!source.CanRead || !source.CanSeek || source.Length is < 12 or > AudioFileSource.MaximumFileBytes)
            throw new InvalidDataException("WEM inspection requires a bounded seekable input.");
        var originalPosition = source.Position;
        try
        {
            token.ThrowIfCancellationRequested();
            source.Position = 0;
            var header = new byte[12];
            await source.ReadExactlyAsync(header, token);
            var bigEndian = header.AsSpan(0, 4).SequenceEqual("RIFX"u8);
            if (!bigEndian && !header.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
                !header.AsSpan(8, 4).SequenceEqual("WAVE"u8) ||
                8L + U32(header.AsSpan(4), bigEndian) != source.Length)
                throw new InvalidDataException("WEM RIFF extent is invalid, incomplete or contains trailing bytes.");

            byte[]? format = null;
            byte[]? vorb = null;
            byte[]? smpl = null;
            byte[]? hash = null;
            long dataOffset = -1, dataSize = -1;
            var chunks = 0;
            var metadataBytes = 0;
            var unsupported = new HashSet<string>(StringComparer.Ordinal);
            while (source.Position < source.Length)
            {
                token.ThrowIfCancellationRequested();
                if (++chunks > MaximumChunks || source.Length - source.Position < 8)
                    throw new InvalidDataException("WEM chunks exceed their count or framing budget.");
                await source.ReadExactlyAsync(header.AsMemory(0, 8), token);
                if (header.AsSpan(0, 4).IndexOfAnyExceptInRange((byte)32, (byte)126) >= 0)
                    throw new InvalidDataException("WEM chunk identifier is invalid.");
                var id = Encoding.ASCII.GetString(header, 0, 4);
                var size = U32(header.AsSpan(4), bigEndian);
                var start = source.Position;
                var end = start + size;
                var paddedEnd = end + (size & 1);
                // Wwise commonly omits RIFF's pad byte after a final odd-sized
                // data chunk. Its declared RIFF extent still matches the file.
                if (id == "data" && end == source.Length) paddedEnd = end;
                if (paddedEnd > source.Length)
                    throw new InvalidDataException("WEM chunk exceeds its RIFF extent (possibly a prefetch fragment).");
                switch (id)
                {
                    case "fmt ":
                        if (format is not null || dataOffset >= 0 || size is < 16 or > MaximumMetadataBytes)
                            throw new InvalidDataException("WEM format is duplicated, out of order or too large.");
                        if (size > MaximumMetadataBytes - metadataBytes)
                            throw new InvalidDataException("WEM format exceeds its aggregate metadata budget.");
                        metadataBytes += (int)size;
                        format = new byte[size];
                        await source.ReadExactlyAsync(format, token);
                        break;
                    case "vorb":
                        if (vorb is not null || size is < 40 or > MaximumMetadataBytes || size > MaximumMetadataBytes - metadataBytes)
                            throw new InvalidDataException("WEM Vorbis metadata is duplicated or exceeds its budget.");
                        metadataBytes += (int)size;
                        vorb = new byte[size];
                        await source.ReadExactlyAsync(vorb, token);
                        break;
                    case "smpl":
                        if (smpl is not null || size is < 36 or > MaximumMetadataBytes || size > MaximumMetadataBytes - metadataBytes)
                            throw new InvalidDataException("WEM loop metadata is duplicated or exceeds its budget.");
                        metadataBytes += (int)size;
                        smpl = new byte[size];
                        await source.ReadExactlyAsync(smpl, token);
                        break;
                    case "hash":
                        if (hash is not null || size != 16 || size > MaximumMetadataBytes - metadataBytes)
                            throw new InvalidDataException("WEM hash chunk is duplicated or has an unexpected size.");
                        metadataBytes += 16;
                        hash = new byte[16];
                        await source.ReadExactlyAsync(hash, token);
                        break;
                    case "data":
                        if (format is null || dataOffset >= 0 || size == 0)
                            throw new InvalidDataException("WEM requires one nonempty data chunk after its format.");
                        dataOffset = start;
                        dataSize = size;
                        break;
                    case "JUNK": case "junk": case "PAD ": break;
                    default: unsupported.Add(id); break;
                }
                source.Position = paddedEnd;
            }
            if (format is null || dataOffset < 0)
                throw new InvalidDataException("WEM format or data is missing.");

            var tag = U16(format, bigEndian);
            var channels = U16(format.AsSpan(2), bigEndian);
            var rate = U32(format.AsSpan(4), bigEndian);
            var byteRate = U32(format.AsSpan(8), bigEndian);
            var block = U16(format.AsSpan(12), bigEndian);
            var bits = U16(format.AsSpan(14), bigEndian);
            if (channels is < 1 or > 8 || rate is < 8000 or > 192000)
                throw new NotSupportedException("WEM channels or sample rate exceed the inspection policy.");
            if (format.Length > 16 && (format.Length < 18 || U16(format.AsSpan(16), bigEndian) != format.Length - 18))
                throw new InvalidDataException("WEM format extension length is inconsistent.");

            var codecName = tag switch
            {
                0x0001 => "pcm", 0xfffe => "wwise_pcm", 0xffff => "wwise_vorbis",
                0x0002 or 0x0069 or 0x8311 => "wwise_adpcm", 0x0161 or 0x0162 => "xwma",
                0x0165 or 0x0166 => "xma2", 0x3039 or 0x3040 or 0x3041 => "wwise_opus",
                0xaac0 => "aac", 0xfff0 => "dsp", 0xfffb => "hevag", 0xfffc => "atrac9",
                _ => "unknown"
            };
            var codec = WemCodec.Unsupported;
            long? samples = null;
            WemVorbisInfo? vorbis = null;
            if (vorb is not null && tag != 0xffff)
                throw new InvalidDataException("Vorbis metadata conflicts with the format tag.");
            if (tag is 0xfffe or 0x0001 && format.Length == 24 && U16(format.AsSpan(16), bigEndian) == 6 && bits == 16)
            {
                if (block != channels * 2 || byteRate != (long)rate * block || dataSize % block != 0)
                    throw new InvalidDataException("Wwise PCM sample framing disagrees with its format.");
                codec = WemCodec.Pcm16;
                samples = dataSize / block;
                codecName = bigEndian ? "pcm_s16be" : "pcm_s16le";
            }
            else if (tag == 0xffff)
            {
                if (bits != 0 || block != 0)
                    throw new InvalidDataException("Wwise Vorbis format has unexpected PCM framing.");
                if (vorb is null && format.Length < 24)
                    throw new InvalidDataException("Wwise Vorbis format extension is incomplete.");
                var extra = vorb is not null ? vorb.AsSpan() : format.AsSpan(24);
                var extraSize = vorb is not null ? vorb.Length : U16(format.AsSpan(16), bigEndian);
                var offset = extraSize switch
                {
                    0x28 or 0x2c or 0x32 or 0x34 => 0x18,
                    0x2a or 0x30 => 0x10,
                    _ => -1
                };
                if (offset >= 0 && extra.Length >= offset + 8)
                {
                    var declaredSamples = bigEndian ? BinaryPrimitives.ReadInt32BigEndian(extra) : BinaryPrimitives.ReadInt32LittleEndian(extra);
                    var seekBytes = U32(extra.Slice(offset), bigEndian);
                    var audioOffset = U32(extra.Slice(offset + 4), bigEndian);
                    if (declaredSamples <= 0 || seekBytes > audioOffset || audioOffset > dataSize)
                        throw new InvalidDataException("Wwise Vorbis sample count or data offsets are invalid.");
                    // This raw field is tentatively called uHashCodebook by Wwise
                    // format research; it does not identify a proven library.
                    var hashOffset = extraSize is 0x28 or 0x2a or 0x2c or 0x30 ? 0x24 : 0x2c;
                    uint? rawCodebookHash = extra.Length >= hashOffset + 4 ?
                        U32(extra.Slice(hashOffset), bigEndian) : null;
                    samples = declaredSamples;
                    vorbis = new(seekBytes, audioOffset,
                        extraSize is 0x28 or 0x2c ? WemCodebooks.FullInline : WemCodebooks.Unknown,
                        rawCodebookHash);
                    codec = WemCodec.Vorbis;
                }
            }
            WemLoop? loop = null;
            if (smpl is not null)
            {
                var loops = U32(smpl.AsSpan(28), bigEndian);
                var samplerBytes = U32(smpl.AsSpan(32), bigEndian);
                if (samplerBytes != 0 || loops > 1 ||
                    loops == 0 && smpl.Length != 36 ||
                    loops == 1 && (smpl.Length is not (52 or 56 or 60) || U32(smpl.AsSpan(40), bigEndian) != 0 ||
                        smpl.Length >= 56 && U32(smpl.AsSpan(52), bigEndian) != 0 ||
                        smpl.Length >= 60 && U32(smpl.AsSpan(56), bigEndian) != 0))
                    throw new NotSupportedException("WEM loop metadata requires one forward loop.");
                if (loops == 1)
                {
                    var start = U32(smpl.AsSpan(44), bigEndian);
                    var end = (long)U32(smpl.AsSpan(48), bigEndian) + 1;
                    if (start >= end || samples is { } count && end > count)
                        throw new InvalidDataException("WEM loop extent exceeds its sample count.");
                    loop = new(start, end);
                }
            }
            return new(tag, codecName, codec, bigEndian, channels, (int)rate, bits, samples, loop,
                dataOffset, dataSize, vorbis)
            { Hash = hash is null ? [] : ImmutableArray.CreateRange(hash),
                UnsupportedChunks = unsupported.Order(StringComparer.Ordinal).ToImmutableArray() };
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("WEM header is truncated.", error); }
        finally { source.Position = originalPosition; }
    }

    private static ushort U16(ReadOnlySpan<byte> bytes, bool bigEndian) => bigEndian ?
        BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    private static uint U32(ReadOnlySpan<byte> bytes, bool bigEndian) => bigEndian ?
        BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
}
