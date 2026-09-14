using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace ContextSuite.Core.Audio;

public sealed record WaveInfoTag(string ChunkId, string Name, string Value);
public sealed record WaveMetadataInventory(int SampleRate, int Channels, int SampleBits, bool FloatingPoint,
    long SampleFrames, ImmutableArray<WaveInfoTag> Tags, ImmutableArray<string> UnsupportedChunks)
{
    public ImmutableArray<FlacMetadataBlock> Pictures { get; init; } = [];

    public void RequireConversionSupport(IReadOnlyDictionary<string, string> probedTags)
    {
        if (!UnsupportedChunks.IsEmpty)
            throw new NotSupportedException("WAV information needs a preservation handler: " + string.Join(", ", UnsupportedChunks));
        foreach (var tag in Tags)
            if (tag.Name != "encoder" && (!probedTags.TryGetValue(tag.Name, out var value) || value != tag.Value))
                throw new InvalidDataException("The audio probe did not retain the WAV information inventory.");
    }
}

// Inventory complete RIFF framing while seeking past samples. This is not an
// audio decoder: the encoder must still validate decoded samples and output tags.
public static class WaveMetadata
{
    public const int MaximumChunks = 4096;
    public const int MaximumMetadataBytes = 256 * 1024;
    private static readonly Dictionary<string, string> InfoNames = new(StringComparer.Ordinal)
    {
        ["INAM"] = "title", ["IART"] = "artist", ["IPRD"] = "album", ["ICMT"] = "comment",
        ["ICRD"] = "date", ["IGNR"] = "genre", ["ILNG"] = "language", ["ITRK"] = "track", ["ICOP"] = "copyright", ["ISFT"] = "encoder"
    };

    public static async Task<WaveMetadataInventory> ReadAsync(Stream source, CancellationToken token, bool preserveId3 = false)
    {
        if (!source.CanRead || !source.CanSeek || source.Length is < 12 or > AudioFileSource.MaximumFileBytes)
            throw new InvalidDataException("WAV inventory requires a bounded seekable input.");
        var originalPosition = source.Position;
        var tags = ImmutableArray.CreateBuilder<WaveInfoTag>();
        var unsupported = new HashSet<string>(StringComparer.Ordinal);
        var format = Array.Empty<byte>();
        long dataBytes = -1;
        uint? factFrames = null;
        var chunks = 0;
        var metadataBytes = 0;
        Id3TagInventory? id3 = null;
        try
        {
            token.ThrowIfCancellationRequested();
            source.Position = 0;
            var header = new byte[12];
            await source.ReadExactlyAsync(header, token);
            if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !header.AsSpan(8, 4).SequenceEqual("WAVE"u8) ||
                (long)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) + 8 != source.Length)
                throw new InvalidDataException("WAV RIFF extent is incomplete, unsupported or has trailing bytes.");
            while (source.Position < source.Length)
            {
                token.ThrowIfCancellationRequested();
                if (++chunks > MaximumChunks || source.Length - source.Position < 8)
                    throw new InvalidDataException("WAV chunks exceed their count or framing budget.");
                await source.ReadExactlyAsync(header.AsMemory(0, 8), token);
                var id = FourCc(header.AsSpan(0, 4));
                var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
                var end = source.Position + size;
                var paddedEnd = end + (size & 1);
                if (paddedEnd > source.Length) throw new InvalidDataException("A WAV chunk exceeds its RIFF extent.");
                switch (id)
                {
                    case "fmt ":
                        if (format.Length != 0 || dataBytes >= 0 || size is not (16 or 18 or 40))
                            throw new InvalidDataException("WAV format framing is duplicated, out of order or unsupported.");
                        format = new byte[size]; await source.ReadExactlyAsync(format, token); break;
                    case "data":
                        if (format.Length == 0 || dataBytes >= 0 || size == 0) throw new InvalidDataException("WAV requires one nonempty sample chunk after its format.");
                        dataBytes = size; break;
                    case "fact":
                        if (factFrames is not null || size != 4) throw new InvalidDataException("WAV fact framing is duplicated or unsupported.");
                        await source.ReadExactlyAsync(header.AsMemory(0, 4), token);
                        factFrames = BinaryPrimitives.ReadUInt32LittleEndian(header); break;
                    case "LIST":
                        if (size < 4) throw new InvalidDataException("WAV LIST is missing its type.");
                        await source.ReadExactlyAsync(header.AsMemory(0, 4), token);
                        var type = FourCc(header.AsSpan(0, 4));
                        if (type != "INFO") { unsupported.Add("LIST/" + type); break; }
                        if (size > MaximumMetadataBytes - metadataBytes) throw new InvalidDataException("WAV INFO exceeds its metadata budget.");
                        metadataBytes += (int)size;
                        var info = new byte[size - 4]; await source.ReadExactlyAsync(info, token);
                        ReadInfo(info, tags, unsupported, ref chunks); break;
                    case "id3 ": case "ID3 ":
                        if (!preserveId3) { unsupported.Add(id); break; }
                        if (id3 is not null || size is < 10 or > Mp3Metadata.MaximumTagBytes + 20)
                            throw new InvalidDataException("WAV ID3 is duplicated or exceeds its tag budget.");
                        var tag = new byte[size]; await source.ReadExactlyAsync(tag, token);
                        id3 = Mp3Metadata.ReadId3Tag(tag, true); break;
                    case "JUNK": case "PAD ": break;
                    default: unsupported.Add(id); break;
                }
                source.Position = paddedEnd;
            }
            if (format.Length == 0 || dataBytes <= 0) throw new InvalidDataException("WAV format or samples are missing.");
            var code = BinaryPrimitives.ReadUInt16LittleEndian(format);
            var channels = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(2));
            var rate = BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(4));
            var byteRate = BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(8));
            var align = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(12));
            var bits = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(14));
            if (code == 0xfffe)
            {
                if (format.Length != 40 || BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(16)) != 22)
                    throw new InvalidDataException("WAV extensible format is incomplete.");
                var validBits = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(18));
                if (validBits != 0 && validBits != bits) unsupported.Add("fmt /valid precision differs from storage");
                var mask = BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(20));
                if (mask != 0 && (System.Numerics.BitOperations.PopCount(mask) != channels || (mask & ~0x3ffffu) != 0))
                    throw new InvalidDataException("WAV speaker mask disagrees with its channels or uses reserved positions.");
                if ((channels == 1 && mask is not (0 or 4)) || (channels == 2 && mask is not (0 or 3)))
                    unsupported.Add("fmt /nonstandard mono or stereo speaker positions");
                var subformat = new Guid(format.AsSpan(24, 16));
                code = subformat == new Guid("00000001-0000-0010-8000-00aa00389b71") ? (ushort)1 :
                    subformat == new Guid("00000003-0000-0010-8000-00aa00389b71") ? (ushort)3 : (ushort)0;
            }
            else if (format.Length != 16 && (format.Length != 18 || BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(16)) != 0))
                throw new InvalidDataException("WAV format extensions need a preservation handler.");
            if (code is not (1 or 3) || channels is < 1 or > 8 || rate is < 8000 or > 192000 ||
                (code == 1 ? bits is not (8 or 16 or 24 or 32) : bits is not (32 or 64)))
                throw new NotSupportedException("WAV inventory currently supports bounded PCM and IEEE float formats.");
            if (align != channels * (bits / 8) || byteRate != (long)rate * align || dataBytes % align != 0 ||
                (factFrames is { } frames && frames != dataBytes / align))
                throw new InvalidDataException("WAV sample framing or fact count disagrees with its format.");
            foreach (var duplicate in tags.GroupBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
                unsupported.Add("LIST/INFO/duplicate " + duplicate.Key);
            if (id3 is not null)
                foreach (var tag in id3.Tags)
                {
                    if (tags.Any(info => info.Name.Equals(tag.Key, StringComparison.OrdinalIgnoreCase)))
                        unsupported.Add("INFO/ID3/duplicate " + tag.Key);
                    tags.Add(new("ID3", tag.Key, tag.Value));
                }
            return new((int)rate, channels, bits, code == 3, dataBytes / align, tags.ToImmutable(), unsupported.Order(StringComparer.Ordinal).ToImmutableArray())
            { Pictures = id3?.Pictures ?? [] };
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("WAV chunk data is truncated.", error); }
        finally { source.Position = originalPosition; }
    }

    private static void ReadInfo(ReadOnlySpan<byte> bytes, ImmutableArray<WaveInfoTag>.Builder tags, HashSet<string> unsupported, ref int chunks)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            if (++chunks > MaximumChunks || bytes.Length - offset < 8) throw new InvalidDataException("WAV INFO chunk framing exceeds its budget.");
            var id = FourCc(bytes.Slice(offset, 4));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);
            offset += 8;
            if (size > bytes.Length - offset || (long)size + (size & 1) > bytes.Length - offset)
                throw new InvalidDataException("WAV INFO value is truncated.");
            var data = bytes.Slice(offset, (int)size);
            offset += (int)size + (int)(size & 1);
            if (!InfoNames.TryGetValue(id, out var name)) { unsupported.Add("LIST/INFO/" + id); continue; }
            var terminator = data.IndexOf((byte)0);
            if (terminator < 0 || data[terminator..].IndexOfAnyExcept((byte)0) >= 0)
                throw new InvalidDataException("WAV INFO text is not correctly terminated.");
            data = data[..terminator];
            var unsupportedText = false;
            foreach (var value in data)
                if (value > 126 || (value < 32 && value is not (9 or 10 or 13))) { unsupportedText = true; break; }
            if (unsupportedText)
            { unsupported.Add("LIST/INFO/" + id + " text encoding or controls"); continue; }
            if (!data.IsEmpty) tags.Add(new(id, name, Encoding.ASCII.GetString(data)));
        }
    }

    private static string FourCc(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4 || bytes.ContainsAnyExceptInRange((byte)32, (byte)126)) throw new InvalidDataException("WAV chunk identifiers must be printable FourCCs.");
        return Encoding.ASCII.GetString(bytes);
    }
}
