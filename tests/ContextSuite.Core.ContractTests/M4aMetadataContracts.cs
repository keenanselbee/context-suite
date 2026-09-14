using System.Buffers.Binary;
using System.Text;
using ContextSuite.Core.Audio;

internal static partial class M4aMetadataContracts
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        await PicturesAsync(check);
        await OutputPicturesAsync(check);
        var basic = FileOf();
        using (var stream = new MemoryStream(basic))
        {
            stream.Position = 3; var result = await M4aMetadata.ReadAsync(stream, default);
            check(result.SampleRate == 48000 && result.Channels == 2 && result.PacketCount == 2 &&
                result.Tags["title"] == "Title \u00fc" && result.Tags["language"] == "und" && stream.Position == 3,
                "M4A inventory: local AAC, tags, sample table and caller position");
        }
        using (var stream = new MemoryStream(FileOf(tags: [Text("\u00a9cmt", "Comment"), Text("desc", "Description"),
            Box("trkn", Box("data", new byte[8], [0, 0, 0, 2, 0, 9, 0, 0])),
            Box("disk", Box("data", new byte[8], [0, 0, 0, 1, 0, 0]))])))
        {
            var result = await M4aMetadata.ReadAsync(stream, default);
            check(result.Tags["comment"] == "Comment" && result.Tags["description"] == "Description" &&
                result.Tags["track"] == "2/9" && result.Tags["disc"] == "1", "M4A inventory: distinct descriptions and track/disc totals");
        }
        using (var stream = new MemoryStream(FileOf(tags: [Box("\u00a9nam", Box("data", N(2), N(0), Encoding.BigEndianUnicode.GetBytes("Title \u00fc")))])))
            check((await M4aMetadata.ReadAsync(stream, default)).Tags["title"] == "Title \u00fc", "M4A inventory: UTF16BE text");
        var tablePath = new[] { "moov", "trak", "mdia", "minf", "stbl" };
        var chunks = Replace(basic, tablePath, data => Replace(data, ["stco"], _ => N(0).Concat(N(2)).Concat(N(32)).Concat(N(28)).ToArray()));
        chunks = Change(chunks, "stsc", 12, 1);
        using (var stream = new MemoryStream(chunks))
            check((await M4aMetadata.ReadAsync(stream, default)).PacketCount == 2, "M4A inventory: physical chunk order differs from time order");
        await Reject(Change(chunks, "stco", 12, 32), "overlapping chunks");
        var wideOffsets = Replace(basic, tablePath, data =>
        {
            var replaced = Replace(data, ["stco"], _ => N(0).Concat(N(1)).Concat(N(0)).Concat(N(28)).ToArray());
            "co64"u8.CopyTo(replaced.AsSpan(replaced.AsSpan().IndexOf("stco"u8))); return replaced;
        });
        using (var stream = new MemoryStream(wideOffsets))
            check((await M4aMetadata.ReadAsync(stream, default)).PacketCount == 2, "M4A inventory: 64-bit chunk offsets");
        await Reject(Change(wideOffsets, "co64", 8, uint.MaxValue), "overflowing chunk offset");
        var variableSizes = Replace(basic, tablePath, data => Replace(data, ["stsz"], _ => N(0).Concat(N(0)).Concat(N(2)).Concat(N(3)).Concat(N(5)).ToArray()));
        using (var stream = new MemoryStream(variableSizes))
            check((await M4aMetadata.ReadAsync(stream, default)).PacketCount == 2, "M4A inventory: variable sample sizes");
        await Reject(Change(variableSizes, "stsz", 12, 0), "zero sample size");
        var roll = Replace(basic, tablePath, data => data.Concat(Box("sgpd", [1, 0, 0, 0], "roll"u8.ToArray(), N(2), N(1), [255, 255]))
            .Concat(Box("sbgp", N(0), "roll"u8.ToArray(), N(1), N(2), N(1))).ToArray());
        using (var stream = new MemoryStream(roll))
            check((await M4aMetadata.ReadAsync(stream, default)).PacketCount == 2, "M4A inventory: paired roll-recovery groups");
        await Reject(Change(roll, "sbgp", 16, 2), "unknown roll group index");
        await Reject(Change(roll, "sbgp", 12, 1), "incomplete roll coverage");
        var edit = Replace(basic, ["moov", "trak"], data => data.Concat(Box("edts", Box("elst", N(0), N(1), N(1024), N(1024), N(65536)))).ToArray());
        edit = Change(Change(edit, "mvhd", 16, 1024), "tkhd", 20, 1024);
        using (var stream = new MemoryStream(edit))
            check((await M4aMetadata.ReadAsync(stream, default)).PacketCount == 2, "M4A inventory: one normal-rate priming edit");
        await Reject(Change(edit, "elst", 12, 1025), "edit beyond media duration");
        await Reject(Change(edit, "elst", 16, 131072), "edit playback speed");
        await Reject(Change(edit, "elst", 4, 2), "multiple edits");
        var large = FileOf(sampleBytes: 600000);
        using (var stream = new CountingStream(large))
        {
            var result = await M4aMetadata.ReadAsync(stream, default);
            check(result.PacketCount == 2 && stream.BytesRead == large.Length - 1200000,
                "M4A inventory: seeks past over one MiB of encoded samples");
        }
        foreach (var name in new[] { "covr", "----", "gnre", "uuid", "cpil" })
            await Reject(FileOf(tags: [Text(name, "Extra")]), "unsupported metadata " + name);
        await Reject(FileOf(tags: [Text("\u00a9nam", "One"), Text("\u00a9nam", "Two")]), "duplicate title");
        await Reject(FileOf(tags: [Text("\u00a9nam", "One\0Two")]), "multiple text values");
        await Reject(FileOf(tags: [Text("\u00a9nam", new string('A', 4097))]), "text value budget");
        await Reject(FileOf(tags: [Box("\u00a9nam", Box("data", N(1), N(0), [255]))]), "invalid UTF8");
        await Reject(FileOf(tags: [Box("\u00a9nam", Box("data", N(2), N(0), [0]))]), "odd UTF16");
        await Reject(FileOf(tags: [Box("\u00a9nam", Box("data", N(1), N(1), "Locale"u8.ToArray()))]), "localized text");
        await Reject(FileOf(external: true), "external resource reference");
        await Reject(FileOf(offset: 1), "chunk outside media data");
        await Reject(FileOf(offset: 29), "chunk crosses media extent");
        await Reject(FileOf(extraMedia: true), "unreferenced media bytes");
        await Reject(FileOf(sampleCount: 3), "samples disagree with timing");
        await Reject(FileOf(sampleCount: 1000001), "sample count budget");
        await Reject(FileOf(config: [0x2b, 0x90]), "HE-AAC configuration");
        await Reject(FileOf(config: [0x11, 0xc0]), "reserved channel configuration");
        await Reject(FileOf(config: [0x11, 0x91]), "dependent AAC coding");
        foreach (var (type, offset, value) in new[] { ("mvhd", 4, 1U), ("tkhd", 4, 1U), ("mdhd", 8, 1U),
            ("mvhd", 20, 131072U), ("tkhd", 76, 1U), ("mdhd", 12, 44100U), ("stsc", 16, 2U), ("stts", 12, 1023U) })
            await Reject(Change(basic, type, offset, value), "unsupported or inconsistent " + type + " field " + offset);
        foreach (var type in new[] { "moof", "pssh", "uuid" }) await Reject(basic.Concat(Box(type, [])).ToArray(), "root " + type);
        await Reject(basic[..^1], "truncated root atom");
        await Reject(basic.Concat(new byte[] { 1, 2, 3 }).ToArray(), "trailing fragment");
        var extended = Box("ftyp", "M4A "u8.ToArray(), N(0));
        extended = extended.Concat(N(1)).Concat("moov"u8.ToArray()).Concat(new byte[] { 127, 255, 255, 255, 255, 255, 255, 255 }).ToArray();
        await Reject(extended, "overflowing extended atom");
        var zeroMedia = basic.ToArray(); N(0).CopyTo(zeroMedia, 20);
        await Reject(zeroMedia, "zero-sized media swallows movie");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var cancelledStream = new MemoryStream(basic); cancelledStream.Position = 5;
        try { await M4aMetadata.ReadAsync(cancelledStream, cancelled.Token); check(false, "M4A cancellation"); }
        catch (OperationCanceledException) { check(cancelledStream.Position == 5, "M4A cancellation restores caller position"); }

        async Task Reject(byte[] bytes, string name)
        {
            using var stream = new MemoryStream(bytes); stream.Position = 2;
            try { await M4aMetadata.ReadAsync(stream, default); check(false, "M4A rejects " + name); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            { check(stream.Position == 2, "M4A rejects " + name + " and restores position"); }
        }
    }
    internal static byte[] FileOf(byte[][]? tags = null, bool external = false, uint offset = 28, bool extraMedia = false,
        uint sampleCount = 2, byte[]? config = null, uint sampleBytes = 4)
    {
        var mvhd = new byte[100]; N(48000).CopyTo(mvhd, 12); N(2048).CopyTo(mvhd, 16); N(65536).CopyTo(mvhd, 20); mvhd[24] = 1; Matrix(mvhd, 36); N(2).CopyTo(mvhd, 96);
        var tkhd = new byte[84]; tkhd[3] = 3; N(1).CopyTo(tkhd, 12); N(2048).CopyTo(tkhd, 20); tkhd[36] = 1; Matrix(tkhd, 40);
        var mdhd = new byte[24]; N(48000).CopyTo(mdhd, 12); N(2048).CopyTo(mdhd, 16); mdhd[20] = 0x55; mdhd[21] = 0xc4;
        var entry = new byte[28]; entry[7] = 1; entry[17] = 2; entry[19] = 16; N(48000U << 16).CopyTo(entry, 24);
        var decoder = new byte[13]; decoder[0] = 0x40; decoder[1] = 0x15;
        var esds = Box("esds", N(0), Descriptor(3, new byte[] { 0, 1, 0 }.Concat(Descriptor(4, decoder.Concat(Descriptor(5, config ?? [0x11, 0x90])).ToArray())).Concat(Descriptor(6, [2])).ToArray()));
        var stbl = Box("stbl", Box("stsd", N(0), N(1), Box("mp4a", entry, esds)),
            Box("stts", N(0), N(1), N(2), N(1024)), Box("stsc", N(0), N(1), N(1), N(2), N(1)),
            Box("stsz", N(0), N(sampleBytes), N(sampleCount)), Box("stco", N(0), N(1), N(offset)));
        var reference = Box("dinf", Box("dref", N(0), N(1), Box("url ", N(external ? 0U : 1U), external ? "https://invalid.example/audio\0"u8.ToArray() : [])));
        var mdia = Box("mdia", Box("mdhd", mdhd), Handler("soun"), Box("minf", Box("smhd", new byte[8]), reference, stbl));
        var moov = Box("moov", Box("mvhd", mvhd), Box("trak", Box("tkhd", tkhd), mdia),
            Box("udta", Box("meta", N(0), Handler("mdir"), Box("ilst", tags ?? [Text("\u00a9nam", "Title \u00fc")]))));
        return Box("ftyp", "M4A "u8.ToArray(), N(0), "isom"u8.ToArray()).Concat(Box("mdat", new byte[sampleBytes * 2 + (extraMedia ? 1 : 0)])).Concat(moov).ToArray();
    }
    internal static byte[] Change(byte[] bytes, string type, int offset, uint value)
    {
        var copy = bytes.ToArray(); var index = copy.AsSpan().IndexOf(Encoding.Latin1.GetBytes(type));
        if (index < 0) throw new InvalidDataException("Missing fixture atom.");
        N(value).CopyTo(copy, index + 4 + offset); return copy;
    }
    internal static byte[] Box(string type, params byte[][] payload)
    {
        var data = payload.SelectMany(part => part).ToArray(); return N((uint)data.Length + 8).Concat(Encoding.Latin1.GetBytes(type)).Concat(data).ToArray();
    }
    private static byte[] Text(string name, string text) => Box(name, Box("data", N(1), N(0), Encoding.UTF8.GetBytes(text)));
    private static byte[] Handler(string type) => Box("hdlr", new byte[8], Encoding.ASCII.GetBytes(type), new byte[12]);
    private static byte[] Descriptor(byte tag, byte[] data) => new byte[] { tag, checked((byte)data.Length) }.Concat(data).ToArray();
    private static byte[] N(uint value) { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); return bytes; }
    private static void Matrix(byte[] data, int start) { N(65536).CopyTo(data, start); N(65536).CopyTo(data, start + 16); N(1073741824).CopyTo(data, start + 32); }
    private static byte[] Replace(byte[] data, string[] path, Func<byte[], byte[]> change)
    {
        using var output = new MemoryStream(); var found = false;
        for (var offset = 0; offset < data.Length;)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset)));
            var type = Encoding.Latin1.GetString(data, offset + 4, 4);
            if (type == path[0])
            {
                found = true; var payload = data.AsSpan(offset + 8, size - 8).ToArray();
                output.Write(Box(type, path.Length == 1 ? change(payload) : Replace(payload, path[1..], change)));
            }
            else output.Write(data.AsSpan(offset, size));
            offset += size;
        }
        if (!found) throw new InvalidDataException("Missing fixture atom.");
        return output.ToArray();
    }
    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            var count = await base.ReadAsync(buffer, token); BytesRead += count; return count;
        }
    }
}
