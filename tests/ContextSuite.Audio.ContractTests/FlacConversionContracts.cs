using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static class FlacConversionContracts
{
    public static async Task RunAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports)
    {
        var original = await File.ReadAllBytesAsync(Path.Combine(fixtures, "source.flac"));
        var header = FlacMetadata.Parse(original);
        var digest = SHA256.HashData(original);
        var scratch = Path.Combine(evidence, "flac-conversion-scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem)) await Accept(original, target, "baseline");
        var unicode = Assemble((4, Comments("TITLE=Unicode \u00fc \ud83d\ude00", "ARTIST=Artist", "COMMENT=Line one\r\nLine two\n\tquotes \" and trailing\\")));
        foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem).Where(target => target != AudioFormat.Wave))
            await Accept(unicode, target, "Unicode/multiline");
        await Accept(unicode, AudioFormat.Wave, "Unicode/multiline ID3");
        await Accept(Assemble((4, Comments("TITLE=ASCII", "COMMENT=Line one\r\nLine two\n\tquotes \" and trailing\\"))), AudioFormat.Wave, "ASCII multiline");
        await Accept(Assemble((4, Comments("ALBUMARTIST=Artist", "TRACKNUMBER=2/9", "DISCNUMBER=1", "DISCSUBTITLE=First", "DESCRIPTION=Comment"))),
            AudioFormat.Vorbis, "canonical aliases");
        foreach (var fields in new[] { new[] { "ARTIST=First", "artist=Second" }, new[] { "DESCRIPTION=One", "COMMENT=Two" },
            new[] { "CHAPTER001=00:00:01.000" }, new[] { "REPLAYGAIN_TRACK_GAIN=-3.25 dB" }, new[] { "LOOPSTART=1000" } })
            await Reject(Assemble((4, Comments(fields))), fields[0], AudioFormat.Mp3);
        await Reject(Assemble((2, "TESTapplication data"u8.ToArray())), "application metadata", AudioFormat.Mp3);
        await Reject(Assemble((7, "unknown metadata"u8.ToArray())), "unknown block", AudioFormat.Mp3);
        var staleSeek = new byte[18]; staleSeek[15] = 1; staleSeek[16] = 0x10;
        await Reject(Assemble((3, staleSeek)), "stale seek offset", AudioFormat.Mp3);
        var corrupt = original.ToArray(); corrupt[^1] ^= 1;
        await Reject(corrupt, "frame checksum", AudioFormat.Mp3);
        await Reject(original.Concat(new byte[] { 1, 2, 3 }).ToArray(), "trailing bytes", AudioFormat.Mp3);
        if (!SHA256.HashData(original).SequenceEqual(digest) ||
            !SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(fixtures, "source.flac"))).SequenceEqual(digest) ||
            Directory.EnumerateFileSystemEntries(scratch).Any()) throw new IOException("FLAC conversion changed input or retained scratch.");
        checks.Add("FLAC conversion: source digest and owned scratch survive accepted and refused work");

        async Task Accept(byte[] bytes, AudioFormat target, string name)
        {
            var result = await adapter.ConvertAsync(bytes, target, consent, default);
            if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || (!result.Plan.AlreadyTarget && result.DecodedFrames != 96000))
                throw new InvalidDataException("FLAC conversion did not prove metadata/samples: " + name + " to " + target);
            checks.Add("FLAC conversion: " + name + " to " + target);
            reports.Add(new { flacConversion = name, target = target.ToString(), result.SourceMetadataVerified, result.Facts, result.DecodedFrames });
        }
        async Task Reject(byte[] bytes, string name, AudioFormat target)
        {
            try { await adapter.ConvertAsync(bytes, target, consent, default); throw new Exception("FLAC conversion omitted or admitted: " + name); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { checks.Add("FLAC conversion refuses " + name); }
            if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new IOException("Refused FLAC retained scratch: " + name);
        }
        byte[] Assemble(params (byte Type, byte[] Data)[] additional)
        {
            using var stream = new MemoryStream(); stream.Write("fLaC"u8);
            var blocks = new[] { ((byte)0, header.Blocks[0].Data.ToArray()) }.Concat(additional).ToArray();
            for (var i = 0; i < blocks.Length; i++)
            {
                var (type, data) = blocks[i]; stream.WriteByte((byte)(type | (i == blocks.Length - 1 ? 128 : 0)));
                stream.WriteByte((byte)(data.Length >> 16)); stream.WriteByte((byte)(data.Length >> 8)); stream.WriteByte((byte)data.Length); stream.Write(data);
            }
            stream.Write(original.AsSpan(header.AudioOffset)); return stream.ToArray();
        }
    }
    private static byte[] Comments(params string[] fields)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        var vendor = Encoding.UTF8.GetBytes("Context Suite conversion fixture"); writer.Write(vendor.Length); writer.Write(vendor); writer.Write(fields.Length);
        foreach (var field in fields) { var data = Encoding.UTF8.GetBytes(field); writer.Write(data.Length); writer.Write(data); }
        return stream.ToArray();
    }
}
