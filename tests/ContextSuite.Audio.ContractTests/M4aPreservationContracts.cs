using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static partial class M4aPreservationContracts
{
    public static async Task RunAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports)
    {
        var path = Path.Combine(fixtures, "source.m4a"); var source = await File.ReadAllBytesAsync(path); var hash = SHA256.HashData(source);
        if (source.AsSpan().IndexOf("moov"u8) < source.AsSpan().IndexOf("mdat"u8)) throw new InvalidDataException("Fixture edits require a trailing movie atom.");
        var scratch = Path.Combine(evidence, "m4a-preservation-scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem)) await Accept(source, target, "baseline");
        var unicode = Replace(source, ["moov", "udta", "meta", "ilst"], _ => Text("\u00a9nam", "Title \u00fc")
            .Concat(Text("\u00a9cmt", "Line one\r\nLine two\n\tquotes \" and trailing\\")).ToArray());
        foreach (var target in new[] { AudioFormat.Flac, AudioFormat.Mp3, AudioFormat.Vorbis, AudioFormat.Opus })
            await Accept(unicode, target, "Unicode/multiline");
        await Accept(unicode, AudioFormat.Wave, "Unicode/multiline ID3");
        foreach (var type in new[] { "covr", "----", "gnre", "uuid", "cpil" })
            await Reject(Replace(source, ["moov", "udta", "meta", "ilst"], data => data.Concat(Text(type, "Extra")).ToArray()), "unhandled " + type);
        await Reject(Replace(source, ["moov", "udta", "meta", "ilst"], data => data.Concat(Text("\u00a9nam", "Repeated")).ToArray()), "duplicate title");
        await Reject(Replace(source, ["moov", "trak", "mdia", "minf", "dinf", "dref"], _ => N(0).Concat(N(1)).Concat(Box("url ", N(0).Concat("file:///must-not-read\0"u8.ToArray()).ToArray())).ToArray()), "external reference");
        foreach (var (type, offset, value) in new[] { ("mvhd", 4, 1U), ("tkhd", 4, 1U), ("mdhd", 8, 1U), ("stco", 8, 0U),
            ("stsc", 16, 2U), ("elst", 12, uint.MaxValue), ("sgpd", 4, 0x73656967U) })
        {
            var changed = source.ToArray(); var index = changed.AsSpan().IndexOf(Encoding.ASCII.GetBytes(type));
            if (index < 0) throw new InvalidDataException("Missing generated M4A atom: " + type);
            N(value).CopyTo(changed, index + 4 + offset); await Reject(changed, type + " field " + offset);
        }
        await Reject(source.Concat(Box("uuid", [])).ToArray(), "unhandled root atom");
        await Reject(source[..^1], "truncated movie");
        foreach (var rate in new[] { 16000, 44100, 48000 })
        {
            var encoded = await adapter.ConvertAsync(EncodingContracts.Wave(16, false, rate, 1), AudioFormat.M4a, consent, default);
            var decoded = await adapter.ConvertAsync(encoded.Bytes, AudioFormat.Wave, consent, default);
            if (!decoded.SourceMetadataVerified || decoded.Facts.Streams[0].SampleRate != rate || decoded.DecodedFrames != rate / 5)
                throw new InvalidDataException("M4A faststart inventory or priming decode changed.");
            checks.Add("M4A inventory: faststart mono and priming decode " + rate);
            reports.Add(new { m4aRate = rate, decoded.Facts, decoded.DecodedFrames });
        }
        if (!SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(hash) || Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new IOException("M4A source changed or owned scratch remains.");
        checks.Add("M4A inventory: original digest and cleanup across accepted/refused conversions");

        async Task Accept(byte[] bytes, AudioFormat target, string name)
        {
            var result = await adapter.ConvertAsync(bytes, target, consent, default);
            if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || (!result.Plan.AlreadyTarget && result.DecodedFrames != 96000))
                throw new InvalidDataException("M4A did not retain metadata or priming: " + name + " to " + target);
            checks.Add("M4A preservation: " + name + " to " + target);
            reports.Add(new { m4aCase = name, target = target.ToString(), result.Facts, result.DecodedFrames });
        }
        async Task Reject(byte[] bytes, string name, AudioFormat target = AudioFormat.Flac)
        {
            try { await adapter.ConvertAsync(bytes, target, consent, default); throw new Exception("M4A omitted or admitted " + name); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { checks.Add("M4A preservation refuses " + name); }
            if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new IOException("M4A refusal retained scratch.");
        }
    }
    private static byte[] Replace(byte[] data, string[] path, Func<byte[], byte[]> change)
    {
        using var output = new MemoryStream(); var found = false; var offset = path[0] == "ilst" ? 4 : 0;
        output.Write(data.AsSpan(0, offset));
        while (offset < data.Length)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset)));
            var type = Encoding.Latin1.GetString(data, offset + 4, 4);
            if (type == path[0])
            {
                if (found) throw new InvalidDataException("Duplicate fixture atom."); found = true;
                var payload = data.AsSpan(offset + 8, size - 8).ToArray();
                output.Write(Box(type, path.Length == 1 ? change(payload) : Replace(payload, path[1..], change)));
            }
            else output.Write(data.AsSpan(offset, size));
            offset += size;
        }
        if (!found) throw new InvalidDataException("Missing fixture atom: " + path[0]);
        return output.ToArray();
    }
    private static byte[] Text(string type, string value) => Box(type, Box("data", N(1).Concat(N(0)).Concat(Encoding.UTF8.GetBytes(value)).ToArray()));
    private static byte[] Box(string type, byte[] data) => N((uint)data.Length + 8).Concat(Encoding.Latin1.GetBytes(type)).Concat(data).ToArray();
    private static byte[] N(uint value) { var data = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(data, value); return data; }
}
