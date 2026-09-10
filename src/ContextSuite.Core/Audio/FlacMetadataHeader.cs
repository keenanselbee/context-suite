using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

public sealed record FlacMetadataHeader(ImmutableArray<FlacMetadataBlock> Blocks, int AudioOffset,
    int SampleRate, int Channels, int SampleBits, long SampleFrames);
