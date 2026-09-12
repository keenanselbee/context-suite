using System.Collections.Immutable;
using ContextSuite.Core.Dds;

namespace ContextSuite.Core.Analysis;

public enum IdentificationConfidence { Unknown, Likely, Confirmed, Ambiguous }
public enum IdentificationBasis { Unknown, Filename, Content }
public enum FactAvailability { Explicit, Derived, Unknown, NotEncoded, Unavailable }

// Text, integer and Boolean facts retain their value type independently of UI formatting.
public sealed record AnalysisFact(string Id, string Group, string Label, string? Text = null,
    long? Integer = null, bool? Boolean = null, FactAvailability Availability = FactAvailability.Explicit);

public sealed record FormatIdentity(string? FormatId, string Name, string Family,
    string CommonUses, IdentificationConfidence Confidence, ImmutableArray<string> Evidence,
    IdentificationBasis Basis = IdentificationBasis.Unknown);

public sealed record FileAnalysis(string Path, long FileBytes, FormatIdentity Identity,
    ImmutableArray<AnalysisFact> Facts, ImmutableArray<string> Warnings, int InspectedBytes,
    DdsInfo? Texture = null, ImmutableArray<FileTypeDescription> FilenameHints = default)
{
    public const int SchemaVersion = 1;
    public const string AnalyzerVersion = "slide-visibility-1";
}
