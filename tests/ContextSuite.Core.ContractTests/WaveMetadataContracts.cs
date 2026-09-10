using System.Buffers.Binary;
using System.Text;
using ContextSuite.Core.Audio;

internal static class WaveMetadataContracts
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var format = Format();
        var samples = new byte[192];
        var titled = FileOf(("fmt ", format), ("data", samples), ("LIST", Info(("INAM", Text("Title")), ("IART", Text("Artist")))));
        using (var stream = new MemoryStream(titled))
        {
            stream.Position = 3;
            var inventory = await WaveMetadata.ReadAsync(stream, default);
            inventory.RequireConversionSupport(new Dictionary<string, string> { ["title"] = "Title", ["artist"] = "Artist" });
            check(stream.Position == 3 && inventory.SampleFrames == 48 && inventory.SampleRate == 48000 && inventory.Channels == 2 && inventory.Tags.Length == 2,
                "WAV inventory: metadata after samples, exact framing and caller position");
            try { inventory.RequireConversionSupport(new Dictionary<string, string> { ["title"] = "Wrong" }); check(false, "WAV inventory: lost probe tags"); }
            catch (InvalidDataException) { check(true, "WAV inventory: native tag omission or change cannot authorize conversion"); }
        }
        foreach (var name in new[] { "cue ", "smpl", "bext", "iXML", "cart", "id3 ", "abcd" })
        {
            var result = await Read(FileOf(("fmt ", format), (name, new byte[7]), ("data", samples)));
            try { result.RequireConversionSupport(new Dictionary<string, string>()); check(false, "WAV inventory: " + name); }
            catch (NotSupportedException) { check(result.UnsupportedChunks.Contains(name), "WAV inventory: preserves refusal for unhandled " + name); }
        }
        var lists = await Read(FileOf(("fmt ", format), ("LIST", Encoding.ASCII.GetBytes("adtl")), ("data", samples)));
        check(lists.UnsupportedChunks.Contains("LIST/adtl"), "WAV inventory: associated labels are not mistaken for INFO");
        var duplicate = await Read(FileOf(("fmt ", format), ("data", samples), ("LIST", Info(("INAM", Text("First")), ("INAM", Text("Second"))))));
        check(duplicate.Tags.Select(tag => tag.Value).SequenceEqual(["First", "Second"]) && duplicate.UnsupportedChunks.Any(item => item.Contains("duplicate")),
            "WAV inventory: repeated INFO values remain visible and block flattened mapping");
        foreach (var value in new[] { Encoding.UTF8.GetBytes("Café\0"), Encoding.ASCII.GetBytes("line\nline\0") })
        {
            var result = await Read(FileOf(("fmt ", format), ("data", samples), ("LIST", Info(("INAM", value)))));
            check(result.UnsupportedChunks.Any(item => item.Contains("text encoding or controls")), "WAV inventory: ambiguous text encoding and control values require explicit handling");
        }
        var unknownInfo = await Read(FileOf(("fmt ", format), ("data", samples), ("LIST", Info(("IENG", Text("Engineer"))))));
        check(unknownInfo.UnsupportedChunks.Contains("LIST/INFO/IENG"), "WAV inventory: unmapped INFO is not silently discarded");
        var padding = await Read(FileOf(("JUNK", new byte[] { 1, 2, 3 }), ("fmt ", format), ("PAD ", new byte[5]), ("data", samples)));
        check(padding.UnsupportedChunks.IsEmpty && padding.SampleFrames == 48, "WAV inventory: declared padding and odd chunk alignment preserve sample extent");
        var floating = await Read(FileOf(("fmt ", Format(3, 32)), ("fact", BitConverter.GetBytes(24)), ("data", samples)));
        check(floating.FloatingPoint && floating.SampleBits == 32 && floating.SampleFrames == 24, "WAV inventory: IEEE float and fact sample count agree");
        var extensible = new byte[40]; Format(0xfffe, 24).CopyTo(extensible, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(extensible.AsSpan(16), 22);
        BinaryPrimitives.WriteUInt16LittleEndian(extensible.AsSpan(18), 24);
        new Guid("00000001-0000-0010-8000-00aa00389b71").TryWriteBytes(extensible.AsSpan(24));
        var extended = await Read(FileOf(("fmt ", extensible), ("data", samples)));
        check(extended.SampleBits == 24 && extended.SampleFrames == 32 && !extended.FloatingPoint, "WAV inventory: extensible PCM subtype retains precision");
        BinaryPrimitives.WriteUInt16LittleEndian(extensible.AsSpan(18), 20);
        check(!(await Read(FileOf(("fmt ", extensible), ("data", samples)))).UnsupportedChunks.IsEmpty,
            "WAV inventory: valid bits differing from storage require a verified precision policy");
        BinaryPrimitives.WriteUInt16LittleEndian(extensible.AsSpan(18), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(extensible.AsSpan(20), 12);
        check((await Read(FileOf(("fmt ", extensible), ("data", samples)))).UnsupportedChunks.Any(item => item.Contains("speaker positions")),
            "WAV inventory: two channels with non-stereo speaker positions require preservation");
        BinaryPrimitives.WriteUInt32LittleEndian(extensible.AsSpan(20), 4);
        await Reject(FileOf(("fmt ", extensible), ("data", samples)), "speaker mask count mismatch");
        var badSize = titled.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(badSize.AsSpan(4), uint.MaxValue);
        await Reject(badSize, "RIFF extent overflow");
        await Reject(titled.Concat(new byte[] { 0 }).ToArray(), "trailing bytes");
        await Reject(FileOf(("data", samples), ("fmt ", format)), "samples before format");
        await Reject(FileOf(("fmt ", format), ("fmt ", format), ("data", samples)), "duplicate format");
        await Reject(FileOf(("fmt ", format), ("data", samples), ("data", samples)), "multiple sample chunks");
        await Reject(FileOf(("fmt ", format), ("data", samples[..^1])), "incomplete sample frame");
        await Reject(FileOf(("fmt ", format), ("fact", BitConverter.GetBytes(99)), ("data", samples)), "wrong fact count");
        await Reject(FileOf(("fmt ", format), ("data", samples), ("LIST", Info(("INAM", Encoding.ASCII.GetBytes("not terminated"))))), "unterminated INFO");
        await Reject(FileOf(("fmt ", format), ("data", samples), ("LIST", Info(("INAM", Encoding.ASCII.GetBytes("one\0two\0"))))), "hidden trailing INFO text");
        await Reject(FileOf(("fmt ", format), ("data", samples), ("LIST", Info(("INAM", new byte[WaveMetadata.MaximumMetadataBytes])))), "metadata budget");
        await Reject(FileOf(Enumerable.Repeat(("JUNK", Array.Empty<byte>()), WaveMetadata.MaximumChunks + 1).Prepend(("fmt ", format)).Append(("data", samples)).ToArray()), "chunk count budget");
        using (var large = new CountedStream(FileOf(("fmt ", format), ("data", new byte[4 * 1024 * 1024]), ("LIST", Info(("INAM", Text("After audio")))))))
        {
            var result = await WaveMetadata.ReadAsync(large, default);
            check(large.BytesRead < 256 && result.SampleFrames == 1048576 && result.Tags.Single().Value == "After audio",
                "WAV inventory: seeks past four MiB of audio while reading fewer than 256 bytes");
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var input = new MemoryStream(titled); input.Position = 2;
        try { await WaveMetadata.ReadAsync(input, cancelled.Token); check(false, "WAV inventory: cancellation"); }
        catch (OperationCanceledException) { check(input.Position == 2, "WAV inventory: cancellation preserves caller position"); }

        async Task Reject(byte[] bytes, string name)
        {
            try { await Read(bytes); check(false, "WAV inventory: " + name); }
            catch (InvalidDataException) { check(true, "WAV inventory: rejects " + name); }
        }
    }
    private static async Task<WaveMetadataInventory> Read(byte[] bytes)
    { using var stream = new MemoryStream(bytes); return await WaveMetadata.ReadAsync(stream, default); }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + "\0");
    private static byte[] Format(ushort code = 1, ushort bits = 16)
    {
        var bytes = new byte[16]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, code);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 2); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 48000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)(48000 * 2 * bits / 8));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), (ushort)(2 * bits / 8)); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), bits);
        return bytes;
    }
    private static byte[] Info(params (string Id, byte[] Data)[] chunks)
    { using var stream = new MemoryStream(); stream.Write("INFO"u8); WriteChunks(stream, chunks); return stream.ToArray(); }
    private static byte[] FileOf(params (string Id, byte[] Data)[] chunks)
    {
        using var stream = new MemoryStream(); stream.Write("RIFF\0\0\0\0WAVE"u8); WriteChunks(stream, chunks);
        var bytes = stream.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length - 8); return bytes;
    }
    private static void WriteChunks(Stream stream, (string Id, byte[] Data)[] chunks)
    {
        foreach (var chunk in chunks)
        { stream.Write(Encoding.ASCII.GetBytes(chunk.Id)); stream.Write(BitConverter.GetBytes(chunk.Data.Length)); stream.Write(chunk.Data); if ((chunk.Data.Length & 1) != 0) stream.WriteByte(0); }
    }
    private sealed class CountedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public long BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { var read = await base.ReadAsync(buffer, token); BytesRead += read; return read; }
    }
}
