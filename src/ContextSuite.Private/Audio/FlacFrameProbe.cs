using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static class FlacFrameProbe
{
    internal const int MaximumJsonBytes = 8 * 1024 * 1024;
    private static readonly ushort[] CrcTable = BuildCrcTable();

    internal static ImmutableArray<FlacFrame> Parse(ReadOnlyMemory<byte> json, FlacMetadataHeader header, long fileBytes)
    {
        if (json.Length > MaximumJsonBytes) throw new InvalidDataException("FLAC packet index exceeds its JSON budget.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var streams = document.RootElement.GetProperty("streams");
            if (streams.GetArrayLength() != 1 || streams[0].GetProperty("codec_name").GetString() != "flac" ||
                streams[0].GetProperty("time_base").GetString() != "1/" + header.SampleRate.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("FLAC packet timestamps do not use the declared sample clock.");
            var packets = document.RootElement.GetProperty("packets");
            if (packets.GetArrayLength() > FlacSeekTable.MaximumFrames) throw new InvalidDataException("FLAC packet index exceeds its frame budget.");
            var frames = ImmutableArray.CreateBuilder<FlacFrame>();
            foreach (var packet in packets.EnumerateArray())
                frames.Add(new(Number(packet, "pts"), checked(Number(packet, "pos") - header.AudioOffset),
                    checked((int)Number(packet, "duration")), checked((int)Number(packet, "size"))));
            var result = frames.ToImmutable();
            FlacSeekTable.ValidateFrames(result, fileBytes - header.AudioOffset, header.SampleFrames);
            return result;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Malformed FLAC packet index.", error); }
    }

    internal static async Task ValidateBytesAsync(FileStream source, FlacMetadataHeader header, ImmutableArray<FlacFrame> frames, CancellationToken token)
    {
        var buffer = new byte[65536];
        source.Position = header.AudioOffset;
        foreach (var frame in frames)
        {
            token.ThrowIfCancellationRequested();
            var remaining = frame.Bytes;
            ushort crc = 0;
            var first = true;
            while (remaining != 0)
            {
                var count = Math.Min(buffer.Length, remaining);
                await source.ReadExactlyAsync(buffer.AsMemory(0, count), token);
                if (first && (buffer[0] != 255 || (buffer[1] & 254) != 248)) throw new InvalidDataException("FLAC packet is not a frame boundary.");
                first = false;
                for (var index = 0; index < count; index++) crc = (ushort)((crc << 8) ^ CrcTable[(crc >> 8) ^ buffer[index]]);
                remaining -= count;
            }
            if (crc != 0) throw new InvalidDataException("FLAC frame checksum does not match its indexed bytes.");
        }
    }

    private static long Number(JsonElement packet, string name)
    {
        var field = packet.GetProperty(name);
        var text = field.ValueKind == JsonValueKind.String ? field.GetString() : field.GetRawText();
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) throw new InvalidDataException("Invalid FLAC packet number.");
        return value;
    }

    private static ushort[] BuildCrcTable()
    {
        var table = new ushort[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = index << 8;
            for (var bit = 0; bit < 8; bit++) value = (value << 1) ^ ((value & 0x8000) != 0 ? 0x8005 : 0);
            table[index] = (ushort)value;
        }
        return table;
    }
}
