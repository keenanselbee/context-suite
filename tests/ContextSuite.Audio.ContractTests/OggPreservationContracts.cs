using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Audio;

internal static partial class OggPreservationContracts
{
    public static async Task RunAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports)
    {
        var scratch = Path.Combine(evidence, "ogg-preservation-scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        foreach (var frames in new[] { 1, 9600 })
        {
            try
            {
                await adapter.ConvertAsync(EncodingContracts.Wave(24, false, 48000, 6, frames), AudioFormat.Vorbis, consent, default);
                throw new Exception("Extreme impulse or out-of-band LFE unexpectedly passed the fixed signal-error bound.");
            }
            catch (ImageFailureException ex) when (ex.Failure == ImageFailure.ValidationFailed)
            { checks.Add("Vorbis retains signal-error refusal after fixing page framing: " + frames); }
        }
        foreach (var frames in new[] { 64, 128, 129, 256, 511, 1024 })
        {
            var result = await adapter.ConvertAsync(EncodingContracts.Wave(24, false, 48000, 6, frames), AudioFormat.Vorbis, consent, default);
            if (result.DecodedFrames != frames) throw new InvalidDataException("Short Vorbis output changed its decoded length.");
            checks.Add("Vorbis packet pages retain short decoded extent: " + frames);
            reports.Add(new { shortVorbisFrames = frames, result.DecodedFrames, result.RootMeanSquareError });
        }
        foreach (var format in new[] { AudioFormat.Vorbis, AudioFormat.Opus })
        {
            var encoded = await adapter.ConvertAsync(EncodingContracts.Wave(24, false, 48000, 6, lowFrequencyEffects: true), format, consent, default);
            var decoded = await adapter.ConvertAsync(encoded.Bytes, AudioFormat.Wave, consent, default);
            using var input = new MemoryStream(encoded.Bytes);
            var inventory = await OggMetadata.ReadAsync(input, default);
            if (inventory.Channels != 6 || !decoded.SourceMetadataVerified || decoded.Facts.Streams[0].Channels != 6 ||
                decoded.Facts.Streams[0].ChannelLayout != encoded.Facts.Streams[0].ChannelLayout || decoded.DecodedFrames < 9000)
                throw new InvalidDataException("Ogg multichannel interpretation changed across conversion.");
            checks.Add("Ogg preservation: six-channel source mapping and decoded layout: " + format);
            reports.Add(new { oggSurround = format.ToString(), inventory, decoded.Facts, decoded.DecodedFrames });
        }
        foreach (var opus in new[] { false, true })
        {
            var path = Path.Combine(fixtures, opus ? "source.opus" : "source.ogg");
            var source = await File.ReadAllBytesAsync(path);
            var digest = SHA256.HashData(source);
            foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem)) await Accept(source, target, "baseline");
            var rich = ReplaceComments(source, opus, "TITLE=Unicode \u00fc \ud83d\ude00", "COMMENT=Line one\r\nLine two\n\tquotes \" and trailing\\");
            foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem).Where(target => target != AudioFormat.Wave)) await Accept(rich, target, "Unicode/multiline");
            await Accept(rich, AudioFormat.Wave, "Unicode/multiline ID3");
            await Accept(ReplaceComments(source, opus, "TITLE=ASCII", "COMMENT=Line one\r\nLine two\n\tend\\"), AudioFormat.Wave, "ASCII multiline");
            await Reject(ReplaceComments(source, opus, "ARTIST=One", "ARTIST=Two"), "duplicate values");
            await Reject(ReplaceComments(source, opus, "REPLAYGAIN_TRACK_GAIN=-3 dB"), "playback gain comment");
            var corrupt = source.ToArray(); corrupt[^1] ^= 1;
            await Reject(corrupt, "corrupt page checksum");
            await Reject(source.Concat(new byte[] { 1, 2, 3 }).ToArray(), "trailing bytes");
            await Reject(source.Concat(source).ToArray(), "chained streams");
            var sequence = source.ToArray(); var second = PageLength(sequence, 0); sequence[second + 18] += 2; Checksum(sequence.AsSpan(second, PageLength(sequence, second)));
            await Reject(sequence, "missing page sequence");
            if (opus)
            {
                var gain = source.ToArray(); var headerBody = 27 + gain[26]; gain[headerBody + 16] = 1; Checksum(gain.AsSpan(0, PageLength(gain, 0)));
                await Reject(gain, "Opus output gain");
            }
            if (!SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(digest) || Directory.EnumerateFileSystemEntries(scratch).Any())
                throw new IOException("Ogg source changed or scratch remains.");
            checks.Add("Ogg preservation: source hash and scratch cleanup: " + opus);

            async Task Accept(byte[] bytes, AudioFormat target, string name)
            {
                var result = await adapter.ConvertAsync(bytes, target, consent, default);
                if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || (!result.Plan.AlreadyTarget && result.DecodedFrames < 95000))
                    throw new InvalidDataException("Ogg metadata or duration was not preserved: " + opus + " " + name + " " + target);
                checks.Add("Ogg preservation: " + opus + " " + name + " to " + target);
                reports.Add(new { oggOpus = opus, name, target = target.ToString(), result.SourceMetadataVerified, result.Facts, result.DecodedFrames });
            }
            async Task Reject(byte[] bytes, string name, AudioFormat target = AudioFormat.Mp3)
            {
                try { await adapter.ConvertAsync(bytes, target, consent, default); throw new Exception("Ogg omitted or admitted " + name); }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { checks.Add("Ogg preservation refuses " + opus + " " + name); }
                if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new IOException("Ogg rejection retained scratch.");
            }
        }
    }

    private static byte[] ReplaceComments(byte[] source, bool opus, params string[] fields)
    {
        // Generated fixtures keep the complete comment packet at the start of page 1.
        // Preserve following Vorbis setup segments and all audio pages byte-for-byte.
        var offset = PageLength(source, 0);
        var oldLength = PageLength(source, offset);
        var segments = source[offset + 26]; var removedSegments = 0; var removedBytes = 0;
        byte size;
        do { size = source[offset + 27 + removedSegments++]; removedBytes += size; } while (size == 255 && removedSegments < segments);
        if (size == 255 || source[offset + 5] != 0) throw new InvalidDataException("Fixture comments do not fit one page.");
        using var packet = new MemoryStream(); using var writer = new BinaryWriter(packet, Encoding.UTF8, true);
        packet.Write(opus ? "OpusTags"u8 : "\x03vorbis"u8);
        var vendor = Encoding.UTF8.GetBytes("Context Suite authored comments"); writer.Write(vendor.Length); writer.Write(vendor); writer.Write(fields.Length);
        foreach (var field in fields) { var text = Encoding.UTF8.GetBytes(field); writer.Write(text.Length); writer.Write(text); }
        if (!opus) packet.WriteByte(1);
        var bytes = packet.ToArray(); var addedSegments = bytes.Length / 255 + 1;
        var newSegments = segments - removedSegments + addedSegments;
        var page = new byte[oldLength - removedSegments - removedBytes + addedSegments + bytes.Length];
        source.AsSpan(offset, 27).CopyTo(page); page[26] = checked((byte)newSegments);
        for (var i = 0; i < addedSegments; i++) page[27 + i] = (byte)Math.Min(255, bytes.Length - 255 * i);
        source.AsSpan(offset + 27 + removedSegments, segments - removedSegments).CopyTo(page.AsSpan(27 + addedSegments));
        bytes.CopyTo(page, 27 + newSegments);
        source.AsSpan(offset + 27 + segments + removedBytes, oldLength - 27 - segments - removedBytes).CopyTo(page.AsSpan(27 + newSegments + bytes.Length));
        Checksum(page);
        return source[..offset].Concat(page).Concat(source[(offset + oldLength)..]).ToArray();
    }
    private static int PageLength(byte[] file, int offset)
    {
        var length = 27 + file[offset + 26];
        for (var i = 0; i < file[offset + 26]; i++) length += file[offset + 27 + i];
        return length;
    }
    private static void Checksum(Span<byte> page)
    {
        page.Slice(22, 4).Clear(); uint crc = 0;
        foreach (var value in page)
        {
            crc ^= (uint)value << 24;
            for (var bit = 0; bit < 8; bit++) crc = (crc << 1) ^ ((crc & 0x80000000) != 0 ? 0x04c11db7u : 0);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(page[22..], crc);
    }
}
