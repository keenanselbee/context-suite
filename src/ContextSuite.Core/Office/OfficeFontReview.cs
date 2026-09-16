using System.Collections.Immutable;

namespace ContextSuite.Core.Office;

// A per-document decision before publication, never a persistent overwrite or
// substitution preference. Missing reports cannot be interpreted as no warning.
public sealed record OfficeFontReview(string SourcePath, ImmutableArray<string> MissingFontFamilies);
