using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

public sealed record AudioChapterFacts(long Id, decimal StartSeconds, decimal EndSeconds,
    ImmutableDictionary<string, string> Tags);
