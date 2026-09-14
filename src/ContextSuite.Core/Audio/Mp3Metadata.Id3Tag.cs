using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

internal sealed record Id3TagInventory(ImmutableDictionary<string, string> Tags, ImmutableArray<FlacMetadataBlock> Pictures);

public static partial class Mp3Metadata
{
    // A complete bounded tag inside another container, without invented MPEG
    // frames. Reuse the same frame, text, picture and duplicate-field policies.
    internal static Id3TagInventory ReadId3Tag(ReadOnlySpan<byte> tag, bool preservePictures)
    {
        if (tag.Length < 10 || !tag[..3].SequenceEqual("ID3"u8)) throw new InvalidDataException("Missing ID3 tag header.");
        var version = tag[3];
        if (version is not (2 or 3 or 4) || tag[4] != 0) throw new NotSupportedException("This ID3 version needs a metadata handler.");
        var flags = tag[5]; var permittedFlags = version == 4 ? 0x90 : 0x80;
        if ((flags & ~permittedFlags) != 0) throw new NotSupportedException("Compressed, extended or experimental ID3 headers need a preservation handler.");
        var length = Synchsafe(tag.Slice(6, 4)); var footer = (flags & 16) != 0 ? 10 : 0;
        if (length > MaximumTagBytes || tag.Length != 10 + length + footer) throw new InvalidDataException("ID3 tag extent or byte budget is invalid.");
        if (footer != 0 && (!tag[^10..^7].SequenceEqual("3DI"u8) || !tag[^7..].SequenceEqual(tag.Slice(3, 7))))
            throw new InvalidDataException("ID3 footer does not match its header.");
        var pictures = ImmutableArray.CreateBuilder<FlacMetadataBlock>();
        var comments = ReadFrames(tag.Slice(10, length).ToArray(), version, (flags & 128) != 0, pictures, preservePictures);
        var blocks = pictures.ToImmutable(); var facts = FlacDescriptiveMetadata.ReadBlocks(blocks).Pictures;
        if (facts.Select(picture => picture.Description).Distinct(StringComparer.Ordinal).Count() != pictures.Count)
            throw new InvalidDataException("ID3 pictures have duplicate descriptions.");
        return new(AudioCommentConversion.Read(comments), blocks);
    }
}
