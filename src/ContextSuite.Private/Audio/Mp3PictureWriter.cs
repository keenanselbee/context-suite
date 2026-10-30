using System.Collections.Immutable;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

// Reconcile only the freshly encoded candidate. Keep every encoded audio byte,
// including its gapless header, while adding checked standard APIC frames.
internal static class Mp3PictureWriter
{
    internal static async Task RewriteAsync(FileStream source, Stream target, ImmutableArray<FlacMetadataBlock> pictures, CancellationToken token)
    {
        var inventory = await Mp3Metadata.ReadAsync(source, token);
        byte[] tag;
        source.Position = 0;
        if (inventory.AudioOffset == 0) tag = [73, 68, 51, 4, 0, 0, 0, 0, 0, 0];
        else
        {
            if (inventory.AudioOffset > Mp3Metadata.MaximumTagBytes + 10) throw new InvalidDataException("Unexpected encoded MP3 tag extent.");
            tag = new byte[checked((int)inventory.AudioOffset)]; await source.ReadExactlyAsync(tag, token);
        }
        var replacement = Mp3PictureFrames.AppendToTag(tag, pictures);
        var remaining = source.Length - inventory.AudioOffset;
        if (replacement.Length + remaining > AudioFileSource.MaximumFileBytes) throw new InvalidDataException("MP3 artwork output exceeds its file budget.");
        await target.WriteAsync(replacement, token); source.Position = inventory.AudioOffset;
        var buffer = new byte[65536];
        while (remaining > 0)
        {
            var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
            if (count == 0) throw new InvalidDataException("Encoded MP3 changed while adding artwork.");
            await target.WriteAsync(buffer.AsMemory(0, count), token); remaining -= count;
        }
    }
}
