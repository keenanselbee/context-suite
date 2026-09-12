using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace ContextSuite.Core.Analysis;

internal static partial class AudioHeaderFacts
{
    public static void AddAu(ReadOnlySpan<byte> bytes, long fileBytes,
        ImmutableArray<AnalysisFact>.Builder facts, ImmutableArray<string>.Builder warnings)
    {
        if (bytes.Length < 24)
        {
            warnings.Add("The AU header is incomplete. Audio properties are unavailable.");
            return;
        }
        var offset = BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]);
        var size = BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]);
        var encoding = BinaryPrimitives.ReadUInt32BigEndian(bytes[12..]);
        var rate = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]);
        var channels = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]);
        if (offset < 24 || offset > fileBytes || rate == 0 || channels == 0 || size != uint.MaxValue && size > fileBytes - offset)
        {
            warnings.Add("The AU header has inconsistent offset, size or audio declarations. Audio properties are unavailable.");
            return;
        }
        var dataBytes = size == uint.MaxValue ? fileBytes - offset : size;
        var bits = encoding switch { 2 => 8, 3 => 16, 4 => 24, 5 or 6 => 32, 7 => 64, _ => 0 };
        facts.Add(new("au.data-offset", "Audio", "Declared audio offset (bytes)", Integer: offset));
        facts.Add(new("au.encoding", "Audio", "Raw encoding identifier", Integer: encoding));
        facts.Add(new("au.data-bytes", "Audio", size == uint.MaxValue ? "Audio extent (bytes, from file length)" : "Declared audio extent (bytes)",
            Integer: dataBytes, Availability: size == uint.MaxValue ? FactAvailability.Derived : FactAvailability.Explicit));
        facts.Add(new("audio.codec", "Audio", "Declared codec", Text: encoding switch
        { >= 2 and <= 5 => "Signed integer PCM (big-endian)", 6 or 7 => "IEEE floating-point PCM (big-endian)", _ => "Not interpreted" }));
        facts.Add(new("audio.channels", "Audio", "Declared channels", Integer: channels));
        facts.Add(new("audio.sample-rate", "Audio", "Declared sample rate (Hz)", Integer: rate));
        facts.Add(bits == 0 ? new("audio.sample-bits", "Audio", "Sample precision", Availability: FactAvailability.Unavailable)
            : new("audio.sample-bits", "Audio", "Declared sample container bits", Integer: bits));
        if (size != uint.MaxValue && offset + dataBytes < fileBytes)
            warnings.Add("Bytes follow the declared AU audio extent; they were not interpreted as audio.");
        var frameBytes = (long)channels * (bits / 8);
        if (frameBytes > 0 && dataBytes % frameBytes == 0)
        {
            var frames = dataBytes / frameBytes;
            var milliseconds = (Int128)frames * 1000 / rate;
            facts.Add(new("audio.sample-frames", "Audio", "Sample frames (from header/extent)", Integer: frames, Availability: FactAvailability.Derived));
            facts.Add(milliseconds <= long.MaxValue
                ? new("audio.duration-ms", "Audio", "Duration (ms, from header/extent)", Integer: (long)milliseconds, Availability: FactAvailability.Derived)
                : new("audio.duration-ms", "Audio", "Duration", Availability: FactAvailability.Unavailable));
        }
        else
        {
            facts.Add(new("audio.duration-ms", "Audio", "Duration", Availability: FactAvailability.Unavailable));
            if (frameBytes > 0) warnings.Add("The AU audio extent is not a whole number of sample frames. Duration is unavailable.");
        }
    }

    public static void AddAiff(ReadOnlySpan<byte> bytes, long fileBytes,
        ImmutableArray<AnalysisFact>.Builder facts, ImmutableArray<string>.Builder warnings)
    {
        var parsed = ImmutableArray.CreateBuilder<AnalysisFact>();
        try
        {
            var compressed = bytes.Slice(8, 4).SequenceEqual("AIFC"u8);
            var end = 8L + BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]);
            if (end < 12 || end > fileBytes) throw new InvalidDataException();
            long offset = 12;
            var seen = false;
            var records = 0;
            while (offset < end && records < MaximumRecords)
            {
                if (end - offset < 8) throw new InvalidDataException();
                if (offset > bytes.Length - 8) break;
                var header = bytes[(int)offset..];
                var size = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
                var next = offset + 8 + size + (size & 1);
                if (next > end) throw new InvalidDataException();
                records++;
                if (header[..4].SequenceEqual("COMM"u8))
                {
                    if (seen || size < (compressed ? 24 : 18)) throw new InvalidDataException();
                    seen = true;
                    if (offset + 8 + size > bytes.Length) break;
                    var common = header.Slice(8, (int)size);
                    var channels = BinaryPrimitives.ReadInt16BigEndian(common);
                    var frames = BinaryPrimitives.ReadUInt32BigEndian(common[2..]);
                    var bits = BinaryPrimitives.ReadInt16BigEndian(common[6..]);
                    if (channels <= 0 || bits is < 1 or > 32) throw new InvalidDataException();
                    if (compressed && 22 + ((common[22] + 2) & ~1) > size) throw new InvalidDataException();
                    var codec = !compressed || common.Slice(18, 4).SequenceEqual("NONE"u8) ? "Signed integer PCM (big-endian)" : "Not interpreted";
                    parsed.Add(new("aiff.form", "Audio", "Declared form", Text: compressed ? "AIFF-C" : "AIFF"));
                    parsed.Add(new("audio.codec", "Audio", "Declared codec", Text: codec));
                    if (compressed)
                    {
                        var tag = common.Slice(18, 4);
                        parsed.Add(new("aiff.compression", "Audio", "Raw compression identifier",
                            Text: tag.IndexOfAnyExceptInRange((byte)32, (byte)126) < 0 ? Encoding.ASCII.GetString(tag) : "0x" + Convert.ToHexString(tag)));
                    }
                    parsed.Add(new("audio.channels", "Audio", "Declared channels", Integer: channels));
                    parsed.Add(new("audio.sample-frames", "Audio", "Declared decoded sample frames", Integer: frames));
                    parsed.Add(new("audio.sample-bits", "Audio", "Declared original sample bits", Integer: bits));
                    var rawRate = common.Slice(8, 10);
                    parsed.Add(new("aiff.sample-rate-80", "Audio", "Raw 80-bit sample rate", Text: Convert.ToHexString(rawRate)));
                    var exponent = BinaryPrimitives.ReadUInt16BigEndian(rawRate);
                    var mantissa = BinaryPrimitives.ReadUInt64BigEndian(rawRate[2..]);
                    var rate = exponent is > 0 and < 0x7fff && (mantissa & (1UL << 63)) != 0
                        ? Math.ScaleB((double)mantissa, exponent - 16383 - 63) : double.NaN;
                    var supported = double.IsFinite(rate) && rate > 0;
                    parsed.Add(supported ? new("audio.sample-rate", "Audio", "Approximate declared sample rate (Hz)",
                        Text: rate.ToString("G17", CultureInfo.InvariantCulture), Availability: FactAvailability.Derived)
                        : new("audio.sample-rate", "Audio", "Sample rate", Availability: FactAvailability.Unavailable));
                    var milliseconds = frames / rate * 1000;
                    parsed.Add(supported && double.IsFinite(milliseconds) && milliseconds >= 0 && milliseconds < 9223372036854775808.0
                        ? new("audio.duration-ms", "Audio", "Approximate duration (ms, from declarations)", Integer: (long)milliseconds, Availability: FactAvailability.Derived)
                        : new("audio.duration-ms", "Audio", "Duration", Availability: FactAvailability.Unavailable));
                    if (!supported) warnings.Add("The AIFF sample-rate representation is unsupported or not a finite positive value. Rate and duration are unavailable.");
                }
                offset = next;
            }
            if (offset != end) warnings.Add("The AIFF chunk list exceeds the inspected bytes or record limit. Later properties are unavailable.");
            if (end < fileBytes) warnings.Add("Bytes follow the declared AIFF container; they were not interpreted as audio.");
            if (parsed.Count == 0) warnings.Add("A complete supported AIFF Common Chunk was not found. Audio properties are unavailable.");
            facts.AddRange(parsed);
        }
        catch (InvalidDataException)
        {
            warnings.Add("The AIFF header has incomplete, unsupported or inconsistent chunk/audio declarations. Audio properties are unavailable.");
        }
    }
}
