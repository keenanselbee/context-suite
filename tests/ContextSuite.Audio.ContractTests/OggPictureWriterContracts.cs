using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static class OggPictureWriterContracts
{
    internal static async Task RunAsync(string evidence, List<string> checks)
    {
        // Structural fixtures deliberately use opaque setup/audio bytes. The
        // separate native conversions prove real decoder compatibility.
        using var picture = new MemoryStream();
        void Number(int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); picture.Write(bytes); }
        Number(3); Number(9); picture.Write("image/png"u8); Number(0);
        Number(1); Number(1); Number(32); Number(0); Number(1); picture.WriteByte(42);
        var pictures = ImmutableArray.Create(new FlacMetadataBlock(6, picture.ToArray().ToImmutableArray()));
        foreach (var opus in new[] { false, true })
        foreach (var finalSize in new[] { 255, 65025, 130050, 130051 })
        {
            var basePacket = Comments(opus, "");
            var overhead = OggPictureComments.AppendToPacket(basePacket, pictures).Length;
            var title = new string('x', finalSize - overhead);
            var comments = Comments(opus, title);
            var id = new byte[opus ? 19 : 30];
            if (opus) { "OpusHead"u8.CopyTo(id); id[8] = 1; id[9] = 2; BinaryPrimitives.WriteUInt16LittleEndian(id.AsSpan(10), 312); }
            else { "\x01vorbis"u8.CopyTo(id); id[11] = 2; BinaryPrimitives.WriteInt32LittleEndian(id.AsSpan(12), 48000); id[28] = 0xb8; id[29] = 1; }
            var setup = "\x05vorbis\x01"u8.ToArray(); var audio = new byte[] { 2, 3, 4, 5 };
            var source = new List<byte[]> { Page(2, 0, 0, [(byte)id.Length], id) }; uint sequence = 1;
            // Vorbis setup shares the comment-ending page, including when the
            // original comment itself continues across earlier pages.
            var packets = opus ? new[] { comments } : new[] { comments, setup };
            var laces = packets.SelectMany(packet => Enumerable.Repeat((byte)255, packet.Length / 255).Append((byte)(packet.Length % 255))).ToArray();
            var body = packets.SelectMany(packet => packet).ToArray(); var offset = 0;
            for (var i = 0; i < laces.Length; i += 255)
            {
                var part = laces.Skip(i).Take(255).ToArray(); var length = part.Sum(value => (int)value);
                source.Add(Page(i > 0 && laces[i - 1] == 255 ? (byte)1 : (byte)0, sequence++, part.Any(value => value < 255) ? 0 : -1,
                    part, body.AsSpan(offset, length).ToArray())); offset += length;
            }
            source.Add(Page(4, sequence, 96312, [(byte)audio.Length], audio));
            var inputPath = Path.Combine(evidence, $"writer-{opus}-{finalSize}.ogg");
            await File.WriteAllBytesAsync(inputPath, source.SelectMany(page => page).ToArray());
            using var input = File.OpenRead(inputPath); using var output = new MemoryStream();
            await OggPictureWriter.RewriteAsync(input, output, pictures, default);
            var inventory = await OggMetadata.ReadAsync(output, default, true);
            OggPictureComments.RequirePreserved(pictures, inventory.Descriptions);
            var rewritten = Packets(output.ToArray());
            var expected = opus ? new[] { id, audio } : new[] { id, setup, audio };
            if (inventory.FinalGranule != 96312 || inventory.Descriptions.Comments.Single(comment => comment.Name == "TITLE").Value != title ||
                rewritten[1].Length != finalSize || rewritten.Count != expected.Length + 1 ||
                rewritten.Where((_, index) => index != 1).Where((packet, index) => !packet.SequenceEqual(expected[index])).Any() ||
                (opus && !rewritten[1].AsSpan(^2).SequenceEqual(new byte[] { 2, 99 })))
                throw new InvalidDataException("Artwork page rewrite changed framing, setup/audio bytes, padding or pictures.");
            checks.Add($"Ogg artwork writer: exact packet/page boundary {finalSize}, shared/continued headers and unchanged packets; opus={opus}");
        }
    }

    private static byte[] Comments(bool opus, string title)
    {
        using var stream = new MemoryStream(); stream.Write(opus ? "OpusTags"u8 : "\x03vorbis"u8);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(0); writer.Write(1); var text = Encoding.UTF8.GetBytes("TITLE=" + title); writer.Write(text.Length); writer.Write(text);
        stream.Write(opus ? new byte[] { 2, 99 } : new byte[] { 1 }); return stream.ToArray();
    }
    private static byte[] Page(byte flags, uint sequence, long granule, byte[] laces, byte[] body)
    {
        var page = new byte[27 + laces.Length + body.Length]; "OggS"u8.CopyTo(page); page[5] = flags;
        BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), granule); BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(14), 71);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(18), sequence); page[26] = (byte)laces.Length;
        laces.CopyTo(page, 27); body.CopyTo(page, 27 + laces.Length); uint crc = 0;
        foreach (var value in page) { crc ^= (uint)value << 24; for (var bit = 0; bit < 8; bit++) crc = (crc << 1) ^ ((crc & 0x80000000) == 0 ? 0u : 0x04c11db7u); }
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), crc); return page;
    }
    private static List<byte[]> Packets(byte[] bytes)
    {
        var result = new List<byte[]>(); using var packet = new MemoryStream(); var page = 0;
        while (page < bytes.Length)
        {
            var segments = bytes[page + 26]; var body = page + 27 + segments;
            for (var i = 0; i < segments; i++)
            {
                var length = bytes[page + 27 + i]; packet.Write(bytes, body, length); body += length;
                if (length < 255) { result.Add(packet.ToArray()); packet.SetLength(0); }
            }
            page = body;
        }
        if (packet.Length != 0) throw new InvalidDataException("Unfinished rewritten packet."); return result;
    }
}
