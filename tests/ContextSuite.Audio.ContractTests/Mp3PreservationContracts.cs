using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static partial class Mp3PreservationContracts
{
    public static async Task RunAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports)
    {
        var path = Path.Combine(fixtures, "source.mp3"); var source = await File.ReadAllBytesAsync(path); var hash = SHA256.HashData(source);
        using var input = new MemoryStream(source);
        var inventory = await Mp3Metadata.ReadAsync(input, default);
        var frames = source[checked((int)inventory.AudioOffset)..];
        var scratch = Path.Combine(evidence, "mp3-preservation-scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem)) await Accept(source, target, "baseline");
        var versionTwo = Tag(2, Frame(2, "TT2", Text(0, "Older title")), Frame(2, "TP1", Text(0, "Artist")),
            Frame(2, "TAL", Text(0, "Album")), Frame(2, "TRK", Text(0, "7")), Frame(2, "TYE", Text(0, "1997")),
            Frame(2, "TCO", Text(0, "(17)")), Frame(2, "COM", new byte[] { 0 }.Concat("und\0Comment"u8.ToArray()).ToArray()))
            .Concat(frames).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidence, "mp3-id3v22.mp3"), versionTwo);
        foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem))
            await Accept(versionTwo, target, "ID3v2.2 common fields", target == AudioFormat.Mp3 ? null : "Older title",
                target == AudioFormat.Mp3 ? null : "Rock");
        foreach (var (version, encoding) in new[] { (2, 0), (2, 1), (3, 0), (3, 1), (4, 2), (4, 3) })
        {
            var title = Frame(version, version == 2 ? "TT2" : "TIT2", Text(encoding, "Title \u00fc"));
            var comment = "comment\0" + (encoding == 1 ? "\ufeff" : "") + "Line one\r\nLine two\n\tquotes \" and trailing\\";
            var authored = Tag(version, title, Frame(version, version == 2 ? "TXX" : "TXXX", Text(encoding, comment))).Concat(frames).ToArray();
            await Accept(authored, AudioFormat.Flac, "ID3 " + version + "/" + encoding, "Title \u00fc");
            if (version == 2 || version == 4 && encoding == 3)
            {
                foreach (var target in new[] { AudioFormat.M4a, AudioFormat.Vorbis, AudioFormat.Opus }) await Accept(authored, target, "Unicode/multiline " + version + "/" + encoding, "Title \u00fc");
                await Accept(authored, AudioFormat.Wave, "Unicode ID3 " + version + "/" + encoding, "Title \u00fc");
            }
        }
        foreach (var version in new[] { 2, 3, 4 })
        {
            var text = Text(0, "A\u00ff\u00e0B");
            var payload = version <= 3 ? Escape(Frame(version, version == 2 ? "TT2" : "TIT2", text)) : Frame(version, "TIT2", Escape(text), 2);
            var tag = Tag(version, payload); if (version <= 3) tag[5] = 128;
            var unsyncPath = Path.Combine(evidence, "mp3-unsync-v" + version + ".mp3");
            await File.WriteAllBytesAsync(unsyncPath, tag.Concat(frames).ToArray());
            using (var probe = new AudioProbeAdapter(binary))
            using (var file = new FileStream(unsyncPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var facts = await probe.ProbeSeekableAsync(file, evidence, default);
                await File.WriteAllTextAsync(unsyncPath + ".json", System.Text.Json.JsonSerializer.Serialize(facts));
            }
            Console.WriteLine("Checking ID3 unsynchronisation version " + version);
            foreach (var target in new[] { AudioFormat.Flac, AudioFormat.M4a, AudioFormat.Vorbis, AudioFormat.Opus })
                await Accept(tag.Concat(frames).ToArray(), target, "unsynchronisation " + version, "A\u00ff\u00e0B");
        }
        foreach (var (id, key, value) in new[] { ("TP2", "album_artist", "Band"), ("TPA", "disc", "1/2"),
            ("TCM", "composer", "Composer"), ("TCR", "copyright", "2026 Owner"), ("TEN", "encoded_by", "Engineer"),
            ("TPB", "publisher", "Publisher"), ("TLA", "language", "eng"), ("TP3", "performer", "Conductor"),
            ("TT1", "grouping", "Group") })
        {
            var result = await adapter.ConvertAsync(Tag(2, Frame(2, id, Text(0, value))).Concat(frames).ToArray(), AudioFormat.Flac, consent, default);
            if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || result.DecodedFrames != 96000 ||
                !result.Facts.Tags.Concat(result.Facts.Streams.SelectMany(stream => stream.Tags)).Any(tag =>
                    tag.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && tag.Value == value))
                throw new InvalidDataException("ID3v2.2 descriptive field was not preserved: " + id);
            checks.Add("MP3 preservation: ID3v2.2 " + id + " to FLAC");
            reports.Add(new { mp3Case = "ID3v2.2 " + id, result.Facts, result.DecodedFrames });
        }
        foreach (var (encoded, expected) in new[] { ("(RX)", "Remix"), ("(CR)", "Cover"), ("((Live)", "(Live)") })
            await Accept(Tag(2, Frame(2, "TCO", Text(0, encoded))).Concat(frames).ToArray(), AudioFormat.Flac, "v2.2 genre " + encoded, expectedGenre: expected);
        foreach (var id in new[] { "PIC", "GEO", "ULT", "TDA", "TIM" })
            await Reject(Tag(2, Frame(2, id, Text(0, "Extra"))).Concat(frames).ToArray(), "v2.2 unmapped " + id);
        var compressed = Tag(2, Frame(2, "TT2", Text(0, "Compressed"))); compressed[5] = 64;
        await Reject(compressed.Concat(frames).ToArray(), "v2.2 compression");
        await Reject(Tag(2, new byte[] { 84, 84, 50, 255, 255, 255, 0 }).Concat(frames).ToArray(), "v2.2 truncated frame");
        await Accept(Tag(4, Frame(4, "COMM", new byte[] { 3 }.Concat("und\0A comment"u8.ToArray()).ToArray())).Concat(frames).ToArray(), AudioFormat.Flac, "undefined-language comment");
        foreach (var frame in new[] { Frame(4, "PRIV", Text(3, "Private data")), Frame(4, "GEOB", Text(3, "Object")),
            Frame(4, "POPM", Text(3, "Rating")), Frame(4, "TXXX", Text(3, "REPLAYGAIN_TRACK_GAIN\0-3 dB")),
            Frame(4, "COMM", new byte[] { 3 }.Concat("eng\0English"u8.ToArray()).ToArray()),
            Frame(4, "TIT2", Text(3, "First\0Second")), Frame(4, "TCON", Text(3, "(17)(20)")) })
            await Reject(Tag(4, frame).Concat(frames).ToArray(), "unmapped ID3 information");
        await Reject(Tag(4, Frame(4, "TPE1", Text(3, "One")), Frame(4, "TPE1", Text(3, "Two"))).Concat(frames).ToArray(), "duplicate artist");
        var legacy = Legacy("Legacy title", "Artist", "Album", "1997", "Comment", 7, 17);
        var legacyOnly = frames.Concat(legacy).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidence, "mp3-legacy.mp3"), legacyOnly);
        foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem))
            await Accept(legacyOnly, target, "ID3v1.1 fields", "Legacy title", "Rock");
        foreach (var (encoded, expected) in new[] { ("(17)", "Rock"), ("(147)", "SynthPop"), ("(RX)", "Remix"), ("CR", "Cover"), ("((Live)", "(Live)") })
            await Accept(Tag(3, Frame(3, "TCON", Text(0, encoded))).Concat(frames).ToArray(), AudioFormat.Flac, "genre " + encoded, expectedGenre: expected);
        foreach (var (encoded, expected) in new[] { ("17", "Rock"), ("((Live)", "((Live)") })
            await Accept(Tag(4, Frame(4, "TCON", Text(3, encoded))).Concat(frames).ToArray(), AudioFormat.Flac, "v2.4 genre " + encoded, expectedGenre: expected);
        var unicodeLegacy = frames.Concat(Legacy("Caf\u00e9", "", "", "", new string('c', 30), 0, 255)).ToArray();
        foreach (var target in new[] { AudioFormat.Flac, AudioFormat.M4a, AudioFormat.Vorbis, AudioFormat.Opus })
            await Accept(unicodeLegacy, target, "ID3v1 Latin-1", "Caf\u00e9");
        await Accept(unicodeLegacy, AudioFormat.Wave, "Unicode legacy ID3");
        var longTitle = new string('T', 30) + " full title";
        var combined = Tag(4, Frame(4, "TIT2", Text(3, longTitle)), Frame(4, "TDRC", Text(3, "1997-08-16")),
            Frame(4, "TRCK", Text(3, "07/12")), Frame(4, "TCON", Text(3, "(17)"))).Concat(frames)
            .Concat(Legacy(longTitle[..30], "Artist", "Album", "1997", "Comment", 7, 17)).ToArray();
        await Accept(combined, AudioFormat.Flac, "agreeing v1/v2 fields", longTitle, "Rock");
        await File.WriteAllBytesAsync(Path.Combine(evidence, "mp3-legacy-combined.mp3"), combined);
        foreach (var (id, value) in new[] { ("TIT2", "Different title"), ("TRCK", "8"), ("TCON", "Pop"), ("TDRC", "1998") })
            await Reject(Tag(4, Frame(4, id, Text(3, value))).Concat(frames).Concat(legacy).ToArray(), "conflicting v1/v2 " + id);
        await Reject(frames.Concat(legacy).Concat(legacy).ToArray(), "duplicate legacy trailer");
        await Reject(source.Concat(new byte[] { 1, 2, 3, 4 }).ToArray(), "trailing bytes");
        await Reject(source[..^3], "truncated MPEG frame");
        foreach (var rate in new[] { 8000, 16000, 24000 })
        {
            var encoded = await adapter.ConvertAsync(EncodingContracts.Wave(16, false, rate, 1), AudioFormat.Mp3, consent, default);
            var decoded = await adapter.ConvertAsync(encoded.Bytes, AudioFormat.Wave, consent, default);
            if (!decoded.SourceMetadataVerified || decoded.Facts.Streams[0].SampleRate != rate || decoded.DecodedFrames != rate / 5)
                throw new InvalidDataException("Low-rate MP3 inventory or gapless decode changed.");
            checks.Add("MP3 inventory: low-rate MPEG and gapless decode " + rate);
            reports.Add(new { mp3Rate = rate, decoded.Facts, decoded.DecodedFrames });
        }
        if (!SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(hash) || Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new IOException("MP3 source changed or owned scratch remains.");
        checks.Add("MP3 inventory: original digest and cleanup across accepted/refused conversions");

        async Task Accept(byte[] bytes, AudioFormat target, string name, string? expectedTitle = null, string? expectedGenre = null)
        {
            var result = await adapter.ConvertAsync(bytes, target, consent, default);
            if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || (!result.Plan.AlreadyTarget && result.DecodedFrames != 96000))
                throw new InvalidDataException("MP3 did not retain inventory or gapless frames: " + name + " to " + target);
            if (result.Plan.AlreadyTarget && !result.Bytes.AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException("MP3 same-format retention changed its bytes.");
            if (expectedTitle is not null && !result.Facts.Tags.Concat(result.Facts.Streams.SelectMany(stream => stream.Tags))
                .Any(tag => tag.Key.Equals("title", StringComparison.OrdinalIgnoreCase) && tag.Value == expectedTitle))
                throw new InvalidDataException("MP3 unsynchronised title was not retained exactly.");
            if (expectedGenre is not null && !result.Facts.Tags.Concat(result.Facts.Streams.SelectMany(stream => stream.Tags))
                .Any(tag => tag.Key.Equals("genre", StringComparison.OrdinalIgnoreCase) && tag.Value == expectedGenre))
                throw new InvalidDataException("MP3 genre was not retained exactly.");
            checks.Add("MP3 preservation: " + name + " to " + target);
            reports.Add(new { mp3Case = name, target = target.ToString(), result.Facts, result.DecodedFrames });
        }
        async Task Reject(byte[] bytes, string name, AudioFormat target = AudioFormat.Flac)
        {
            try { await adapter.ConvertAsync(bytes, target, consent, default); throw new Exception("MP3 omitted or admitted " + name); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { checks.Add("MP3 preservation refuses " + name); }
            if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new IOException("MP3 refusal retained scratch.");
        }
    }
    private static byte[] Legacy(string title, string artist, string album, string year, string comment, byte track, byte genre)
    {
        var bytes = new byte[128]; "TAG"u8.CopyTo(bytes);
        foreach (var (value, offset, length) in new[] { (title, 3, 30), (artist, 33, 30), (album, 63, 30), (year, 93, 4), (comment, 97, track == 0 ? 30 : 28) })
            Encoding.Latin1.GetBytes(value.AsSpan(), bytes.AsSpan(offset, length));
        if (track != 0) bytes[126] = track;
        bytes[127] = genre; return bytes;
    }
    private static byte[] Tag(int version, params byte[][] frames)
    {
        var data = frames.SelectMany(frame => frame).ToArray();
        return new byte[] { 73, 68, 51, (byte)version, 0, 0 }.Concat(Size(data.Length)).Concat(data).ToArray();
    }
    private static byte[] Frame(int version, string id, byte[] data, ushort flags = 0)
    {
        if (version == 2)
            return Encoding.ASCII.GetBytes(id).Concat(new byte[] { (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length }).Concat(data).ToArray();
        var header = new byte[10]; Encoding.ASCII.GetBytes(id).CopyTo(header, 0);
        if (version == 4) Size(data.Length).CopyTo(header, 4); else BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), data.Length);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), flags); return header.Concat(data).ToArray();
    }
    private static byte[] Text(int encoding, string text) => new byte[] { (byte)encoding }.Concat(encoding switch
    {
        0 => Encoding.Latin1.GetBytes(text), 1 => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray(),
        2 => Encoding.BigEndianUnicode.GetBytes(text), _ => Encoding.UTF8.GetBytes(text)
    }).ToArray();
    private static byte[] Size(int length) => [(byte)((length >> 21) & 127), (byte)((length >> 14) & 127), (byte)((length >> 7) & 127), (byte)(length & 127)];
    private static byte[] Escape(byte[] data)
    {
        using var stream = new MemoryStream();
        for (var i = 0; i < data.Length; i++) { stream.WriteByte(data[i]); if (data[i] == 255 && (i + 1 == data.Length || data[i + 1] == 0 || data[i + 1] >= 224)) stream.WriteByte(0); }
        return stream.ToArray();
    }
}
