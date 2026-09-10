using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

// Cross-format admission is separate from raw-preserving FLAC optimization.
// A comment surviving a flattened probe is necessary, but not by itself enough:
// embedded structures and duplicate values must have an explicit mapping too.
public static class FlacConversionMetadata
{
    public static ImmutableDictionary<string, string> Read(FlacMetadataHeader header)
    {
        if (header.Blocks.Any(block => block.Type is not (0 or 1 or 3 or 4)))
            throw new NotSupportedException("FLAC application data, cue sheets, artwork or unknown metadata need a conversion preservation handler.");
        var descriptions = FlacDescriptiveMetadata.Read(header);
        return AudioCommentConversion.Read(descriptions.Comments);
    }
}
