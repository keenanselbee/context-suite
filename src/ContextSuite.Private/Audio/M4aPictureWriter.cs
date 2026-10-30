using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

// Only reconciles a fresh encoded candidate. Preserve media bytes and movie
// placement; repair chunk offsets when the enlarged movie precedes media.
internal static class M4aPictureWriter
{
    internal static async Task RewriteAsync(FileStream source, Stream target, ImmutableArray<FlacMetadataBlock> pictures,
        CancellationToken token, string? language = null)
    {
        token.ThrowIfCancellationRequested();
        await M4aMetadata.ReadAsync(source, token); // Refuses pre-existing covers and unsupported structure.
        var cover = M4aPictureAtoms.CreateCover(pictures);
        if (language is not null) cover = cover.Concat(M4aLanguageAtom.Create(language)).ToArray();
        var position = source.Position;
        try
        {
            source.Position = 0; var header = new byte[16];
            long movieStart = -1, movieEnd = -1; byte[]? movie = null;
            while (source.Position < source.Length)
            {
                var start = source.Position;
                await source.ReadExactlyAsync(header.AsMemory(0, 8), token);
                long size = BinaryPrimitives.ReadUInt32BigEndian(header); var headerBytes = 8;
                if (size == 1)
                {
                    await source.ReadExactlyAsync(header.AsMemory(8, 8), token);
                    size = checked((long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8))); headerBytes = 16;
                }
                if (size == 0) size = source.Length - start;
                if (header.AsSpan(4, 4).SequenceEqual("moov"u8))
                {
                    if (headerBytes != 8 || size - 8 > M4aMetadata.MaximumMovieBytes)
                        throw new InvalidDataException("Expected the bounded encoder's ordinary movie atom.");
                    movieStart = start; movieEnd = start + size;
                    movie = new byte[checked((int)size - 8)]; await source.ReadExactlyAsync(movie, token);
                }
                source.Position = start + size;
            }
            if (movie is null) throw new InvalidDataException("Encoded M4A has no movie atom.");
            var updated = Edit(movie, ["udta", "meta", "ilst"], data =>
            {
                var combined = new byte[checked(data.Length + cover.Length)];
                data.CopyTo(combined); cover.CopyTo(combined, data.Length); return combined;
            });
            var delta = updated.Length - movie.Length;
            if (updated.Length > M4aMetadata.MaximumMovieBytes || source.Length + delta > AudioFileSource.MaximumFileBytes)
                throw new InvalidDataException("M4A artwork exceeds the movie or file budget.");
            updated = Edit(updated, ["trak", "mdia", "minf", "stbl"], data => ShiftChunks(data, movieEnd, delta));
            token.ThrowIfCancellationRequested();
            source.Position = 0; await CopyAsync(source, target, movieStart, token);
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)(updated.Length + 8)); "moov"u8.CopyTo(header.AsSpan(4));
            await target.WriteAsync(header.AsMemory(0, 8), token); await target.WriteAsync(updated, token);
            source.Position = movieEnd; await CopyAsync(source, target, source.Length - movieEnd, token);
        }
        finally { source.Position = position; }
    }

    private static byte[] Edit(ReadOnlyMemory<byte> input, string[] path, Func<ReadOnlyMemory<byte>, byte[]> change)
    {
        using var output = new MemoryStream(); var found = false;
        // meta alone has a four-byte full-box header before its children.
        var offset = path[0] == "ilst" ? 4 : 0;
        output.Write(input.Span[..offset]);
        while (offset < input.Length)
        {
            var size = AtomSize(input.Span[offset..]);
            var atom = input.Slice(offset, size);
            if (Encoding.Latin1.GetString(atom.Span.Slice(4, 4)) == path[0])
            {
                if (found) throw new InvalidDataException("Duplicate encoded M4A container."); found = true;
                var payload = path.Length == 1 ? change(atom[8..]) : Edit(atom[8..], path[1..], change);
                var header = atom.Span[..8].ToArray(); BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)payload.Length + 8));
                output.Write(header); output.Write(payload);
            }
            else output.Write(atom.Span);
            offset += size;
        }
        if (!found) throw new InvalidDataException("Missing encoded M4A container: " + path[0]);
        return output.ToArray();
    }

    private static byte[] ShiftChunks(ReadOnlyMemory<byte> input, long movieEnd, int delta)
    {
        var output = input.ToArray(); var offset = 0; var found = false;
        while (offset < output.Length)
        {
            var size = AtomSize(output.AsSpan(offset)); var atom = output.AsSpan(offset, size);
            var wide = atom.Slice(4, 4).SequenceEqual("co64"u8);
            if (wide || atom.Slice(4, 4).SequenceEqual("stco"u8))
            {
                if (found || atom.Length < 16) throw new InvalidDataException("Invalid encoded M4A chunk table."); found = true;
                var count = BinaryPrimitives.ReadUInt32BigEndian(atom[12..]); var width = wide ? 8 : 4;
                if (16L + (long)count * width != size) throw new InvalidDataException("Invalid encoded M4A chunk table extent.");
                for (var index = 16; index < size; index += width)
                {
                    var value = wide ? BinaryPrimitives.ReadUInt64BigEndian(atom[index..]) : BinaryPrimitives.ReadUInt32BigEndian(atom[index..]);
                    if (value >= (ulong)movieEnd) value = checked(value + (uint)delta);
                    if (wide) BinaryPrimitives.WriteUInt64BigEndian(atom[index..], value);
                    else BinaryPrimitives.WriteUInt32BigEndian(atom[index..], checked((uint)value));
                }
            }
            offset += size;
        }
        if (!found) throw new InvalidDataException("Encoded M4A lacks chunk offsets.");
        return output;
    }

    private static int AtomSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8) throw new InvalidDataException("Truncated encoded M4A atom.");
        var size = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (size < 8 || size > data.Length) throw new InvalidDataException("Expected bounded ordinary encoded M4A atoms.");
        return (int)size;
    }

    private static async Task CopyAsync(Stream source, Stream target, long remaining, CancellationToken token)
    {
        var buffer = new byte[65536];
        while (remaining > 0)
        {
            var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
            if (count == 0) throw new InvalidDataException("Encoded M4A changed while adding artwork.");
            await target.WriteAsync(buffer.AsMemory(0, count), token); remaining -= count;
        }
    }
}
