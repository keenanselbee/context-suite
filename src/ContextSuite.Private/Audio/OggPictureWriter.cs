using System.Buffers.Binary;
using System.Collections.Immutable;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

// Rewrite only the comment packet of an owned, validated encoded candidate.
// Audio/setup packets and granules retain their bytes; changed pages get new
// sequence numbers/checksums. No customer file is rewritten in place.
internal static class OggPictureWriter
{
    private static readonly uint[] Checksums = Table();

    internal static async Task RewriteAsync(FileStream source, Stream target, ImmutableArray<FlacMetadataBlock> pictures, CancellationToken token)
    {
        await OggMetadata.ReadAsync(source, token);
        source.Position = 0;
        uint sequence = 0; long written = 0;
        var first = await Page(source, token);
        var serial = BinaryPrimitives.ReadUInt32LittleEndian(first.AsSpan(14));
        await Write(first);
        using var comments = new MemoryStream();
        var complete = false;
        while (!complete)
        {
            var page = await Page(source, token);
            var segments = page[26]; var body = 27 + segments; var segment = 0;
            for (; segment < segments; segment++)
            {
                var length = page[27 + segment];
                if (comments.Length + length > OggMetadata.MaximumPacketBytes) throw new InvalidDataException("Encoded comment packet exceeds its budget.");
                comments.Write(page, body, length); body += length;
                if (length == 255) continue;
                complete = true; segment++; break;
            }
            if (!complete) continue;
            var replacement = OggPictureComments.AppendToPacket(comments.GetBuffer().AsSpan(0, (int)comments.Length), pictures);
            var lacing = Enumerable.Repeat((byte)255, replacement.Length / 255).Append((byte)(replacement.Length % 255)).ToArray();
            var dataOffset = 0;
            for (var lace = 0; lace < lacing.Length; lace += 255)
            {
                var count = Math.Min(255, lacing.Length - lace);
                var bytes = lacing.AsSpan(lace, count).ToArray().Sum(value => (int)value);
                var part = new byte[27 + count + bytes]; "OggS"u8.CopyTo(part);
                part[5] = lace == 0 ? (byte)0 : (byte)1;
                BinaryPrimitives.WriteInt64LittleEndian(part.AsSpan(6), lace + count == lacing.Length ? 0 : -1);
                BinaryPrimitives.WriteUInt32LittleEndian(part.AsSpan(14), serial);
                part[26] = (byte)count; lacing.AsSpan(lace, count).CopyTo(part.AsSpan(27));
                replacement.AsSpan(dataOffset, bytes).CopyTo(part.AsSpan(27 + count)); dataOffset += bytes;
                await Write(part);
            }
            // Vorbis setup or other complete/following packets can share the
            // original comment-ending page. Keep all remaining laces/data.
            if (segment < segments)
            {
                var remaining = segments - segment;
                var rest = new byte[27 + remaining + page.Length - body];
                page.AsSpan(0, 27).CopyTo(rest); rest[5] &= 0xfe; rest[26] = (byte)remaining;
                page.AsSpan(27 + segment, remaining).CopyTo(rest.AsSpan(27));
                page.AsSpan(body).CopyTo(rest.AsSpan(27 + remaining));
                await Write(rest);
            }
        }
        while (source.Position < source.Length) await Write(await Page(source, token));

        async Task Write(byte[] page)
        {
            token.ThrowIfCancellationRequested();
            if (sequence >= OggMetadata.MaximumPages || page.Length > AudioFileSource.MaximumFileBytes - written)
                throw new InvalidDataException("Ogg artwork output exceeds its page or file budget.");
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(18), sequence++);
            page.AsSpan(22, 4).Clear(); uint crc = 0;
            foreach (var value in page) crc = (crc << 8) ^ Checksums[(crc >> 24) ^ value];
            BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), crc);
            await target.WriteAsync(page, token); written += page.Length;
        }
    }

    private static async Task<byte[]> Page(Stream source, CancellationToken token)
    {
        var header = new byte[27]; await source.ReadExactlyAsync(header, token);
        var lacing = new byte[header[26]]; await source.ReadExactlyAsync(lacing, token);
        var bytes = lacing.Sum(value => (int)value); var page = new byte[27 + lacing.Length + bytes];
        header.CopyTo(page, 0); lacing.CopyTo(page, 27);
        await source.ReadExactlyAsync(page.AsMemory(27 + lacing.Length, bytes), token); return page;
    }
    private static uint[] Table()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var crc = (uint)index << 24;
            for (var bit = 0; bit < 8; bit++) crc = (crc << 1) ^ ((crc & 0x80000000) == 0 ? 0u : 0x04c11db7u);
            table[index] = crc;
        }
        return table;
    }
}
