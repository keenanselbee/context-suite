using System.Buffers.Binary;

namespace ContextSuite.Core.Audio;

internal static class M4aAudioConfiguration
{
    public static (int Rate, int Channels) Read(ReadOnlyMemory<byte> entry, M4aBoxes boxes)
    {
        var data = entry.Span;
        if (data.Length < 28 || data[..6].IndexOfAnyExcept((byte)0) >= 0 || BinaryPrimitives.ReadUInt16BigEndian(data[6..]) != 1 ||
            data.Slice(8, 8).IndexOfAnyExcept((byte)0) >= 0)
            throw new NotSupportedException("M4A sample entry version or data reference needs a handler.");
        var children = boxes.Read(entry[28..], "esds", "btrt");
        if (children.Count(box => box.Type == "btrt") > 1 || children.Any(box => box.Type == "btrt" && box.Data.Length != 12))
            throw new InvalidDataException("Invalid M4A bitrate atom.");
        var descriptors = M4aBoxes.One(children, "esds").Span;
        M4aBoxes.FullBox(descriptors, 5);
        var offset = 4;
        var es = Descriptor(descriptors, ref offset, 3);
        if (offset != descriptors.Length || es.Length < 3 || es[2] != 0) throw new NotSupportedException("M4A external or dependent elementary streams need a handler.");
        offset = 3;
        var decoder = Descriptor(es, ref offset, 4);
        var sl = Descriptor(es, ref offset, 6);
        if (offset != es.Length || sl.Length != 1 || sl[0] != 2 || decoder.Length < 15 || decoder[0] != 0x40 || decoder[1] != 0x15)
            throw new NotSupportedException("M4A requires one self-contained MPEG-4 audio configuration.");
        offset = 13;
        var config = Descriptor(decoder, ref offset, 5);
        if (offset != decoder.Length || config.Length is < 2 or > 64) throw new NotSupportedException("M4A decoder configuration needs a handler.");
        var bit = 0;
        if (Bits(config, ref bit, 5) != 2) throw new NotSupportedException("Only AAC-LC has a reviewed M4A conversion policy.");
        var frequency = Bits(config, ref bit, 4);
        ReadOnlySpan<int> rates = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];
        var rate = frequency == 15 ? Bits(config, ref bit, 24) : frequency < rates.Length ? rates[frequency] : 0;
        var channels = Bits(config, ref bit, 4);
        if (channels is < 1 or > 7) throw new NotSupportedException("AAC channel configuration needs a mapping handler.");
        if (channels == 7) channels = 8;
        if (rate is < 8000 or > 192000 || channels is < 1 or > 8 || Bits(config, ref bit, 3) != 0)
            throw new NotSupportedException("AAC rate, channel program or dependent coding needs a policy.");
        if (config.Length * 8 - bit >= 17)
        {
            if (Bits(config, ref bit, 11) != 0x2b7 || Bits(config, ref bit, 5) != 5 || Bits(config, ref bit, 1) != 0)
                throw new NotSupportedException("AAC extension or spectral-band replication needs a policy.");
        }
        while (bit < config.Length * 8)
            if (Bits(config, ref bit, 1) != 0) throw new NotSupportedException("Unknown AAC configuration suffix.");
        return (rate, channels);
    }
    private static ReadOnlySpan<byte> Descriptor(ReadOnlySpan<byte> data, ref int offset, byte tag)
    {
        if (offset >= data.Length || data[offset++] != tag) throw new InvalidDataException("Missing MPEG-4 audio descriptor.");
        var length = 0; var completed = false;
        for (var i = 0; i < 4; i++)
        {
            if (offset >= data.Length) throw new InvalidDataException("Truncated audio descriptor length.");
            var value = data[offset++]; length = (length << 7) | (value & 127);
            if ((value & 128) == 0) { completed = true; break; }
        }
        if (!completed || length > data.Length - offset) throw new InvalidDataException("Audio descriptor exceeds its parent.");
        var result = data.Slice(offset, length); offset += length; return result;
    }
    private static int Bits(ReadOnlySpan<byte> data, ref int position, int count)
    {
        if (count > data.Length * 8 - position) throw new InvalidDataException("Truncated AAC configuration bits.");
        var value = 0;
        for (var i = 0; i < count; i++, position++) value = (value << 1) | ((data[position / 8] >> (7 - position % 8)) & 1);
        return value;
    }
}
