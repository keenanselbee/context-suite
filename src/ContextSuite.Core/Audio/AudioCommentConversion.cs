using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

public static class AudioCommentConversion
{
    // Shared with native probe filtering: codec/container provenance is not a
    // descriptive value or a promise to preserve an old encoder's identity.
    public static bool IsTechnicalTag(string name) => name.ToLowerInvariant() is
        "encoder" or "major_brand" or "minor_version" or "compatible_brands" or "handler_name" or "vendor_id";

    public static ImmutableDictionary<string, string> Read(IEnumerable<AudioComment> comments, bool mapVorbisAliases = true)
    {
        var tags = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var comment in comments)
        {
            var key = comment.Name.ToLowerInvariant();
            if (IsTechnicalTag(key)) continue;
            if (key is "cuesheet" or "metadata_block_picture" or "coverart" or "coverartmime" ||
                key.StartsWith("chapter", StringComparison.Ordinal) || key.StartsWith("loop", StringComparison.Ordinal) ||
                key.StartsWith("replaygain_", StringComparison.Ordinal) || key.StartsWith("r128_", StringComparison.Ordinal))
                throw new NotSupportedException("Audio chapter, loop, artwork or playback-gain comments need a conversion policy.");
            key = !mapVorbisAliases ? key : key switch
            {
                "albumartist" => "album_artist", "tracknumber" => "track", "discnumber" => "disc",
                "discsubtitle" => "disc_subtitle", "description" => "comment", _ => key
            };
            if (key.Length > 128 || comment.Value.Length > 4096 || comment.Value.Contains('\0') || tags.Count >= 128)
                throw new InvalidDataException("Audio conversion comments exceed the transport limits or contain NUL.");
            if (!tags.TryAdd(key, comment.Value))
                throw new NotSupportedException("Repeated or aliased audio comments need a mapping that retains every value.");
        }
        return tags.ToImmutable();
    }

    public static void RequireTags(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual)
    {
        foreach (var tag in expected)
            if (!actual.TryGetValue(tag.Key, out var value) || value != tag.Value)
                throw new InvalidDataException("The audio probe or output did not retain the original audio comment values.");
    }
}
