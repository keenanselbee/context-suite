using System.Buffers.Binary;
using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

public sealed record OggMetadataInventory(string Codec, int SampleRate, int Channels, ushort PreSkip,
    short OutputGain, long FinalGranule, VorbisCommentList Descriptions)
{
    public ImmutableDictionary<string, string> ConversionTags()
    {
        if (OutputGain != 0) throw new NotSupportedException("Opus playback gain needs an explicit conversion policy.");
        return AudioCommentConversion.Read(Descriptions.Comments);
    }
}

// Validate every page with bounded storage, retaining only one packet at a time.
// Codec setup/audio decoding remains the native engine's responsibility.
public static class OggMetadata
{
    public const int MaximumPages = 131072;
    public const int MaximumPacketBytes = 1024 * 1024;
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static async Task<OggMetadataInventory> ReadAsync(Stream source, CancellationToken token)
    {
        if (!source.CanRead || !source.CanSeek || source.Length is < 27 or > AudioFileSource.MaximumFileBytes)
            throw new InvalidDataException("Ogg inventory requires a bounded seekable input.");
        var originalPosition = source.Position;
        var page = new byte[65307];
        using var packet = new MemoryStream();
        uint serial = 0, sequence = 0;
        var pages = 0;
        var packets = 0;
        var pending = false;
        var ended = false;
        long granule = -1;
        OggMetadataInventory? inventory = null;
        try
        {
            token.ThrowIfCancellationRequested(); source.Position = 0;
            while (source.Position < source.Length)
            {
                token.ThrowIfCancellationRequested();
                if (ended) throw new NotSupportedException("Chained Ogg streams or trailing data need a separate conversion policy.");
                if (++pages > MaximumPages) throw new InvalidDataException("Ogg page count exceeds its budget.");
                await source.ReadExactlyAsync(page.AsMemory(0, 27), token);
                if (!page.AsSpan(0, 4).SequenceEqual("OggS"u8) || page[4] != 0 || (page[5] & ~7) != 0)
                    throw new InvalidDataException("Invalid Ogg page header.");
                var flags = page[5];
                if (((flags & 1) != 0) != pending || ((flags & 2) != 0) != (pages == 1))
                    throw new InvalidDataException("Ogg beginning or packet continuation flags disagree with framing.");
                var pageSerial = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(14));
                var pageSequence = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(18));
                if (pages == 1) serial = pageSerial;
                if (pageSerial != serial) throw new NotSupportedException("Multiplexed Ogg streams need a preservation policy.");
                if (pageSequence != sequence++) throw new InvalidDataException("Ogg pages are missing or out of sequence.");
                var segments = page[26];
                await source.ReadExactlyAsync(page.AsMemory(27, segments), token);
                var bodyLength = 0;
                for (var i = 0; i < segments; i++) bodyLength += page[27 + i];
                var body = 27 + segments;
                await source.ReadExactlyAsync(page.AsMemory(body, bodyLength), token);
                var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(22));
                page.AsSpan(22, 4).Clear();
                uint crc = 0;
                foreach (var value in page.AsSpan(0, body + bodyLength)) crc = (crc << 8) ^ CrcTable[(crc >> 24) ^ value];
                if (crc != expectedCrc) throw new InvalidDataException("Ogg page checksum does not match its bytes.");
                var pageGranule = BinaryPrimitives.ReadInt64LittleEndian(page.AsSpan(6));
                if (pageGranule < -1 || (pageGranule >= 0 && pageGranule < granule))
                    throw new InvalidDataException("Ogg granule positions move backwards or are invalid.");
                if (pageGranule >= 0) granule = pageGranule;
                var completedBeforePage = packets;
                for (var i = 0; i < segments; i++)
                {
                    var length = page[27 + i];
                    if (packet.Length + length > MaximumPacketBytes) throw new InvalidDataException("Ogg packet exceeds its byte budget.");
                    packet.Write(page, body, length); body += length;
                    pending = length == 255;
                    if (pending) continue;
                    var data = packet.GetBuffer().AsSpan(0, checked((int)packet.Length));
                    if (packets == 0)
                    {
                        if (pages != 1 || i != segments - 1 || pageGranule != 0)
                            throw new InvalidDataException("Ogg identification header must be alone on its first page.");
                        inventory = Identification(data);
                    }
                    else if (packets == 1)
                    {
                        var opus = inventory!.Codec == "opus";
                        var prefix = opus ? "OpusTags"u8 : "\x03vorbis"u8;
                        if (!data.StartsWith(prefix)) throw new InvalidDataException("Missing Ogg comment header.");
                        var textBytes = 0;
                        var descriptions = VorbisComments.Read(data[prefix.Length..], ref textBytes, out var consumed);
                        var trailing = data[(prefix.Length + consumed)..];
                        if (opus)
                        {
                            if (!trailing.IsEmpty && (trailing[0] & 1) != 0)
                                throw new NotSupportedException("Opus comment binary extensions need a preservation handler.");
                            if (i != segments - 1 || pageGranule != 0)
                                throw new InvalidDataException("Opus comment header must finish its own page.");
                        }
                        else if (trailing.Length != 1 || trailing[0] != 1)
                            throw new InvalidDataException("Vorbis comment framing is invalid or has trailing data.");
                        inventory = inventory with { Descriptions = descriptions };
                    }
                    else if (packets == 2 && inventory!.Codec == "vorbis")
                    {
                        if (data.Length <= 7 || !data.StartsWith("\x05vorbis"u8)) throw new InvalidDataException("Missing Vorbis setup header.");
                    }
                    else if (data.IsEmpty || (inventory!.Codec == "vorbis" && (data[0] & 1) != 0))
                        throw new InvalidDataException("Unexpected Ogg header or empty audio packet.");
                    packets++; packet.SetLength(0);
                }
                if (packets == completedBeforePage ? pageGranule != -1 : pageGranule < 0)
                    throw new InvalidDataException("Ogg granule position disagrees with packet completion.");
                if (inventory is not null && packets <= (inventory.Codec == "opus" ? 2 : 3) && packets > completedBeforePage && pageGranule != 0)
                    throw new InvalidDataException("Ogg header pages must have zero granule positions.");
                ended = (flags & 4) != 0;
                if (ended && (pending || pageGranule < 0)) throw new InvalidDataException("Ogg end page has no completed extent.");
            }
            if (!ended || inventory is null || packets < (inventory.Codec == "opus" ? 3 : 4) || granule < inventory.PreSkip)
                throw new InvalidDataException("Ogg stream is incomplete or contains no audio.");
            return inventory with { FinalGranule = granule };
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("Ogg page is truncated.", ex); }
        finally { source.Position = originalPosition; }
    }

    private static OggMetadataInventory Identification(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith("\x01vorbis"u8))
        {
            if (data.Length != 30 || BinaryPrimitives.ReadUInt32LittleEndian(data[7..]) != 0 || data[29] != 1 ||
                (data[28] & 15) < 6 || (data[28] >> 4) > 13 || (data[28] & 15) > (data[28] >> 4))
                throw new InvalidDataException("Invalid Vorbis identification header.");
            var rate = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
            if (rate is < 8000 or > 192000 || data[11] is < 1 or > 8) throw new NotSupportedException("Vorbis rate or channels exceed the conversion policy.");
            return new("vorbis", (int)rate, data[11], 0, 0, 0, new("", []));
        }
        if (!data.StartsWith("OpusHead"u8)) throw new NotSupportedException("This Ogg codec needs a metadata handler.");
        if (data.Length < 19 || data[8] != 1) throw new NotSupportedException("This Opus header version needs review.");
        var channels = data[9];
        if (channels is < 1 or > 8 || data[18] is not (0 or 1)) throw new NotSupportedException("This Opus channel mapping needs a conversion policy.");
        if (data[18] == 0)
        {
            if (channels > 2 || data.Length != 19) throw new InvalidDataException("Invalid mono/stereo Opus mapping.");
        }
        else
        {
            if (data.Length != 21 + channels || data[19] == 0 || data[20] > data[19] || data[19] + data[20] > 255)
                throw new InvalidDataException("Invalid Opus channel map extent.");
            foreach (var map in data[21..])
                if (map != 255 && map >= data[19] + data[20]) throw new InvalidDataException("Invalid Opus channel map index.");
        }
        return new("opus", 48000, channels, BinaryPrimitives.ReadUInt16LittleEndian(data[10..]),
            BinaryPrimitives.ReadInt16LittleEndian(data[16..]), 0, new("", []));
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var i = 0; i < table.Length; i++)
        {
            var value = (uint)i << 24;
            for (var bit = 0; bit < 8; bit++) value = (value << 1) ^ ((value & 0x80000000) != 0 ? 0x04c11db7u : 0);
            table[i] = value;
        }
        return table;
    }
}
