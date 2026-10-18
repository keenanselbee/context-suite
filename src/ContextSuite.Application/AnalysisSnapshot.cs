using System.Collections.Immutable;
using System.Globalization;
using ContextSuite.Core.Analysis;

namespace ContextSuite.Application;

internal sealed record AnalysisHighlight(string Label, string Value, string Detail);
internal sealed record AnalysisDetail(string Label, string Value);
internal sealed record AnalysisDetailGroup(string Name, IReadOnlyList<AnalysisDetail> Items);

// A bounded presentation of facts already reported by the analyzer. No further file inspection occurs here.
internal sealed record AnalysisSnapshot(
    string TypeName,
    string Identification,
    string EvidenceSummary,
    string FileSize,
    IReadOnlyList<AnalysisHighlight> Highlights,
    string KeyInformation,
    string Description,
    string Warnings,
    IReadOnlyList<AnalysisDetailGroup> TechnicalDetails)
{
    public static AnalysisSnapshot Create(FileAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var identity = analysis.Identity;
        var identification = identity.Confidence switch
        {
            IdentificationConfidence.Ambiguous => "Ambiguous",
            IdentificationConfidence.Unknown => "Unknown",
            _ when identity.Basis == IdentificationBasis.Filename => "Filename only",
            IdentificationConfidence.Confirmed => "Identified from contents",
            _ => "Likely from contents"
        };
        var evidence = identity.Basis == IdentificationBasis.Filename
            ? "The filename suggests this type; inspected contents did not establish it."
            : !identity.Evidence.IsDefaultOrEmpty
                ? string.Join(" ", identity.Evidence)
                : identity.Confidence == IdentificationConfidence.Ambiguous
                    ? "The available evidence does not establish one type."
                    : "The inspected contents did not establish a file type.";

        var size = FormatSize(analysis.FileBytes);
        var highlights = ImmutableArray.CreateBuilder<AnalysisHighlight>(3);
        highlights.Add(new("File size", size, ""));
        var facts = analysis.Facts.IsDefault ? ImmutableArray<AnalysisFact>.Empty : analysis.Facts;
        var width = KnownNumber(facts, "image.width");
        var height = KnownNumber(facts, "image.height");
        if (width is > 0 && height is > 0)
            highlights.Add(new("Dimensions", $"{width:N0} x {height:N0}", "Pixels"));
        var pages = KnownNumber(facts, "document.pages");
        if (pages is >= 0 && highlights.Count < 3)
            highlights.Add(new("Pages", pages.Value.ToString("N0"), ""));
        var duration = KnownNumber(facts, "audio.duration-ms");
        if (duration is >= 0 && highlights.Count < 3)
            highlights.Add(new("Duration", FormatDuration(duration.Value), ""));
        else if (highlights.Count < 3 && ProbeDuration(facts) is { } probedDuration)
            highlights.Add(new("Duration", FormatDuration(probedDuration), ""));
        var rate = KnownNumber(facts, "audio.sample-rate");
        rate ??= facts.FirstOrDefault(fact => fact.Id.StartsWith("audio.probe.stream.", StringComparison.Ordinal) &&
            fact.Id.EndsWith(".rate", StringComparison.Ordinal) && fact.Availability is FactAvailability.Explicit or FactAvailability.Derived)?.Integer;
        if (rate is > 0 && highlights.Count < 3)
            highlights.Add(new("Sample rate", $"{rate:N0} Hz", ""));
        var mips = KnownNumber(facts, "dds.mips");
        if (mips is > 1 && highlights.Count < 3)
            highlights.Add(new("Mip levels", mips.Value.ToString("N0"), ""));
        var lines = KnownNumber(facts, "text.lines");
        if (lines is >= 0 && highlights.Count < 3)
        {
            var complete = facts.Any(fact => fact.Id == "text.lines-complete" && fact.Boolean == true &&
                fact.Availability is FactAvailability.Explicit or FactAvailability.Derived);
            highlights.Add(new("Lines", complete ? $"{lines:N0}" : $"{lines:N0}+",
                complete ? "" : $"At least {lines:N0}; first {KnownNumber(facts, "text.inspected") ?? analysis.InspectedBytes:N0} bytes checked"));
        }
        var encoding = facts.FirstOrDefault(fact => fact.Id == "text.encoding" &&
            fact.Availability is FactAvailability.Explicit or FactAvailability.Derived)?.Text;
        if (!string.IsNullOrEmpty(encoding) && highlights.Count < 3)
            highlights.Add(new("Text encoding", FriendlyEncoding(encoding), ""));
        var selected = highlights.ToImmutable();

        var details = ImmutableArray.CreateBuilder<AnalysisDetailGroup>();
        // Lead with properties of the selected file; keep classification and
        // diagnostic evidence below them. Source already covers generic file facts.
        foreach (var group in facts.Where(fact => fact.Id is not ("file.size" or "file.inspected")).GroupBy(fact => fact.Group))
            details.Add(new(group.Key, group.Select(fact =>
                new AnalysisDetail($"{fact.Label} ({fact.Id})", FactValue(fact))).ToImmutableArray()));
        details.Add(new("Source", ImmutableArray.CreateRange(new AnalysisDetail[] {
            new("Path", analysis.Path),
            new("File size (bytes)", analysis.FileBytes.ToString("N0")),
            new("Header bytes inspected", analysis.InspectedBytes.ToString("N0"))
        })));
        details.Add(new("Identification", ImmutableArray.CreateRange(new AnalysisDetail[] {
            new("Format ID", identity.FormatId ?? "Unknown"),
            new("Confidence", identity.Confidence.ToString()),
            new("Basis", identity.Basis.ToString())
        })));
        var catalogType = FileTypeCatalog.Default.Types.FirstOrDefault(type => type.Id == identity.FormatId);
        if (catalogType is { MimeTypes.IsEmpty: false })
            details.Add(new("Catalog", [
                new("MIME types (descriptive; exact variant not determined)", string.Join(", ", catalogType.MimeTypes.Select(mime => mime.Value))),
                new("Catalog revision", FileTypeCatalog.Default.Revision)
            ]));
        if (!analysis.FilenameHints.IsDefaultOrEmpty)
            details.Add(new("Filename hints", analysis.FilenameHints
                .Select(hint => new AnalysisDetail(hint.Id, hint.Name + " - " + hint.CommonUses)).ToImmutableArray()));
        details.Add(new("Analysis scope", ImmutableArray.CreateRange(new AnalysisDetail[] {
            new("Scope", "Read-only header and available bounded structure analysis; complete content validity was not established."),
            new("Analyzer", FileAnalysis.AnalyzerVersion)
        })));

        details.Add(new("Evidence", ImmutableArray.Create(new AnalysisDetail("How the type was determined", evidence))
            .AddRange(identity.Evidence.IsDefaultOrEmpty ? [] : identity.Evidence.Select((item, index) =>
                new AnalysisDetail($"Observation {index + 1}", item)))));

        var keyInformation = selected.Length > 1
            ? string.Join("  |  ", selected.Skip(1).Select(highlight => highlight.Label + ": " + highlight.Value))
            : analysis.FileBytes == 0 ? "Empty file"
            : identity.Confidence == IdentificationConfidence.Unknown ? "Unknown file type"
            : "No additional details available";
        var warnings = analysis.Warnings.IsDefault ? ImmutableArray<string>.Empty : analysis.Warnings;
        if (identity.Basis == IdentificationBasis.Content && identity.FormatId is not null &&
            !analysis.FilenameHints.IsDefaultOrEmpty && analysis.FilenameHints.All(hint => hint.Id != identity.FormatId) &&
            !warnings.Any(warning => warning.Contains("filename", StringComparison.OrdinalIgnoreCase)))
            warnings = warnings.Add("The filename suggests a different type from the inspected content.");
        return new(identity.Name, identification, evidence, size, selected, keyInformation,
            identity.CommonUses, warnings.IsEmpty ? "" : string.Join(Environment.NewLine, warnings),
            details.ToImmutable());
    }

    private static string FriendlyEncoding(string encoding) => encoding switch
    {
        "UTF-8 compatible (ASCII is also possible)" or "UTF-8 BOM" => "UTF-8",
        "UTF-16 little-endian BOM" => "UTF-16 LE",
        "UTF-16 big-endian BOM" => "UTF-16 BE",
        "UTF-32 little-endian BOM" => "UTF-32 LE",
        "UTF-32 big-endian BOM" => "UTF-32 BE",
        _ => encoding
    };

    private static long? KnownNumber(ImmutableArray<AnalysisFact> facts, string id)
    {
        var fact = facts.FirstOrDefault(item => item.Id == id);
        return fact?.Availability is FactAvailability.Explicit or FactAvailability.Derived ? fact.Integer : null;
    }

    private static long? ProbeDuration(ImmutableArray<AnalysisFact> facts)
    {
        var fact = facts.FirstOrDefault(item => item.Id == "audio.probe.duration" &&
            item.Availability is FactAvailability.Explicit or FactAvailability.Derived);
        if (fact?.Text is null || !double.TryParse(fact.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            !double.IsFinite(seconds) || seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds)
            return null;
        return checked((long)Math.Round(seconds * 1000));
    }

    private static string FactValue(AnalysisFact fact) => fact.Availability switch
    {
        FactAvailability.Unknown => "Unknown",
        FactAvailability.NotEncoded => "Not encoded",
        FactAvailability.Unavailable => "Not checked by this analyzer",
        _ => (fact.Text ?? fact.Integer?.ToString("N0") ??
            (fact.Boolean is { } value ? value ? "Yes" : "No" : "Unknown")) +
            (fact.Availability == FactAvailability.Derived ? " (derived)" : "")
    };

    private static string FormatDuration(long milliseconds)
    {
        if (milliseconds > TimeSpan.MaxValue.TotalMilliseconds)
            return $"{milliseconds:N0} ms";
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        return duration.TotalHours >= 1 ? $"{(long)duration.TotalHours:N0}:{duration.Minutes:00}:{duration.Seconds:00}"
            : duration.ToString(@"m\:ss", CultureInfo.CurrentCulture);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes:N0} bytes";
        var units = new[] { "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
        var value = (double)bytes;
        var index = -1;
        do { value /= 1024; index++; } while (value >= 1024 && index < units.Length - 1);
        return $"{value:N1} {units[index]}";
    }
}
