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
        var tags = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var comment in descriptions.Comments)
        {
            var key = comment.Name.ToLowerInvariant();
            if (key == "encoder") continue;
            if (key is "cuesheet" or "metadata_block_picture" or "coverart" or "coverartmime" ||
                key.StartsWith("chapter", StringComparison.Ordinal) || key.StartsWith("loop", StringComparison.Ordinal) ||
                key.StartsWith("replaygain_", StringComparison.Ordinal) || key.StartsWith("r128_", StringComparison.Ordinal))
                throw new NotSupportedException("FLAC chapter, loop, artwork or playback-gain comments need a conversion policy.");
            key = key switch
            {
                "albumartist" => "album_artist", "tracknumber" => "track", "discnumber" => "disc",
                "discsubtitle" => "disc_subtitle", "description" => "comment", _ => key
            };
            if (key.Length > 128 || comment.Value.Length > 4096 || comment.Value.Contains('\0') || tags.Count >= 128)
                throw new InvalidDataException("FLAC conversion comments exceed the transport limits or contain NUL.");
            if (!tags.TryAdd(key, comment.Value))
                throw new NotSupportedException("Repeated or aliased FLAC comments need a mapping that retains every value.");
        }
        return tags.ToImmutable();
    }

    public static void RequireTags(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual)
    {
        foreach (var tag in expected)
            if (!actual.TryGetValue(tag.Key, out var value) || value != tag.Value)
                throw new InvalidDataException("The audio probe or output did not retain the original FLAC comment values.");
    }
}
