using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

// Probed declarations are not decoded-sample validation or transformation admission.
public sealed record AudioProbeFacts(string Container, decimal? ReportedDurationSeconds,
    ImmutableArray<AudioStreamFacts> Streams, ImmutableDictionary<string, string> Tags)
{
    public void Validate()
    {
        if (Container is null || Container.Length > 128 || ReportedDurationSeconds < 0 || Streams.IsDefault || Streams.Length > 32)
            throw new InvalidDataException("Invalid audio probe summary.");
        var indices = new HashSet<int>();
        var textLength = 0;
        CheckTags(Tags);
        foreach (var stream in Streams)
        {
            if (stream is null || stream.Index < 0 || !indices.Add(stream.Index) || stream.Kind is null || stream.Kind.Length > 32 ||
                stream.Codec is null || stream.Codec.Length > 64 || stream.ChannelLayout?.Length > 128 || stream.SampleRate <= 0 ||
                stream.Channels <= 0 || stream.SampleBits is <= 0 or > 64 || stream.BitRate < 0 || stream.ReportedDurationSeconds < 0)
                throw new InvalidDataException("Invalid audio probe stream.");
            CheckTags(stream.Tags);
        }
        void CheckTags(ImmutableDictionary<string, string> tags)
        {
            if (tags is null || tags.Count > 128) throw new InvalidDataException("Invalid audio probe tags.");
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tags)
            {
                if (tag.Key.Length > 128 || tag.Value is null || tag.Value.Length > 4096 || !keys.Add(tag.Key))
                    throw new InvalidDataException("Invalid audio probe tag.");
                textLength += tag.Key.Length + tag.Value.Length;
                if (textLength > 256 * 1024) throw new InvalidDataException("Audio probe tags exceed their aggregate budget.");
            }
        }
    }
}
