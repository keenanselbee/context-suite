using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

public sealed record FlacMetadataBlock(byte Type, ImmutableArray<byte> Data);
