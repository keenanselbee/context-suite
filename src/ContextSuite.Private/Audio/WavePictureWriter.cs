using System.Buffers.Binary;
using System.Collections.Immutable;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

// Add a bounded ID3 text/picture chunk to a fresh encoded RIFF candidate. Samples,
// fmt/fact, INFO values and existing chunk padding remain byte-identical.
internal static class WavePictureWriter
{
    internal static async Task RewriteAsync(FileStream source, Stream target, ImmutableArray<FlacMetadataBlock> pictures, CancellationToken token,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        token.ThrowIfCancellationRequested();
        var inventory = await WaveMetadata.ReadAsync(source, token);
        if (!inventory.UnsupportedChunks.IsEmpty)
            throw new InvalidDataException("Encoded WAV contains unsupported information or an existing ID3 chunk: " + string.Join(", ", inventory.UnsupportedChunks));
        if (tags is not null && inventory.Tags.Any(tag => tags.ContainsKey(tag.Name)))
            throw new InvalidDataException("Encoded WAV already contains a descriptive field planned for ID3.");
        var textTag = tags is null ? new byte[] { 73, 68, 51, 4, 0, 0, 0, 0, 0, 0 } : WaveTextFrames.Create(tags);
        var tag = pictures.IsEmpty ? textTag : Mp3PictureFrames.AppendToTag(textTag, pictures);
        var length = source.Length + 8L + tag.Length + (tag.Length & 1);
        if (length >= AudioFileSource.MaximumFileBytes) throw new InvalidDataException("WAV artwork output exceeds its file budget.");
        var position = source.Position;
        try
        {
            source.Position = 0; var header = new byte[12]; await source.ReadExactlyAsync(header, token);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)length - 8));
            await target.WriteAsync(header, token);
            var remaining = source.Length - 12; var buffer = new byte[65536];
            while (remaining > 0)
            {
                var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
                if (count == 0) throw new InvalidDataException("Encoded WAV changed while adding artwork.");
                await target.WriteAsync(buffer.AsMemory(0, count), token); remaining -= count;
            }
            "id3 "u8.CopyTo(header); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)tag.Length);
            await target.WriteAsync(header.AsMemory(0, 8), token); await target.WriteAsync(tag, token);
            if ((tag.Length & 1) != 0) await target.WriteAsync(new byte[1], token);
        }
        finally { source.Position = position; }
    }
}
