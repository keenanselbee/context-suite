using System.Buffers.Binary;
using System.Text;
using ContextSuite.Core.Audio;

internal static class Mp3MetadataContracts
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var (version, encoding) in new[] { (3, 0), (3, 1), (4, 2), (4, 3) })
        {
            var bytes = FileOf(Tag(version, Frame(version, "TIT2", Text(encoding, "Title \u00fc")),
                Frame(version, "TXXX", Text(encoding, "comment\0Line one\r\nLine two"))));
            using var stream = new MemoryStream(bytes); stream.Position = 5;
            var inventory = await Mp3Metadata.ReadAsync(stream, default);
            check(inventory.SampleRate == 48000 && inventory.Channels == 2 && inventory.AudioFrames == 2 &&
                inventory.Tags["title"] == "Title \u00fc" && inventory.Tags["comment"] == "Line one\r\nLine two" && stream.Position == 5,
                "MP3 inventory: ID3 version/text encoding and literal comments: " + version + "/" + encoding);
        }
        foreach (var version in new[] { 3, 4 })
        {
            var data = Text(0, "A\u00ff\u00e0B");
            var payload = version == 3 ? Escape(Frame(version, "TIT2", data)) : Frame(version, "TIT2", Escape(data), 2);
            var tag = Tag(version, payload); if (version == 3) tag[5] = 128;
            using var stream = new MemoryStream(FileOf(tag));
            check((await Mp3Metadata.ReadAsync(stream, default)).Tags["title"] == "A\u00ff\u00e0B", "MP3 inventory: unsynchronisation version " + version);
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
        await Reject(FileOf(Tag(4, Frame(4, "TCON", Text(3, "(17)")))), "numeric genre interpretation");
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
        await Reject(FileOf([]).Concat("TAG"u8.ToArray()).Concat(new byte[125]).ToArray(), "trailing ID3v1");
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
        check(large.AudioFrames == 3000 && bounded.BytesRead == 12003, "MP3 inventory: seeks past over 1 MiB of compressed samples");
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
    internal static byte[] Tag(int version, params byte[][] frames)
    {
        var payload = frames.SelectMany(frame => frame).ToArray();
        return new byte[] { 73, 68, 51, (byte)version, 0, 0 }.Concat(Size(payload.Length)).Concat(payload).ToArray();
    }
    internal static byte[] Frame(int version, string id, byte[] data, ushort flags = 0)
    {
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
