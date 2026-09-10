using System.Buffers.Binary;
using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

// Metadata framing and byte-preservation only. Audio decoding/equality, source
// stability, smaller-output checks and final publication remain separate gates.
public static class FlacMetadata
{
    public const int MaximumHeaderBytes = 32 * 1024 * 1024;
    public const int MaximumBlocks = 256;

    public static FlacMetadataHeader Parse(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.StartsWith("fLaC"u8)) throw new InvalidDataException("The file has no native FLAC signature.");
        var blocks = ImmutableArray.CreateBuilder<FlacMetadataBlock>();
        var offset = 4;
        var singletons = new HashSet<byte>();
        while (true)
        {
            if (blocks.Count >= MaximumBlocks || offset > MaximumHeaderBytes - 4)
                throw new InvalidDataException("FLAC metadata exceeds the block or byte budget.");
            if (offset > bytes.Length - 4) throw new InvalidDataException("The FLAC metadata header is incomplete.");
            var type = (byte)(bytes[offset] & 127);
            var last = (bytes[offset] & 128) != 0;
            var size = (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
            if (size > MaximumHeaderBytes - offset - 4 || size > bytes.Length - offset - 4)
                throw new InvalidDataException("A FLAC metadata block is incomplete or exceeds the byte budget.");
            if (type == 127 || (blocks.Count == 0 && type != 0) ||
                (type is 0 or 3 or 4 or 5 && !singletons.Add(type)))
                throw new InvalidDataException("The FLAC metadata block order or type is invalid.");
            var data = bytes.Slice(offset + 4, size);
            if ((type == 0 && size != 34) || (type == 1 && data.IndexOfAnyExcept((byte)0) >= 0) ||
                (type == 2 && size < 4) || (type == 3 && size % 18 != 0) || (type == 4 && size < 8) ||
                (type == 5 && size < 396) || (type == 6 && size < 32))
                throw new InvalidDataException("A FLAC metadata block has an invalid declared structure.");
            blocks.Add(new(type, ImmutableArray.Create(data.ToArray())));
            offset += 4 + size;
            if (last) break;
        }
        var streamInfo = blocks[0].Data.AsSpan();
        var minimumBlock = BinaryPrimitives.ReadUInt16BigEndian(streamInfo);
        var maximumBlock = BinaryPrimitives.ReadUInt16BigEndian(streamInfo[2..]);
        var packed = BinaryPrimitives.ReadUInt64BigEndian(streamInfo[10..]);
        var rate = (int)(packed >> 44);
        var channels = (int)((packed >> 41) & 7) + 1;
        var precision = (int)((packed >> 36) & 31) + 1;
        if (minimumBlock < 16 || maximumBlock < minimumBlock || precision < 4 || rate == 0)
            throw new InvalidDataException("FLAC STREAMINFO does not describe a supported audio stream.");
        return new(blocks.ToImmutable(), offset, rate, channels, precision, (long)(packed & 0xfffffffffUL));
    }

    public static ImmutableArray<string> RecompressionRestrictions(FlacMetadataHeader header)
    {
        var reasons = ImmutableArray.CreateBuilder<string>();
        if (header.Blocks.Any(block => block.Type == 2))
            reasons.Add("Application-specific metadata needs a preservation handler before this file can be recompressed.");
        if (header.Blocks.Any(block => block.Type == 3))
            reasons.Add("The seek table must be rebuilt for new audio frames before this file can be recompressed.");
        if (header.Blocks.Any(block => block.Type >= 7))
            reasons.Add("Unrecognized metadata needs a preservation handler before this file can be recompressed.");
        return reasons.ToImmutable();
    }

    public static byte[] CreateRecompressionHeader(FlacMetadataHeader source, FlacMetadataHeader encoded)
    {
        var restrictions = RecompressionRestrictions(source);
        if (!restrictions.IsEmpty) throw new NotSupportedException(string.Join(" ", restrictions));
        RequireSameAudioDeclarations(source, encoded);
        // The encoder's frame-size fields and checksum belong to the new frames.
        // Original descriptive metadata, cuesheets and pictures remain byte-exact.
        var blocks = new[] { encoded.Blocks[0] }.Concat(source.Blocks.Where(block => block.Type is not (0 or 1))).ToArray();
        var length = 4L + blocks.Sum(block => 4L + block.Data.Length);
        if (length > MaximumHeaderBytes || blocks.Length > MaximumBlocks)
            throw new InvalidDataException("Reconciled FLAC metadata exceeds the budget.");
        var result = new byte[(int)length];
        "fLaC"u8.CopyTo(result);
        var offset = 4;
        for (var index = 0; index < blocks.Length; index++)
        {
            var block = blocks[index];
            result[offset] = (byte)(block.Type | (index == blocks.Length - 1 ? 128 : 0));
            result[offset + 1] = (byte)(block.Data.Length >> 16);
            result[offset + 2] = (byte)(block.Data.Length >> 8);
            result[offset + 3] = (byte)block.Data.Length;
            block.Data.AsSpan().CopyTo(result.AsSpan(offset + 4));
            offset += 4 + block.Data.Length;
        }
        return result;
    }

    public static void RequirePreservedMetadata(FlacMetadataHeader source, FlacMetadataHeader output)
    {
        RequireSameAudioDeclarations(source, output);
        var original = source.Blocks.Where(block => block.Type is not (0 or 1)).ToArray();
        var produced = output.Blocks.Where(block => block.Type is not (0 or 1)).ToArray();
        if (original.Length != produced.Length || original.Where((block, index) =>
            block.Type != produced[index].Type || !block.Data.AsSpan().SequenceEqual(produced[index].Data.AsSpan())).Any())
            throw new InvalidDataException("Required FLAC metadata changed or was omitted.");
    }

    private static void RequireSameAudioDeclarations(FlacMetadataHeader source, FlacMetadataHeader output)
    {
        if (source.SampleRate != output.SampleRate || source.Channels != output.Channels || source.SampleBits != output.SampleBits ||
            (source.SampleFrames != 0 && source.SampleFrames != output.SampleFrames))
            throw new InvalidDataException("FLAC audio declarations changed during recompression.");
        var before = source.Blocks[0].Data.AsSpan(18, 16);
        var after = output.Blocks[0].Data.AsSpan(18, 16);
        if (before.IndexOfAnyExcept((byte)0) >= 0 && !before.SequenceEqual(after))
            throw new InvalidDataException("The declared FLAC audio checksum changed during recompression.");
    }
}
