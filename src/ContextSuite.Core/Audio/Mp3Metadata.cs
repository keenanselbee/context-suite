using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace ContextSuite.Core.Audio;

public sealed record Mp3MetadataInventory(int SampleRate, int Channels, int AudioFrames, long AudioOffset,
    ImmutableDictionary<string, string> Tags);

// Inventory tags and walk every MPEG Layer III frame boundary, seeking past
// compressed samples. Native decoding still owns audio validity and gapless trim.
public static class Mp3Metadata
{
    public const int MaximumTagBytes = 2 * 1024 * 1024;
    public const int MaximumTagFrames = 4096;
    public const int MaximumAudioFrames = 1000000;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly UnicodeEncoding Utf16Le = new(false, false, true);
    private static readonly UnicodeEncoding Utf16Be = new(true, false, true);
    private static readonly int[] Rates = [44100, 48000, 32000];
    private static readonly int[] Mpeg1Bitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] LowRateBitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["TIT2"] = "title", ["TPE1"] = "artist", ["TPE2"] = "album_artist", ["TALB"] = "album",
        ["TRCK"] = "track", ["TPOS"] = "disc", ["TCOM"] = "composer", ["TCOP"] = "copyright",
        ["TENC"] = "encoded_by", ["TPUB"] = "publisher", ["TLAN"] = "language", ["TPE3"] = "performer",
        ["TSSE"] = "encoder", ["TDRC"] = "date", ["TYER"] = "date", ["TSST"] = "disc_subtitle",
        ["TIT1"] = "grouping", ["TCON"] = "genre"
    };

    public static async Task<Mp3MetadataInventory> ReadAsync(Stream source, CancellationToken token)
    {
        if (!source.CanRead || !source.CanSeek || source.Length is < 4 or > AudioFileSource.MaximumFileBytes)
            throw new InvalidDataException("MP3 inventory requires a bounded seekable input.");
        var position = source.Position;
        try
        {
            token.ThrowIfCancellationRequested(); source.Position = 0;
            var header = new byte[10]; await source.ReadExactlyAsync(header.AsMemory(0, 3), token);
            var comments = ImmutableArray<AudioComment>.Empty;
            if (header.AsSpan(0, 3).SequenceEqual("ID3"u8))
            {
                await source.ReadExactlyAsync(header.AsMemory(3, 7), token);
                var version = header[3];
                if (version is not (3 or 4) || header[4] != 0) throw new NotSupportedException("This ID3 version needs a metadata handler.");
                var permittedFlags = version == 3 ? 0x80 : 0x90;
                if ((header[5] & ~permittedFlags) != 0) throw new NotSupportedException("Extended or experimental ID3 headers need a preservation handler.");
                var length = Synchsafe(header.AsSpan(6));
                if (length > MaximumTagBytes || length > source.Length - source.Position) throw new InvalidDataException("ID3 tag exceeds its extent or byte budget.");
                var payload = new byte[length]; await source.ReadExactlyAsync(payload, token);
                comments = ReadFrames(payload, version, (header[5] & 128) != 0);
                if ((header[5] & 16) != 0)
                {
                    var footer = new byte[10]; await source.ReadExactlyAsync(footer, token);
                    if (!footer.AsSpan(0, 3).SequenceEqual("3DI"u8) || !footer.AsSpan(3).SequenceEqual(header.AsSpan(3)))
                        throw new InvalidDataException("ID3 footer does not match its header.");
                }
            }
            else source.Position = 0;
            var audioOffset = source.Position;
            var count = 0; var rate = 0; var channels = 0;
            while (source.Position < source.Length)
            {
                token.ThrowIfCancellationRequested();
                if (++count > MaximumAudioFrames) throw new InvalidDataException("MP3 frame count exceeds its budget.");
                var start = source.Position;
                await source.ReadExactlyAsync(header.AsMemory(0, 4), token);
                if (header.AsSpan(0, 3).SequenceEqual("TAG"u8) || header.AsSpan(0, 3).SequenceEqual("ID3"u8) || header.AsSpan(0, 4).SequenceEqual("APET"u8))
                    throw new NotSupportedException("Trailing ID3/APE metadata needs an explicit preservation handler.");
                var bits = BinaryPrimitives.ReadUInt32BigEndian(header);
                var version = (int)((bits >> 19) & 3); var layer = (bits >> 17) & 3;
                var bitrateIndex = (int)((bits >> 12) & 15); var rateIndex = (int)((bits >> 10) & 3);
                if ((bits & 0xffe00000) != 0xffe00000 || version == 1 || layer != 1 || bitrateIndex == 15 || rateIndex == 3)
                    throw new InvalidDataException("MP3 frame header is invalid or the stream has undeclared data.");
                if (bitrateIndex == 0) throw new NotSupportedException("Free-format MP3 needs a frame-boundary policy.");
                if ((bits & 3) != 0 || (bits & 8) != 0)
                    throw new NotSupportedException("MP3 emphasis or copyright flags need a conversion preservation policy.");
                var frameRate = Rates[rateIndex] / (version == 3 ? 1 : version == 2 ? 2 : 4);
                var frameChannels = ((bits >> 6) & 3) == 3 ? 1 : 2;
                var bitrate = (version == 3 ? Mpeg1Bitrates : LowRateBitrates)[bitrateIndex];
                var bytes = (version == 3 ? 144000 : 72000) * bitrate / frameRate + (int)((bits >> 9) & 1);
                if (bytes < 4 || bytes > source.Length - start) throw new InvalidDataException("MP3 audio frame is truncated.");
                if (count == 1) { rate = frameRate; channels = frameChannels; }
                else if (frameRate != rate || frameChannels != channels) throw new NotSupportedException("MP3 rate/channel changes need a conversion policy.");
                source.Position = start + bytes;
            }
            if (count == 0) throw new InvalidDataException("MP3 contains no audio frames.");
            return new(rate, channels, count, audioOffset, AudioCommentConversion.Read(comments));
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("MP3 tag or frame is truncated.", ex); }
        finally { source.Position = position; }
    }

    private static ImmutableArray<AudioComment> ReadFrames(byte[] payload, int version, bool unsynchronised)
    {
        if (version == 3 && unsynchronised) payload = RestoreUnsynchronisation(payload);
        var comments = ImmutableArray.CreateBuilder<AudioComment>(); var offset = 0; var count = 0; var textBytes = 0;
        while (offset < payload.Length)
        {
            if (payload[offset] == 0)
            {
                if (payload.AsSpan(offset).IndexOfAnyExcept((byte)0) >= 0) throw new InvalidDataException("ID3 padding contains nonzero data.");
                break;
            }
            if (++count > MaximumTagFrames || offset > payload.Length - 10) throw new InvalidDataException("ID3 frame extent or count is invalid.");
            var frame = payload.AsSpan(offset, 10);
            foreach (var value in frame[..4])
                if (value is not (>= 65 and <= 90) and not (>= 48 and <= 57)) throw new InvalidDataException("Invalid ID3 frame identifier.");
            var id = Encoding.ASCII.GetString(frame[..4]);
            var length = version == 4 ? (uint)Synchsafe(frame[4..8]) : BinaryPrimitives.ReadUInt32BigEndian(frame[4..8]);
            var flags = BinaryPrimitives.ReadUInt16BigEndian(frame[8..]);
            if ((version == 3 && flags != 0) || (version == 4 && (flags & ~3) != 0))
                throw new NotSupportedException("ID3 compression, encryption, grouping or status flags need a preservation handler.");
            offset += 10;
            if (length == 0 || length > payload.Length - offset) throw new InvalidDataException("ID3 frame is empty or truncated.");
            var data = payload.AsSpan(offset, checked((int)length)); offset += (int)length;
            byte[]? restored = null;
            if (version == 4 && (unsynchronised || (flags & 2) != 0)) { restored = RestoreUnsynchronisation(data); data = restored; }
            if (version == 4 && (flags & 1) != 0)
            {
                if (data.Length < 4 || Synchsafe(data[..4]) != data.Length - 4) throw new InvalidDataException("ID3 data length indicator disagrees with its content.");
                data = data[4..];
            }
            if (data.Length == 0) throw new InvalidDataException("ID3 frame has no value.");
            var encoding = data[0];
            if (encoding > (version == 3 ? 1 : 3)) throw new NotSupportedException("ID3 text encoding is not permitted by this version.");
            string key, valueText;
            if (id == "TXXX")
            {
                var text = Decode(data[1..], encoding, ref textBytes, false);
                var separator = text.IndexOf('\0');
                if (separator <= 0) throw new InvalidDataException("ID3 custom text lacks its description delimiter.");
                key = text[..separator]; valueText = text[(separator + 1)..];
                if (encoding == 1 && valueText.StartsWith('\ufeff')) valueText = valueText[1..];
                valueText = SingleValue(valueText);
                if (key.Any(character => character < 32 || character > 125)) throw new NotSupportedException("ID3 custom field names need a mapping policy.");
            }
            else if (id == "COMM")
            {
                if (data.Length < 5 || !data.Slice(1, 3).SequenceEqual("und"u8))
                    throw new NotSupportedException("Language-specific ID3 comments need a preservation mapping.");
                var text = Decode(data[4..], encoding, ref textBytes, false);
                if (!text.StartsWith('\0')) throw new NotSupportedException("Named ID3 comments need a preservation mapping.");
                key = "comment"; valueText = text[1..];
                if (encoding == 1 && valueText.StartsWith('\ufeff')) valueText = valueText[1..];
                valueText = SingleValue(valueText);
            }
            else if (Names.TryGetValue(id, out var name))
            {
                key = name; valueText = Decode(data[1..], encoding, ref textBytes, true);
                if (id == "TCON" && (valueText.All(char.IsAsciiDigit) || valueText.StartsWith('(') || valueText is "RX" or "CR"))
                    throw new NotSupportedException("Numeric ID3 genre forms need a canonical genre mapping.");
            }
            else throw new NotSupportedException("ID3 frame needs a preservation handler: " + id);
            comments.Add(new(key, valueText));
        }
        return comments.ToImmutable();
    }

    private static string Decode(ReadOnlySpan<byte> data, byte encoding, ref int textBytes, bool single)
    {
        if (data.Length > VorbisComments.MaximumTextBytes - textBytes) throw new InvalidDataException("ID3 text exceeds its byte budget.");
        textBytes += data.Length;
        try
        {
            string text;
            if (encoding == 0) text = Encoding.Latin1.GetString(data);
            else if (encoding == 3) text = Utf8.GetString(data);
            else if (encoding == 2) text = Utf16Be.GetString(data);
            else
            {
                if (data.StartsWith(new byte[] { 255, 254 })) text = Utf16Le.GetString(data[2..]);
                else if (data.StartsWith(new byte[] { 254, 255 })) text = Utf16Be.GetString(data[2..]);
                else throw new InvalidDataException("ID3 UTF-16 text requires a byte-order mark.");
            }
            return single ? SingleValue(text) : text;
        }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("ID3 text has invalid Unicode encoding.", ex); }
    }
    private static string SingleValue(string text)
    {
        text = text.TrimEnd('\0');
        if (text.Contains('\0')) throw new NotSupportedException("Multiple ID3 text values need a preservation mapping.");
        return text;
    }
    private static int Synchsafe(ReadOnlySpan<byte> data)
    {
        if (data.Length != 4 || data.ContainsAnyExceptInRange((byte)0, (byte)127)) throw new InvalidDataException("Invalid ID3 synchsafe size.");
        return (data[0] << 21) | (data[1] << 14) | (data[2] << 7) | data[3];
    }
    private static byte[] RestoreUnsynchronisation(ReadOnlySpan<byte> data)
    {
        using var restored = new MemoryStream(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            restored.WriteByte(data[i]);
            if (data[i] != 255) continue;
            if (i + 1 < data.Length && data[i + 1] == 0)
            {
                if (i + 2 < data.Length && data[i + 2] is > 0 and < 224) throw new InvalidDataException("Invalid ID3 unsynchronisation escape.");
                i++;
            }
            else if (i + 1 == data.Length || data[i + 1] >= 224) throw new InvalidDataException("ID3 unsynchronisation escape is missing.");
        }
        return restored.ToArray();
    }
}
