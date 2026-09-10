using System.Buffers.Binary;
using ContextSuite.Core.Audio;

internal static class OggMetadataContracts
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var opus in new[] { false, true })
        {
            var bytes = FileOf(opus, Comments(opus, "TITLE=Title \u00fc", "COMMENT=Line one\r\nLine two"));
            using var source = new MemoryStream(bytes); source.Position = 5;
            var inventory = await OggMetadata.ReadAsync(source, default);
            check(inventory.Codec == (opus ? "opus" : "vorbis") && inventory.SampleRate == 48000 && inventory.Channels == 2 &&
                inventory.FinalGranule == 96312 && source.Position == 5 && inventory.ConversionTags()["title"] == "Title \u00fc",
                "Ogg inventory: codec, tags, extent and caller position: " + opus);
            var damaged = bytes.ToArray(); damaged[^1] ^= 1;
            await Reject(damaged, "page checksum");
            await Reject(bytes[..^1], "truncated page body");
            await Reject(bytes.Concat(bytes).ToArray(), "chained stream");
            await Reject(bytes.Concat(new byte[] { 1 }).ToArray(), "trailing data");
            var noEnd = Page(0, opus ? 2u : 3u, 96312, [2]);
            await Reject(FileOf(opus, Comments(opus), noEnd), "missing end flag");
            await Reject(FileOf(opus, Comments(opus), Page(4, 99, 96312, [2])), "page sequence gap");
            await Reject(FileOf(opus, Comments(opus), Page(5, opus ? 2u : 3u, 96312, [2])), "unexpected continuation");
            await Reject(FileOf(opus, Comments(opus), Page(6, opus ? 2u : 3u, 96312, [2])), "repeated beginning flag");
            var otherSerial = Page(4, opus ? 2u : 3u, 96312, [2]); otherSerial[14] ^= 1; Checksum(otherSerial);
            await Reject(FileOf(opus, Comments(opus), otherSerial), "multiplexed stream");
            await Reject(FileOf(opus, Comments(opus, "TITLE=One", "title=Two")), "duplicate values", true);
            await Reject(FileOf(opus, Comments(opus, "METADATA_BLOCK_PICTURE=Picture")), "embedded artwork", true);
            await Reject(FileOf(opus, Comments(opus, "R128_TRACK_GAIN=123")), "playback gain comment", true);
            var invalidUtf8 = Comments(opus, "TITLE=X"); invalidUtf8[opus ? ^1 : ^2] = 255;
            await Reject(FileOf(opus, invalidUtf8), "invalid UTF-8");
            await Reject(FileOf(opus, Comments(opus, "BAD~=Value")), "comment name outside ASCII range");
            var longComment = Comments(opus, "LYRICS=" + new string('x', 70000));
            var first = Page(0, 1, -1, longComment[..65025], true);
            var last = Page(1, 2, 0, longComment[65025..]);
            var continued = new[] { Page(2, 0, 0, Identification(opus)), first, last };
            if (!opus) continued = continued.Append(Page(0, 3, 0, "\x05vorbis\x01"u8.ToArray())).ToArray();
            var split = continued.Append(Page(4, opus ? 3u : 4u, 96312, [2])).SelectMany(part => part).ToArray();
            using var splitStream = new MemoryStream(split);
            check((await OggMetadata.ReadAsync(splitStream, default)).Descriptions.Comments.Single().Value.Length == 70000,
                "Ogg inventory: continued comment packet across pages: " + opus);
            first[5] |= 1; Checksum(first);
            await Reject(continued.Append(Page(4, opus ? 3u : 4u, 96312, [2])).SelectMany(part => part).ToArray(), "false continued header");
        }
        var gainHeader = Identification(true); gainHeader[16] = 1;
        var gainFile = Page(2, 0, 0, gainHeader).Concat(Page(0, 1, 0, Comments(true))).Concat(Page(4, 2, 96312, [2])).ToArray();
        await Reject(gainFile, "nonzero Opus header gain", true);
        await Reject(FileOf(true, Comments(true).Concat(new byte[] { 1, 2 }).ToArray()), "Opus binary extension");
        using (var padding = new MemoryStream(FileOf(true, Comments(true).Concat(new byte[] { 2, 99 }).ToArray())))
            check((await OggMetadata.ReadAsync(padding, default)).Codec == "opus", "Ogg inventory: Opus discardable padding follows its flag");
        await Reject(FileOf(false, Comments(false).Concat(new byte[] { 0 }).ToArray()), "Vorbis trailing comment bytes");
        var reserved = FileOf(true, Comments(true)); reserved[5] |= 8;
        await Reject(reserved, "reserved page flags");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var cancelSource = new MemoryStream(FileOf(true, Comments(true))); cancelSource.Position = 7;
        try { await OggMetadata.ReadAsync(cancelSource, cancelled.Token); check(false, "Ogg inventory: pre-cancel"); }
        catch (OperationCanceledException) { check(cancelSource.Position == 7, "Ogg inventory: pre-cancel preserves position"); }
        var backwards = Page(2, 0, 0, Identification(true)).Concat(Page(0, 1, 0, Comments(true)))
            .Concat(Page(0, 2, 96000, [2])).Concat(Page(4, 3, 95000, [2])).ToArray();
        await Reject(backwards, "backward granules");
        await Reject(FileOf(true, Comments(true), Page(4, 2, -1, [2])), "missing end extent");
        await Reject(FileOf(true, Comments(true), Page(4, 2, 96312, [])), "empty audio packet");
        var tooLarge = new[] { Page(2, 0, 0, Identification(true)), Page(0, 1, 0, Comments(true)) }
            .Concat(Enumerable.Range(0, 17).Select(i => Page((byte)(i == 0 ? 0 : 1), (uint)i + 2, -1, new byte[65025], true)))
            .SelectMany(page => page).ToArray();
        await Reject(tooLarge, "packet byte budget");
        var continuedEos = Page(2, 0, 0, Identification(true)).Concat(Page(0, 1, 0, Comments(true)))
            .Concat(Page(4, 2, -1, new byte[255], true)).ToArray();
        await Reject(continuedEos, "unfinished end packet");
        var unknownVersion = Identification(true); unknownVersion[8] = 16;
        await Reject(Page(2, 0, 0, unknownVersion).Concat(Page(0, 1, 0, Comments(true))).Concat(Page(4, 2, 96312, [2])).ToArray(), "unknown Opus version");
        var noMapping = Identification(true); noMapping[18] = 1;
        await Reject(Page(2, 0, 0, noMapping).Concat(Page(0, 1, 0, Comments(true))).Concat(Page(4, 2, 96312, [2])).ToArray(), "truncated channel map");

        async Task Reject(byte[] bytes, string name, bool conversion = false)
        {
            using var stream = new MemoryStream(bytes); stream.Position = 3;
            try
            {
                var result = await OggMetadata.ReadAsync(stream, default);
                if (conversion) result.ConversionTags();
                check(false, "Ogg inventory rejects " + name);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            { check(stream.Position == 3, "Ogg inventory rejects " + name + " and restores position"); }
        }
    }

    private static byte[] FileOf(bool opus, byte[] comments, byte[]? audioPage = null)
    {
        var pages = new[] { Page(2, 0, 0, Identification(opus)), Page(0, 1, 0, comments) };
        if (!opus) pages = pages.Append(Page(0, 2, 0, "\x05vorbis\x01"u8.ToArray())).ToArray();
        return pages.Append(audioPage ?? Page(4, opus ? 2u : 3u, 96312, [2])).SelectMany(page => page).ToArray();
    }
    private static byte[] Identification(bool opus)
    {
        var header = new byte[opus ? 19 : 30];
        if (opus) { "OpusHead"u8.CopyTo(header); header[8] = 1; header[9] = 2; BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), 312); }
        else { "\x01vorbis"u8.CopyTo(header); header[11] = 2; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 48000); header[28] = 0xb8; header[29] = 1; }
        return header;
    }
    private static byte[] Comments(bool opus, params string[] fields) =>
        (opus ? "OpusTags"u8.ToArray() : "\x03vorbis"u8.ToArray()).Concat(FlacDescriptionContracts.Comments(fields))
            .Concat(opus ? Array.Empty<byte>() : new byte[] { 1 }).ToArray();

    // Authored CRC uses the bitwise polynomial, independently of production's lookup table.
    private static byte[] Page(byte flags, uint sequence, long granule, byte[] data, bool unfinished = false)
    {
        var segments = data.Length / 255 + (unfinished ? 0 : 1);
        var page = new byte[27 + segments + data.Length]; "OggS"u8.CopyTo(page); page[5] = flags;
        BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), granule); BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(14), 12345);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(18), sequence); page[26] = checked((byte)segments);
        for (var i = 0; i < segments; i++) page[27 + i] = (byte)Math.Min(255, data.Length - 255 * i);
        data.CopyTo(page, 27 + segments); Checksum(page); return page;
    }
    private static void Checksum(byte[] page)
    {
        page.AsSpan(22, 4).Clear(); uint crc = 0;
        foreach (var value in page)
        {
            crc ^= (uint)value << 24;
            for (var bit = 0; bit < 8; bit++) crc = (crc << 1) ^ ((crc & 0x80000000) != 0 ? 0x04c11db7u : 0);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), crc);
    }
}
