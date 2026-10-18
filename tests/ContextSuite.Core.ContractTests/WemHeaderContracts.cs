using System.Buffers.Binary;
using System.Text;
using ContextSuite.Core.Audio;

internal static class WemHeaderContracts
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var bigEndian in new[] { false, true })
        {
            var bytes = FileOf(bigEndian, ("fmt ", Format(0xfffe, bigEndian)),
                ("hash", Enumerable.Range(0, 16).Select(value => (byte)value).ToArray()),
                ("junk", [1, 2, 3]), ("smpl", Loop(bigEndian, 2, 8)), ("data", new byte[40]));
            using var source = new MemoryStream(bytes);
            source.Position = 7;
            var pcm = await WemHeader.ReadAsync(source);
            check(source.Position == 7 && pcm.IsHeaderLayoutRecognized && !pcm.HasUnrecognizedChunks &&
                pcm.Hash.AsSpan().SequenceEqual(Enumerable.Range(0, 16).Select(value => (byte)value).ToArray()) &&
                pcm.Codec == WemCodec.Pcm16 &&
                pcm.CodecTag == 0xfffe && pcm.BigEndian == bigEndian && pcm.Channels == 2 &&
                pcm.SampleRate == 48000 && pcm.BitDepth == 16 && pcm.SampleCount == 10 &&
                pcm.Loop == new WemLoop(2, 9) && pcm.DataOffset == bytes.Length - 40 && pcm.DataSize == 40,
                "WEM header: Wwise PCM16, endian, loop, exact data extent and caller position: " + bigEndian);

            var old = await Read(FileOf(bigEndian, ("fmt ", Format(1, bigEndian)), ("data", new byte[40])));
            check(old.IsHeaderLayoutRecognized && old.SampleCount == 10, "WEM header: legacy PCM requires Wwise format extension: " + bigEndian);
            var ordinary = await Read(FileOf(bigEndian, ("fmt ", Format(1, bigEndian)[..16]), ("data", new byte[40])));
            check(!ordinary.IsHeaderLayoutRecognized && ordinary.CodecTag == 1 && ordinary.SampleCount is null,
                "WEM header: ordinary PCM WAVE is not asserted to be Wwise: " + bigEndian);

            var vorb = new byte[0x28];
            Put32(vorb, 0, 96000, bigEndian);
            Put32(vorb, 0x18, 12, bigEndian);
            Put32(vorb, 0x1c, 24, bigEndian);
            Put32(vorb, 0x24, 0x12345678, bigEndian);
            var vorbis = await Read(FileOf(bigEndian, ("fmt ", Format(0xffff, bigEndian, 0)),
                ("vorb", vorb), ("data", new byte[48])));
            check(vorbis.IsHeaderLayoutRecognized && vorbis.Codec == WemCodec.Vorbis && vorbis.SampleCount == 96000 &&
                vorbis.Vorbis == new WemVorbisInfo(12, 24, WemCodebooks.FullInline, 0x12345678),
                "WEM header: older Vorbis sample and seek metadata with known inline codebooks: " + bigEndian);

            var newerFormat = new byte[66];
            Format(0xffff, bigEndian, 0).CopyTo(newerFormat, 0);
            Put16(newerFormat, 16, 0x30, bigEndian);
            Put32(newerFormat, 24, 48000, bigEndian);
            Put32(newerFormat, 40, 8, bigEndian);
            Put32(newerFormat, 44, 20, bigEndian);
            Put32(newerFormat, 60, 0x87654321, bigEndian);
            var codebookHash = Enumerable.Repeat((byte)0xab, 16).ToArray();
            var newer = await Read(FileOf(bigEndian, ("fmt ", newerFormat), ("hash", codebookHash), ("data", new byte[40])));
            check(newer.IsHeaderLayoutRecognized && newer.SampleCount == 48000 &&
                newer.Hash.AsSpan().SequenceEqual(codebookHash) &&
                newer.Vorbis == new WemVorbisInfo(8, 20, WemCodebooks.Unknown, 0x87654321),
                "WEM header: fmt-embedded Vorbis keeps ambiguous codebook family explicit: " + bigEndian);
            var oddData = FileOf(bigEndian, ("fmt ", newerFormat), ("data", new byte[41]));
            Array.Resize(ref oddData, oddData.Length - 1);
            Put32(oddData, 4, (uint)oddData.Length - 8, bigEndian);
            check((await Read(oddData)).DataSize == 41,
                "WEM header: final odd-sized Wwise data may omit RIFF padding: " + bigEndian);
            var shortLoop = Loop(bigEndian, 2, 8)[..52];
            var looped = await Read(FileOf(bigEndian, ("fmt ", Format(0xfffe, bigEndian)),
                ("smpl", shortLoop), ("data", new byte[40])));
            check(looped.Loop == new WemLoop(2, 9),
                "WEM header: Wwise 52-byte smpl omits zero fraction and play count: " + bigEndian);
        }

        var pcmFile = FileOf(false, ("fmt ", Format(0xfffe, false)), ("data", new byte[40]));
        var unknownFormat = Format(0x7777, false);
        var unknown = await Read(FileOf(false, ("fmt ", unknownFormat), ("akd ", [1, 2]), ("data", new byte[40])));
        check(!unknown.IsHeaderLayoutRecognized && unknown.HasUnrecognizedChunks &&
            unknown.CodecTag == 0x7777 && unknown.CodecName == "unknown" &&
            unknown.UnsupportedChunks.SequenceEqual(["akd "]), "WEM header: unknown codec and chunk remain explicit");
        var opus = await Read(FileOf(false, ("fmt ", Format(0x3041, false)), ("data", new byte[40])));
        check(!opus.IsHeaderLayoutRecognized && opus.CodecName == "wwise_opus" && opus.SampleCount is null,
            "WEM header: known unsupported codec is identified without inferred samples");

        await Reject(pcmFile[..^1], "truncated data and RIFF extent");
        var overDeclared = pcmFile.ToArray(); Put32(overDeclared, overDeclared.Length - 44, 400, false);
        await Reject(overDeclared, "prefetch data declaration beyond physical file");
        await Reject(pcmFile.Concat(new byte[] { 0 }).ToArray(), "trailing data");
        await Reject(FileOf(false, ("fmt ", Format(0xfffe, false)), ("fmt ", Format(0xfffe, false)), ("data", new byte[40])), "duplicate format");
        await Reject(FileOf(false, ("fmt ", Format(0xfffe, false)), ("data", new byte[40]), ("data", new byte[40])), "duplicate data");
        await Reject(FileOf(false, ("fmt ", Format(0xfffe, false)), ("vorb", new byte[40]), ("vorb", new byte[40]), ("data", new byte[40])), "duplicate Vorbis metadata");
        await Reject(FileOf(false, ("fmt ", Format(0xfffe, false)), ("vorb", new byte[40]), ("data", new byte[40])), "Vorbis metadata with PCM tag");
        await Reject(FileOf(false, ("data", new byte[40]), ("fmt ", Format(0xfffe, false))), "data before format");
        var badExtension = Format(0xfffe, false); Put16(badExtension, 16, 7, false);
        await Reject(FileOf(false, ("fmt ", badExtension), ("data", new byte[40])), "format extension mismatch");
        var badVorbis = new byte[66];
        Format(0xffff, false, 0).CopyTo(badVorbis, 0);
        Put16(badVorbis, 16, 0x30, false);
        Put32(badVorbis, 24, 48000, false);
        Put32(badVorbis, 40, 30, false);
        Put32(badVorbis, 44, 20, false);
        await Reject(FileOf(false, ("fmt ", badVorbis), ("data", new byte[40])), "Vorbis seek table exceeds audio offset");
        var badLoop = Loop(false, 9, 2);
        await Reject(FileOf(false, ("fmt ", Format(0xfffe, false)), ("smpl", badLoop), ("data", new byte[40])), "invalid loop range");
        var twoLoops = Loop(false, 2, 8); Put32(twoLoops, 28, 2, false);
        await Refuse(FileOf(false, ("fmt ", Format(0xfffe, false)), ("smpl", twoLoops), ("data", new byte[40])), "multiple loops");
        foreach (var field in new[] { 32, 52, 56 })
        {
            var unsupportedLoop = Loop(false, 2, 8); Put32(unsupportedLoop, field, 1, false);
            await Refuse(FileOf(false, ("fmt ", Format(0xfffe, false)), ("smpl", unsupportedLoop), ("data", new byte[40])),
                "unsupported smpl field at " + field);
        }
        await Reject(FileOf(false, Enumerable.Repeat(("JUNK", Array.Empty<byte>()), WemHeader.MaximumChunks).Prepend(("fmt ", Format(0xfffe, false))).Append(("data", new byte[40])).ToArray()), "chunk count bound");
        await Reject(FileOf(false, ("fmt ", Format(0xfffe, false)), ("smpl", new byte[WemHeader.MaximumMetadataBytes]), ("data", new byte[40])), "metadata budget");

        using (var large = new CountedStream(FileOf(false, ("fmt ", Format(0xfffe, false)),
            ("data", new byte[4 * 1024 * 1024]))))
        {
            var result = await WemHeader.ReadAsync(large);
            check(result.SampleCount == 1048576 && large.BytesRead < 128,
                "WEM header: seeks past large data without reading samples");
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var input = new MemoryStream(pcmFile);
        input.Position = 4;
        try { await WemHeader.ReadAsync(input, cancelled.Token); check(false, "WEM header: cancellation"); }
        catch (OperationCanceledException) { check(input.Position == 4, "WEM header: cancellation preserves caller position"); }
        using var malformed = new MemoryStream(pcmFile[..^1]);
        malformed.Position = 5;
        try { await WemHeader.ReadAsync(malformed); check(false, "WEM header: malformed extent position"); }
        catch (InvalidDataException) { check(malformed.Position == 5, "WEM header: rejection preserves caller position"); }
        using var nonSeekable = new NonSeekableStream(pcmFile);
        try { await WemHeader.ReadAsync(nonSeekable); check(false, "WEM header: nonseekable input"); }
        catch (InvalidDataException) { check(true, "WEM header: nonseekable input is refused"); }

        async Task Reject(byte[] bytes, string scenario)
        {
            try { await Read(bytes); check(false, "WEM header: " + scenario); }
            catch (InvalidDataException) { check(true, "WEM header: rejects " + scenario); }
        }
        async Task Refuse(byte[] bytes, string scenario)
        {
            try { await Read(bytes); check(false, "WEM header: " + scenario); }
            catch (NotSupportedException) { check(true, "WEM header: refuses " + scenario); }
        }
    }

    private static async Task<WemHeader> Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return await WemHeader.ReadAsync(stream);
    }

    private static byte[] Format(ushort tag, bool bigEndian, ushort bits = 16)
    {
        var bytes = new byte[24];
        Put16(bytes, 0, tag, bigEndian);
        Put16(bytes, 2, 2, bigEndian);
        Put32(bytes, 4, 48000, bigEndian);
        Put32(bytes, 8, bits == 0 ? 0u : (uint)(48000 * 2 * bits / 8), bigEndian);
        Put16(bytes, 12, bits == 0 ? (ushort)0 : (ushort)(2 * bits / 8), bigEndian);
        Put16(bytes, 14, bits, bigEndian);
        Put16(bytes, 16, 6, bigEndian);
        return bytes;
    }

    private static byte[] Loop(bool bigEndian, uint start, uint endInclusive)
    {
        var bytes = new byte[60];
        Put32(bytes, 28, 1, bigEndian);
        Put32(bytes, 44, start, bigEndian);
        Put32(bytes, 48, endInclusive, bigEndian);
        return bytes;
    }

    private static byte[] FileOf(bool bigEndian, params (string Id, byte[] Data)[] chunks)
    {
        using var stream = new MemoryStream();
        stream.Write(bigEndian ? "RIFX\0\0\0\0WAVE"u8 : "RIFF\0\0\0\0WAVE"u8);
        Span<byte> length = stackalloc byte[4];
        foreach (var (id, data) in chunks)
        {
            stream.Write(Encoding.ASCII.GetBytes(id));
            Put32(length, 0, (uint)data.Length, bigEndian);
            stream.Write(length);
            stream.Write(data);
            if ((data.Length & 1) != 0) stream.WriteByte(0);
        }
        var bytes = stream.ToArray();
        Put32(bytes, 4, (uint)bytes.Length - 8, bigEndian);
        return bytes;
    }

    private static void Put16(Span<byte> bytes, int offset, ushort value, bool bigEndian)
    {
        if (bigEndian) BinaryPrimitives.WriteUInt16BigEndian(bytes[offset..], value);
        else BinaryPrimitives.WriteUInt16LittleEndian(bytes[offset..], value);
    }

    private static void Put32(Span<byte> bytes, int offset, uint value, bool bigEndian)
    {
        if (bigEndian) BinaryPrimitives.WriteUInt32BigEndian(bytes[offset..], value);
        else BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);
    }

    private sealed class CountedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public long BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            var read = await base.ReadAsync(buffer, token);
            BytesRead += read;
            return read;
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
