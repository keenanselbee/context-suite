using System.Buffers.Binary;
using System.Collections.Immutable;

namespace ContextSuite.Core.Analysis;

// Reads declarations only. No sample decoding, allocation from file lengths, or I/O.
internal static class AudioHeaderFacts
{
    private const int MaximumRecords = 256;

    public static void AddWave(ReadOnlySpan<byte> bytes, long fileBytes,
        ImmutableArray<AnalysisFact>.Builder facts, ImmutableArray<string>.Builder warnings)
    {
        var parsed = ImmutableArray.CreateBuilder<AnalysisFact>();
        try
        {
            var end = 8L + BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
            if (end < 12 || end > fileBytes) throw new InvalidDataException();
            long offset = 12;
            uint? firstDataBytes = null;
            uint sampleRate = 0;
            ushort blockAlign = 0;
            var pcm = false;
            var formatSeen = false;
            var records = 0;
            while (offset < end && records < MaximumRecords)
            {
                if (end - offset < 8) throw new InvalidDataException();
                if (offset > bytes.Length - 8) break;
                var header = bytes[(int)offset..];
                var size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
                var next = offset + 8 + size + (size & 1);
                if (next > end) throw new InvalidDataException();
                records++;
                if (header[..4].SequenceEqual("fmt "u8))
                {
                    if (formatSeen) throw new InvalidDataException();
                    formatSeen = true;
                    if (size < 16) throw new InvalidDataException();
                    if (offset + 8 + size > bytes.Length) break;
                    var format = header.Slice(8, (int)size);
                    var tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
                    var channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
                    sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
                    var byteRate = BinaryPrimitives.ReadUInt32LittleEndian(format[8..]);
                    blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(format[12..]);
                    var containerBits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
                    if (channels == 0 || sampleRate == 0 || blockAlign == 0) throw new InvalidDataException();
                    var codec = tag;
                    ushort? validBits = null;
                    if (tag == 0xfffe)
                    {
                        if (size < 40) throw new InvalidDataException();
                        var extra = BinaryPrimitives.ReadUInt16LittleEndian(format[16..]);
                        if (extra < 22 || 18L + extra > size) throw new InvalidDataException();
                        var subformat = new Guid(format.Slice(24, 16));
                        codec = subformat == new Guid("00000001-0000-0010-8000-00aa00389b71") ? (ushort)1 :
                            subformat == new Guid("00000003-0000-0010-8000-00aa00389b71") ? (ushort)3 : (ushort)0;
                        if (codec is 1 or 3) validBits = BinaryPrimitives.ReadUInt16LittleEndian(format[18..]);
                        parsed.Add(new("wave.subformat", "Audio", "Declared subformat", Text: subformat.ToString()));
                        parsed.Add(new("wave.channel-mask", "Audio", "Raw speaker mask", Integer: BinaryPrimitives.ReadUInt32LittleEndian(format[20..])));
                    }
                    pcm = codec is 1 or 3;
                    if (pcm && (containerBits == 0 || containerBits % 8 != 0 ||
                        (long)channels * (containerBits / 8) != blockAlign || (long)sampleRate * blockAlign != byteRate ||
                        validBits is 0 || validBits > containerBits || (codec == 3 && containerBits is not (32 or 64))))
                        throw new InvalidDataException();
                    parsed.Add(new("wave.format-tag", "Audio", "Raw format tag", Integer: tag));
                    parsed.Add(new("audio.codec", "Audio", "Declared codec", Text: codec switch
                    { 1 => "Integer PCM", 3 => "IEEE floating-point PCM", _ => "Not interpreted" }));
                    parsed.Add(new("audio.channels", "Audio", "Declared channels", Integer: channels));
                    parsed.Add(new("audio.sample-rate", "Audio", "Declared sample rate (Hz)", Integer: sampleRate));
                    parsed.Add(new("wave.byte-rate", "Audio", "Declared average bytes per second", Integer: byteRate));
                    parsed.Add(new("wave.block-align", "Audio", "Declared block alignment (bytes)", Integer: blockAlign));
                    parsed.Add(pcm ? new("audio.sample-bits", "Audio", "Declared sample container bits", Integer: containerBits)
                        : new("audio.sample-bits", "Audio", "Sample precision", Availability: FactAvailability.Unavailable));
                    if (validBits is { } precision)
                        parsed.Add(new("audio.valid-bits", "Audio", "Declared valid sample bits", Integer: precision));
                }
                else if (header[..4].SequenceEqual("data"u8))
                {
                    if (firstDataBytes.HasValue) warnings.Add("Multiple WAVE data chunks were observed. Timing below covers only the first chunk.");
                    firstDataBytes ??= size;
                }
                offset = next;
            }
            if (offset != end) warnings.Add("The WAVE chunk list exceeds the inspected bytes or record limit. Later properties are unavailable.");
            if (end < fileBytes) warnings.Add("Bytes follow the declared RIFF container; they were not interpreted as audio.");
            if (!parsed.Any(fact => fact.Id == "audio.codec"))
                parsed.Add(new("audio.codec", "Audio", "Audio codec", Availability: FactAvailability.Unavailable));
            if (firstDataBytes is { } dataBytes)
            {
                parsed.Add(new("wave.first-data-bytes", "Audio", "Declared first data chunk (bytes)", Integer: dataBytes));
                if (pcm && dataBytes % blockAlign == 0)
                {
                    var frames = (long)dataBytes / blockAlign;
                    parsed.Add(new("wave.first-data-frames", "Audio", "First data chunk sample frames (from header)", Integer: frames, Availability: FactAvailability.Derived));
                    parsed.Add(new("wave.first-data-duration-ms", "Audio", "First data chunk duration (ms, from header)", Integer: frames * 1000 / sampleRate, Availability: FactAvailability.Derived));
                }
                else if (pcm) warnings.Add("The WAVE data size is not a whole number of sample frames. Duration is unavailable.");
            }
            facts.AddRange(parsed);
        }
        catch (InvalidDataException)
        {
            warnings.Add("The WAVE header has incomplete or inconsistent chunk/format declarations. Audio properties are unavailable.");
        }
    }

    public static void AddFlac(ReadOnlySpan<byte> bytes, long fileBytes,
        ImmutableArray<AnalysisFact>.Builder facts, ImmutableArray<string>.Builder warnings)
    {
        if (bytes.Length < 42 || (bytes[4] & 127) != 0 || bytes[5] != 0 || bytes[6] != 0 || bytes[7] != 34)
        {
            warnings.Add("A complete first FLAC STREAMINFO block was not found. Audio properties are unavailable.");
            return;
        }
        var minimumBlock = BinaryPrimitives.ReadUInt16BigEndian(bytes[8..]);
        var maximumBlock = BinaryPrimitives.ReadUInt16BigEndian(bytes[10..]);
        var packed = BinaryPrimitives.ReadUInt64BigEndian(bytes[18..]);
        var rate = (long)(packed >> 44);
        var channels = (long)((packed >> 41) & 7) + 1;
        var bits = (long)((packed >> 36) & 31) + 1;
        var samples = (long)(packed & 0xfffffffffUL);
        if (minimumBlock < 16 || maximumBlock < minimumBlock || bits < 4)
        {
            warnings.Add("FLAC STREAMINFO contains invalid block-size or sample-precision declarations. Audio properties are unavailable.");
            return;
        }
        facts.Add(new("audio.codec", "Audio", "Declared codec", Text: "FLAC"));
        facts.Add(new("audio.channels", "Audio", "Declared channels", Integer: channels));
        facts.Add(new("audio.sample-bits", "Audio", "Declared sample bits", Integer: bits));
        facts.Add(new("audio.sample-rate", "Audio", "Declared sample rate (Hz)", Integer: rate));
        facts.Add(samples == 0 ? new("audio.sample-frames", "Audio", "Declared sample frames", Availability: FactAvailability.Unknown)
            : new("audio.sample-frames", "Audio", "Declared sample frames", Integer: samples));
        facts.Add(samples > 0 && rate > 0
            ? new("audio.duration-ms", "Audio", "Duration (ms, from header)", Integer: samples * 1000 / rate, Availability: FactAvailability.Derived)
            : new("audio.duration-ms", "Audio", "Duration", Availability: FactAvailability.Unavailable));
        if (rate == 0) warnings.Add("FLAC declares a zero sample rate, which can represent non-audio data. No playback duration is inferred.");
        facts.Add(new("flac.md5-present", "Audio", "Audio MD5 declared (not verified)", Boolean: bytes.Slice(26, 16).IndexOfAnyExcept((byte)0) >= 0));

        var offset = 4;
        var complete = false;
        var comments = 0;
        var pictures = 0;
        for (var records = 0; records < MaximumRecords && offset <= bytes.Length - 4; records++)
        {
            var type = bytes[offset] & 127;
            var size = (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
            var next = (long)offset + 4 + size;
            if (type == 127 || (offset != 4 && type == 0) || next > fileBytes)
            {
                warnings.Add("The FLAC metadata list is malformed. STREAMINFO facts are declarations only.");
                break;
            }
            if (next > bytes.Length) break;
            if (type == 4) comments++;
            if (type == 6) pictures++;
            if ((bytes[offset] & 128) != 0) { complete = true; break; }
            offset = (int)next;
        }
        facts.Add(new("flac.comment-blocks", "Metadata", "Comment blocks observed (contents not validated)", Integer: comments));
        facts.Add(new("flac.picture-blocks", "Metadata", "Picture blocks observed (contents not validated)", Integer: pictures));
        facts.Add(new("flac.metadata-list-complete", "Metadata", "Metadata block list fully inspected", Boolean: complete));
        if (!complete) warnings.Add("The FLAC metadata list was not fully inspected. Unobserved tags or artwork may still be present.");
    }
}
