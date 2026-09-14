using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using ContextSuite.Core.Audio;

internal static partial class WaveMetadataContracts
{
    private static async Task Id3Async(Action<bool, string> check)
    {
        var format = Format(); var samples = new byte[400];
        var title = Tag(Frame("TIT2", new byte[] { 3 }.Concat(Encoding.UTF8.GetBytes("Title \u00fc")).ToArray()));
        foreach (var id in new[] { "id3 ", "ID3 " })
        foreach (var leading in new[] { true, false })
        {
            var file = leading ? FileOf(("fmt ", format), (id, title), ("data", samples)) : FileOf(("fmt ", format), ("data", samples), (id, title));
            using var stream = new MemoryStream(file); stream.Position = 3;
            var inventory = await WaveMetadata.ReadAsync(stream, default, true);
            check(stream.Position == 3 && inventory.SampleFrames == 100 && inventory.Tags.Single().Value == "Title \u00fc" && inventory.UnsupportedChunks.IsEmpty,
                "WAV ID3: typed Unicode text before/after samples, both identifiers and caller position " + id + leading);
            inventory.RequireConversionSupport(new Dictionary<string, string> { ["title"] = "Title \u00fc" });
            var guarded = await WaveMetadata.ReadAsync(stream, default);
            check(guarded.UnsupportedChunks.Contains(id) && guarded.Tags.IsEmpty, "WAV ID3: explicit opt-in retains old unknown-chunk guard " + id + leading);
        }
        var footer = title.ToArray(); footer[5] = 16;
        var ending = footer[..10].ToArray(); "3DI"u8.CopyTo(ending);
        var complete = footer.Concat(ending).ToArray();
        using (var stream = new MemoryStream(FileOf(("fmt ", format), ("data", samples), ("id3 ", complete))))
            check((await WaveMetadata.ReadAsync(stream, default, true)).Tags.Single().Value == "Title \u00fc", "WAV ID3: exact matching v2.4 footer");
        var png = new byte[40]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(8), 13); "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 32); BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 32); png[24] = 8; png[25] = 6;
        using var blockBytes = new MemoryStream();
        void Number(uint value) { Span<byte> field = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(field, value); blockBytes.Write(field); }
        Number(3); Number(9); blockBytes.Write("image/png"u8); Number(5); blockBytes.Write("Front"u8);
        Number(32); Number(32); Number(32); Number(0); Number((uint)png.Length); blockBytes.Write(png);
        var picture = new FlacMetadataBlock(6, blockBytes.ToArray().ToImmutableArray());
        var coverTag = Mp3PictureFrames.AppendToTag(title, [picture]);
        using (var stream = new MemoryStream(FileOf(("fmt ", format), ("data", new byte[1024 * 1024]), ("id3 ", coverTag))))
        {
            var result = await WaveMetadata.ReadAsync(stream, default, true);
            var expected = Mp3PictureFrames.ForOutput([picture]);
            check(result.Pictures.Length == 1 && result.Pictures[0].Data.AsSpan().SequenceEqual(expected[0].Data.AsSpan()),
                "WAV ID3: complete picture type, description and image bytes survive large sample extent");
        }
        foreach (var value in new[] { "Title \u00fc", "Different" })
        {
            // INFO uses ASCII; the first case still deliberately duplicates the field.
            var file = FileOf(("fmt ", format), ("data", samples), ("LIST", Info(("INAM", Text(value)))), ("id3 ", title));
            using var stream = new MemoryStream(file); var result = await WaveMetadata.ReadAsync(stream, default, true);
            check(result.UnsupportedChunks.Contains("INFO/ID3/duplicate title") && result.Tags.Length == 2, "WAV ID3: cross-store duplicates are retained and blocked");
        }
        foreach (var (name, tag) in new[] { ("short header", title[..9]), ("truncated", title[..^1]), ("trailing bytes", title.Concat(new byte[] { 0 }).ToArray()),
                     ("unknown frame", Tag(Frame("PRIV", [0, 1]))), ("duplicate title", Tag(Frame("TIT2", [3, 65]), Frame("TIT2", [3, 66]))),
                     ("bad footer", complete[..^1]), ("unsupported version", Changed(title, 3, 5)), ("revision", Changed(title, 4, 1)),
                     ("extended header", Changed(title, 5, 64)), ("non-synchsafe", Changed(title, 6, 128)), ("tag byte limit", new byte[Mp3Metadata.MaximumTagBytes + 21]) })
            await Reject(FileOf(("fmt ", format), ("data", samples), ("id3 ", tag)), name);
        await Reject(FileOf(("fmt ", format), ("data", samples), ("id3 ", title), ("ID3 ", title)), "duplicate ID3 chunks across casing");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var input = new MemoryStream(FileOf(("fmt ", format), ("data", samples), ("id3 ", title))); input.Position = 5;
        try { await WaveMetadata.ReadAsync(input, cancelled.Token, true); check(false, "WAV ID3 cancellation"); }
        catch (OperationCanceledException) { check(input.Position == 5, "WAV ID3: pre-cancellation and caller position"); }

        async Task Reject(byte[] file, string name)
        {
            using var stream = new MemoryStream(file); stream.Position = 2;
            try { await WaveMetadata.ReadAsync(stream, default, true); check(false, "WAV ID3 rejects " + name); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { check(stream.Position == 2, "WAV ID3 rejects and restores position: " + name); }
        }
        static byte[] Changed(byte[] input, int offset, byte value) { var result = input.ToArray(); result[offset] = value; return result; }
        static byte[] Size(int value) => [(byte)(value >> 21 & 127), (byte)(value >> 14 & 127), (byte)(value >> 7 & 127), (byte)(value & 127)];
        static byte[] Frame(string id, byte[] data) => Encoding.ASCII.GetBytes(id).Concat(Size(data.Length)).Concat(new byte[2]).Concat(data).ToArray();
        static byte[] Tag(params byte[][] frames)
        {
            var data = frames.SelectMany(frame => frame).ToArray();
            return new byte[] { 73, 68, 51, 4, 0, 0 }.Concat(Size(data.Length)).Concat(data).ToArray();
        }
    }
}
