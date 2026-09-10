using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

public sealed record AudioStreamFacts(int Index, string Kind, string Codec, int? SampleRate, int? Channels,
    int? SampleBits, long? BitRate, decimal? ReportedDurationSeconds, string? ChannelLayout,
    ImmutableDictionary<string, string> Tags, bool AttachedPicture = false);
