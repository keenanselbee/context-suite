using System.Buffers.Binary;
using System.Text;
using ContextSuite.Core.Audio;

internal static partial class Mp3MetadataContracts
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        await PicturesAsync(check);
        await OutputPicturesAsync(check);
        foreach (var (version, encoding) in new[] { (2, 0), (2, 1), (3, 0), (3, 1), (4, 2), (4, 3) })
        {
            var bytes = FileOf(Tag(version, Frame(version, version == 2 ? "TT2" : "TIT2", Text(encoding, "Title \u00fc")),
                Frame(version, version == 2 ? "TXX" : "TXXX", Text(encoding, "comment\0Line one\r\nLine two"))));
            using var stream = new MemoryStream(bytes); stream.Position = 5;
            var inventory = await Mp3Metadata.ReadAsync(stream, default);
            check(inventory.SampleRate == 48000 && inventory.Channels == 2 && inventory.AudioFrames == 2 &&
                inventory.Tags["title"] == "Title \u00fc" && inventory.Tags["comment"] == "Line one\r\nLine two" && stream.Position == 5,
                "MP3 inventory: ID3 version/text encoding and literal comments: " + version + "/" + encoding);
        }
        foreach (var version in new[] { 2, 3, 4 })
        {
            var data = Text(0, "A\u00ff\u00e0B");
            var payload = version <= 3 ? Escape(Frame(version, version == 2 ? "TT2" : "TIT2", data)) : Frame(version, "TIT2", Escape(data), 2);
            var tag = Tag(version, payload); if (version <= 3) tag[5] = 128;
            using var stream = new MemoryStream(FileOf(tag));
            check((await Mp3Metadata.ReadAsync(stream, default)).Tags["title"] == "A\u00ff\u00e0B", "MP3 inventory: unsynchronisation version " + version);
        }
        foreach (var (id, key, value) in new[] { ("TT2", "title", "Title"), ("TP1", "artist", "Artist"),
            ("TP2", "album_artist", "Band"), ("TAL", "album", "Album"), ("TRK", "track", "2/9"),
            ("TPA", "disc", "1/2"), ("TCM", "composer", "Composer"), ("TCR", "copyright", "2026 Owner"),
            ("TEN", "encoded_by", "Engineer"), ("TPB", "publisher", "Publisher"), ("TLA", "language", "eng"),
            ("TP3", "performer", "Conductor"), ("TYE", "date", "1997"), ("TT1", "grouping", "Group"),
            ("TCO", "genre", "Rock") })
        {
            using var stream = new MemoryStream(FileOf(Tag(2, Frame(2, id, Text(0, value)))));
            var tags = (await Mp3Metadata.ReadAsync(stream, default)).Tags;
            check(tags.Count == 1 && tags[key] == value, "MP3 inventory: v2.2 field " + id);
        }
        foreach (var (encoded, expected) in new[] { ("(17)", "Rock"), ("(17)Rock", "Rock"),
            ("(RX)", "Remix"), ("(CR)", "Cover"), ("((Live)", "(Live)") })
        {
            using var stream = new MemoryStream(FileOf(Tag(2, Frame(2, "TCO", Text(0, encoded)))));
            check((await Mp3Metadata.ReadAsync(stream, default)).Tags["genre"] == expected, "MP3 inventory: v2.2 genre " + encoded);
        }
        using (var stream = new MemoryStream(FileOf(Tag(2, Frame(2, "TSS", Text(0, new string('e', 65536))),
            Frame(2, "COM", new byte[] { 0 }.Concat("und\0Comment"u8.ToArray()).ToArray()), new byte[3]))))
        {
            var tags = (await Mp3Metadata.ReadAsync(stream, default)).Tags;
            check(tags.Count == 1 && tags["comment"] == "Comment", "MP3 inventory: v2.2 24-bit size, following comment and short padding");
        }
        foreach (var id in new[] { "PIC", "GEO", "POP", "ULT", "CRM", "TDA", "TIM", "XYZ" })
            await Reject(FileOf(Tag(2, Frame(2, id, Text(0, "Extra")))), "unmapped v2.2 frame " + id);
        foreach (var encoding in new[] { 2, 3 })
            await Reject(FileOf(Tag(2, Frame(2, "TT2", Text(encoding, "Wrong")))), "v2.2 later encoding " + encoding);
        await Reject(FileOf(Tag(2, Frame(2, "TT2", [1, 65, 0]))), "v2.2 ambiguous Unicode without BOM");
        await Reject(FileOf(Tag(2, Frame(2, "TT2", Text(0, "One")), Frame(2, "TT2", Text(0, "Two")))), "v2.2 duplicate title");
        await Reject(FileOf(Tag(2, Frame(2, "TCO", Text(0, "(17)(20)")))), "v2.2 multiple genres");
        await Reject(FileOf(Tag(2, Frame(2, "TXX", Text(0, "REPLAYGAIN_TRACK_GAIN\0-3 dB")))), "v2.2 gain semantics");
        await Reject(FileOf(Tag(2, Frame(2, "COM", new byte[] { 0 }.Concat("eng\0English"u8.ToArray()).ToArray()))), "v2.2 comment language");
        await Reject(FileOf(Tag(2, "TT2\0\0"u8.ToArray())), "v2.2 truncated frame header");
        await Reject(FileOf(Tag(2, "TT2\0\0\0"u8.ToArray())), "v2.2 zero frame size");
        await Reject(FileOf(Tag(2, new byte[] { 84, 84, 50, 255, 255, 255, 0 })), "v2.2 24-bit extent beyond tag");
        foreach (var flag in new byte[] { 1, 16, 32, 64 })
        {
            var tag = Tag(2, Frame(2, "TT2", Text(0, "Flag"))); tag[5] = flag;
            await Reject(FileOf(tag), "v2.2 unsupported tag flag " + flag);
        }
        var footerTag = Tag(4, Frame(4, "TIT2", Text(3, "Footer"))); footerTag[5] = 16;
        var footer = footerTag[..10].ToArray(); "3DI"u8.CopyTo(footer);
        using (var stream = new MemoryStream(FileOf(footerTag.Concat(footer).ToArray())))
            check((await Mp3Metadata.ReadAsync(stream, default)).Tags["title"] == "Footer", "MP3 inventory: matched ID3 footer");
        footer[4] = 1; await Reject(FileOf(footerTag.Concat(footer).ToArray()), "mismatched footer");
        var indicated = Text(3, "Indicated");
        using (var stream = new MemoryStream(FileOf(Tag(4, Frame(4, "TIT2", Size(indicated.Length).Concat(indicated).ToArray(), 1)))))
            check((await Mp3Metadata.ReadAsync(stream, default)).Tags["title"] == "Indicated", "MP3 inventory: data-length indicator");
        await Reject(FileOf(Tag(4, Frame(4, "TIT2", Size(999).Concat(indicated).ToArray(), 1))), "wrong data length indicator");
        foreach (var id in new[] { "APIC", "CHAP", "CTOC", "GEOB", "PRIV", "POPM", "RVA2", "USLT" })
            await Reject(FileOf(Tag(4, Frame(4, id, Text(3, "Extra")))), "unmapped frame " + id);
        await Reject(FileOf(Tag(4, Frame(4, "TIT2", Text(3, "One")), Frame(4, "TIT2", Text(3, "Two")))), "duplicate tags");
        await Reject(FileOf(Tag(4, Frame(4, "TIT2", Text(3, "One\0Two")))), "multiple text values");
        foreach (var (encoded, expected) in new[] { ("0", "Blues"), ("(17)", "Rock"), ("(017)Rock", "Rock"),
            ("125", "Dance Hall"), ("147", "SynthPop"), ("(RX)", "Remix"), ("CR", "Cover"), ("((Live)", "(Live)") })
        {
            using var stream = new MemoryStream(FileOf(Tag(3, Frame(3, "TCON", Text(0, encoded)))));
            check((await Mp3Metadata.ReadAsync(stream, default)).Tags["genre"] == expected, "MP3 inventory: canonical genre " + encoded);
        }
        foreach (var encoded in new[] { "(17)(20)", "(17)Indie", "(999)", "148", "99999999999999999", "()", "(17" })
            await Reject(FileOf(Tag(4, Frame(4, "TCON", Text(3, encoded)))), "unsupported genre " + encoded);
        foreach (var (encoded, expected) in new[] { ("17", "Rock"), ("(17)", "Rock"), ("(Live)", "(Live)"), ("((Live)", "((Live)") })
        {
            using var stream = new MemoryStream(FileOf(Tag(4, Frame(4, "TCON", Text(3, encoded)))));
            check((await Mp3Metadata.ReadAsync(stream, default)).Tags["genre"] == expected, "MP3 inventory: v2.4 numeric or literal genre " + encoded);
        }
        await Reject(FileOf(Tag(4, Frame(4, "TXXX", Text(3, "REPLAYGAIN_TRACK_GAIN\0-3 dB")))), "custom gain semantics");
        foreach (ushort flags in new ushort[] { 4, 8, 64, 4096 })
            await Reject(FileOf(Tag(4, Frame(4, "TIT2", Text(3, "Flag"), flags))), "unsupported frame flags");
        foreach (var flags in new byte[] { 1, 32, 64 })
        {
            var tag = Tag(4, Frame(4, "TIT2", Text(3, "Flag"))); tag[5] = flags;
            await Reject(FileOf(tag), "unsupported tag flags");
        }
        await Reject(FileOf(Tag(3, Frame(3, "TIT2", Text(3, "UTF8")))), "v2.3 UTF8 marker");
        await Reject(FileOf(Tag(4, Frame(4, "TIT2", [3, 255]))), "invalid UTF8");
        await Reject(FileOf(Tag(4, Frame(4, "TIT2", [1, 65, 0]))), "missing UTF16 BOM");
        await Reject(FileOf(Tag(4, Frame(4, "TIT2", [2, 0]))), "odd UTF16 length");
        var badSize = Tag(4, Frame(4, "TIT2", Text(3, "Bad"))); badSize[6] = 128;
        await Reject(FileOf(badSize), "non-synchsafe tag size");
        var padding = Tag(4, Frame(4, "TIT2", Text(3, "Good")), new byte[10]); padding[^1] = 1;
        await Reject(FileOf(padding), "nonzero padding");
        await Reject(FileOf(Tag(4, Frame(4, "COMM", new byte[] { 3 }.Concat("eng\0English"u8.ToArray()).ToArray()))), "comment language mapping");
        using (var stream = new MemoryStream(FileOf(Tag(4, Frame(4, "COMM", new byte[] { 3 }.Concat("und\0Neutral"u8.ToArray()).ToArray())))))
            check((await Mp3Metadata.ReadAsync(stream, default)).Tags["comment"] == "Neutral", "MP3 inventory: undefined-language unnamed comment");
        var legacy = Legacy("Legacy title", "Artist", "Album", "1997", "Comment", 7, 17);
        using (var stream = new MemoryStream(FileOf([]).Concat(legacy).ToArray()))
        {
            stream.Position = 11; var result = await Mp3Metadata.ReadAsync(stream, default);
            check(result.Tags.Count == 7 && result.Tags["title"] == "Legacy title" && result.Tags["artist"] == "Artist" &&
                result.Tags["album"] == "Album" && result.Tags["date"] == "1997" && result.Tags["comment"] == "Comment" &&
                result.Tags["track"] == "7" && result.Tags["genre"] == "Rock" && result.AudioFrames == 2 && stream.Position == 11,
                "MP3 inventory: complete ID3v1.1 metadata and frame extent");
        }
        using (var stream = new MemoryStream(FileOf([]).Concat(Legacy("Caf\u00e9", "", "", "", new string('c', 30), 0, 255)).ToArray()))
        {
            var result = await Mp3Metadata.ReadAsync(stream, default);
            check(result.Tags.Count == 2 && result.Tags["title"] == "Caf\u00e9" && result.Tags["comment"].Length == 30,
                "MP3 inventory: Latin-1, full v1.0 comment and unclassified genre");
        }
        var longTitle = new string('T', 30) + " full title";
        var modern = Tag(4, Frame(4, "TIT2", Text(3, longTitle)), Frame(4, "TDRC", Text(3, "1997-08-16")),
            Frame(4, "TRCK", Text(3, "07/12")), Frame(4, "TCON", Text(3, "Rock")));
        using (var stream = new MemoryStream(FileOf(modern).Concat(Legacy(longTitle[..30], "Artist", "Album", "1997", "Comment", 7, 17)).ToArray()))
        {
            var result = await Mp3Metadata.ReadAsync(stream, default);
            check(result.Tags["title"] == longTitle && result.Tags["date"] == "1997-08-16" && result.Tags["track"] == "07/12" &&
                result.Tags["artist"] == "Artist", "MP3 inventory: agreeing v1 truncation/year/track retains richer v2 and adds missing values");
        }
        foreach (var (id, value) in new[] { ("TIT2", "Other title"), ("TPE1", "Other artist"), ("TALB", "Other album"),
            ("TDRC", "1998"), ("TRCK", "8/12"), ("TCON", "Pop"), ("TXXX", "comment\0Other comment") })
            await Reject(FileOf(Tag(4, Frame(4, id, Text(3, value)))).Concat(legacy).ToArray(), "conflicting legacy " + id);
        await Reject(FileOf(Tag(4, Frame(4, "TIT2", Text(3, "Legacy title extended")))).Concat(legacy).ToArray(), "padded legacy prefix is not fixed-width truncation");
        var hidden = legacy.ToArray(); hidden[18] = 65;
        await Reject(FileOf([]).Concat(hidden).ToArray(), "hidden legacy text after terminator");
        var codePage = legacy.ToArray(); codePage[3] = 128;
        await Reject(FileOf([]).Concat(codePage).ToArray(), "legacy control/code-page ambiguity");
        var unknownGenre = legacy.ToArray(); unknownGenre[127] = 254;
        await Reject(FileOf([]).Concat(unknownGenre).ToArray(), "unknown legacy genre");
        await Reject(FileOf([]).Concat(legacy).Concat(legacy).ToArray(), "multiple legacy trailers");
        await Reject(FileOf([])[..^1].Concat(legacy).ToArray(), "legacy trailer cannot conceal truncated audio");
        await Reject(FileOf([]).Concat(legacy[..^1]).ToArray(), "truncated legacy trailer");
        await Reject(FileOf([]).Concat("APETAGEX"u8.ToArray()).ToArray(), "trailing APE");
        await Reject(FileOf([]).Concat(new byte[] { 1, 2, 3, 4 }).ToArray(), "undeclared trailing bytes");
        await Reject(FileOf([])[..^1], "truncated MPEG frame");
        foreach (var last in new byte[] { 1, 3, 8 })
        {
            var bytes = FileOf([]); bytes[3] = last;
            await Reject(bytes, "emphasis/copyright needs policy");
        }
        var free = FileOf([]); free[2] &= 15; await Reject(free, "free-format MP3");
        var mode = FileOf([]); mode[384 + 3] |= 192; await Reject(mode, "channel change");
        await Reject(Tag(4, Frame(4, "TIT2", Text(3, "No audio"))), "no MPEG frames");
        await Reject(FileOf(Tag(4, new byte[Mp3Metadata.MaximumTagBytes + 1])), "tag byte budget");
        await Reject(FileOf(Tag(4, Enumerable.Repeat(Frame(4, "TSSE", Text(3, "Encoder")), Mp3Metadata.MaximumTagFrames + 1).ToArray())), "tag frame budget");
        var missingEscape = Tag(3, Frame(3, "TIT2", Text(0, "A\u00ff\u00e0B"))); missingEscape[5] = 128;
        await Reject(FileOf(missingEscape), "missing unsynchronisation escape");
        using var bounded = new CountingStream(Enumerable.Repeat(AudioFrame(), 3000).SelectMany(frame => frame).ToArray());
        var large = await Mp3Metadata.ReadAsync(bounded, default);
        check(large.AudioFrames == 3000 && bounded.BytesRead == 12131, "MP3 inventory: seeks past over 1 MiB of compressed samples plus one bounded trailer check");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bounded.Position = 9;
        try { await Mp3Metadata.ReadAsync(bounded, cancelled.Token); check(false, "MP3 inventory: cancellation"); }
        catch (OperationCanceledException) { check(bounded.Position == 9, "MP3 inventory: cancellation restores position"); }

        async Task Reject(byte[] bytes, string name)
        {
            using var stream = new MemoryStream(bytes); stream.Position = 2;
            try { await Mp3Metadata.ReadAsync(stream, default); check(false, "MP3 inventory rejects " + name); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            { check(stream.Position == 2, "MP3 inventory rejects " + name + " and restores position"); }
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
    internal static byte[] Tag(int version, params byte[][] frames)
    {
        var payload = frames.SelectMany(frame => frame).ToArray();
        return new byte[] { 73, 68, 51, (byte)version, 0, 0 }.Concat(Size(payload.Length)).Concat(payload).ToArray();
    }
    internal static byte[] Frame(int version, string id, byte[] data, ushort flags = 0)
    {
        if (version == 2)
            return Encoding.ASCII.GetBytes(id).Concat(new byte[] { (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length }).Concat(data).ToArray();
        var header = new byte[10]; Encoding.ASCII.GetBytes(id).CopyTo(header, 0);
        if (version == 4) Size(data.Length).CopyTo(header, 4); else BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), data.Length);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), flags); return header.Concat(data).ToArray();
    }
    internal static byte[] Text(int encoding, string text) => new byte[] { (byte)encoding }.Concat(encoding switch
    {
        0 => Encoding.Latin1.GetBytes(text), 1 => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray(),
        2 => Encoding.BigEndianUnicode.GetBytes(text), _ => Encoding.UTF8.GetBytes(text)
    }).ToArray();
    private static byte[] FileOf(byte[] tag) => tag.Concat(AudioFrame()).Concat(AudioFrame()).ToArray();
    private static byte[] AudioFrame() { var bytes = new byte[384]; bytes[0] = 255; bytes[1] = 251; bytes[2] = 148; return bytes; }
    private static byte[] Size(int length) => [(byte)((length >> 21) & 127), (byte)((length >> 14) & 127), (byte)((length >> 7) & 127), (byte)(length & 127)];
    private static byte[] Escape(byte[] data)
    {
        using var stream = new MemoryStream();
        for (var i = 0; i < data.Length; i++) { stream.WriteByte(data[i]); if (data[i] == 255 && (i + 1 == data.Length || data[i + 1] == 0 || data[i + 1] >= 224)) stream.WriteByte(0); }
        return stream.ToArray();
    }
    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public long BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { var count = await base.ReadAsync(buffer, cancellationToken); BytesRead += count; return count; }
    }
}
