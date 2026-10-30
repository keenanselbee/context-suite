using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static class VideoAudioMetadata
{
    internal static void RequireContainer(string path, AudioProbeFacts facts)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".mp4" or ".mov" && facts.Container == "mov,mp4,m4a,3gp,3g2,mj2" ||
            extension is ".webm" or ".mkv" && facts.Container == "matroska,webm") return;
        throw new InvalidDataException("The video extension and container do not match the reviewed extraction inputs.");
    }

    internal static Dictionary<string, string> DescriptiveTags(AudioProbeFacts facts, AudioStreamFacts selected)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Add(facts.Tags, false);
        Add(selected.Tags, true);
        // Validate semantic comments before native encoding. Unknown ordinary
        // keys remain bound and must survive output validation exactly.
        _ = AudioCommentConversion.Read(tags.Select(tag => new AudioComment(tag.Key, tag.Value)), mapVorbisAliases: false);
        return tags;

        void Add(IReadOnlyDictionary<string, string> source, bool selectedTrack)
        {
            var title = source.GetValueOrDefault("title");
            var name = source.GetValueOrDefault("name");
            var handler = source.GetValueOrDefault("handler_name");
            if (title is not null && name is not null && title != name)
                throw new NotSupportedException("Conflicting video audio names need a preservation policy.");
            title ??= name ?? (selectedTrack && handler is not ("SoundHandler" or "Core Media Audio") ? handler : null);
            foreach (var pair in source)
            {
                var key = pair.Key.ToLowerInvariant();
                if (AudioCommentConversion.IsTechnicalTag(key) || key is "duration" or "name") continue;
                var value = key == "title" ? title! : pair.Value;
                if (tags.TryGetValue(key, out var previous) && previous != value)
                {
                    if (key == "language" && previous == "und") tags[key] = value;
                    else if (key == "language" && value == "und") continue;
                    else throw new NotSupportedException("Conflicting movie and selected-track tags need a preservation policy.");
                }
                else tags[key] = value;
            }
            if (!source.ContainsKey("title") && title is not null)
            {
                if (tags.TryGetValue("title", out var previous) && previous != title)
                    throw new NotSupportedException("Movie and track titles disagree; choose a preservation policy.");
                tags["title"] = title;
            }
        }
    }
}
