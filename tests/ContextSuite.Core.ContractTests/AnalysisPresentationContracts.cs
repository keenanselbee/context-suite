using System.Collections.Immutable;
using ContextSuite.Application;
using ContextSuite.Core.Analysis;

internal static class AnalysisPresentationContracts
{
    public static void Run(Action<bool, string> check)
    {
        var filename = AnalysisSnapshot.Create(HeaderAnalyzer.Analyze("sample.html", "ordinary text"u8, 13));
        check(filename.Identification == "Filename only" && filename.TypeName.Contains("HTML", StringComparison.OrdinalIgnoreCase) &&
            filename.EvidenceSummary.Contains("filename", StringComparison.OrdinalIgnoreCase) && filename.Highlights.Count == 3 &&
            filename.Highlights[0] is { Label: "File size", Value: "13 bytes" } &&
            filename.Highlights[1] is { Label: "Lines", Value: "1" } && filename.Highlights[2] is { Label: "Text encoding", Value: "UTF-8" } &&
            filename.TechnicalDetails.SelectMany(group => group.Items).Any(item => item.Value == filename.EvidenceSummary) &&
            !filename.KeyInformation.Contains("bytes") &&
            filename.Description.Length > 0 && filename.TechnicalDetails.SelectMany(group => group.Items)
                .Any(item => item.Label == "File size (bytes)" && item.Value == "13"),
            "analysis presentation: useful text facts lead while identification evidence remains in details");
        check(filename.TechnicalDetails[0].Name == "Text" && filename.TechnicalDetails[^1].Name == "Evidence" &&
            filename.TechnicalDetails.All(group => group.Name != "File") &&
            filename.TechnicalDetails.SelectMany(group => group.Items).Count(item => item.Label == "File size (bytes)") == 1,
            "analysis presentation: type-specific facts lead, source facts are not duplicated, evidence is last");

        var pngBytes = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82,
            0, 0, 2, 0, 0, 0, 1, 0, 8, 2 }.CopyTo(pngBytes, 0);
        var png = AnalysisSnapshot.Create(HeaderAnalyzer.Analyze("image.png", pngBytes, 2048));
        check(png.Identification == "Likely from contents" && png.Highlights.Count <= 3 &&
            png.Highlights.Any(item => item.Label == "Dimensions" && item.Value == "512 x 256") &&
            png.FileSize.Contains("KiB") && png.KeyInformation.Contains("Dimensions") &&
            png.EvidenceSummary.Contains("not checked", StringComparison.OrdinalIgnoreCase),
            "analysis presentation: declared PNG dimensions and bounded identity claim");

        var shortPng = AnalysisSnapshot.Create(HeaderAnalyzer.Analyze("broken.png", pngBytes[..8], 8));
        check(shortPng.Highlights.Count == 1 && !shortPng.Highlights.Any(item => item.Label == "Dimensions") &&
            shortPng.Warnings.Contains("complete PNG image header") &&
            shortPng.TechnicalDetails.Any(group => group.Name == "Source"),
            "analysis presentation: truncated PNG does not acquire invented dimensions");

        var pdf = AnalysisSnapshot.Create(HeaderAnalyzer.Analyze("document.pdf", "%PDF-1.7\n"u8, 9));
        check(pdf.Identification == "Likely from contents" && !pdf.Highlights.Any(item => item.Label == "Pages") &&
            pdf.TechnicalDetails.Any(group => group.Name == "Catalog" && group.Items.Any(item => item.Value == "application/pdf")) &&
            pdf.TechnicalDetails.SelectMany(group => group.Items).Any(item =>
                item.Label.Contains("document.pages") && item.Value == "Not checked by this analyzer"),
            "analysis presentation: unavailable PDF page count stays in details only");

        var unknown = AnalysisSnapshot.Create(HeaderAnalyzer.Analyze("mystery.unregistered", [0, 255, 1], 3));
        check(unknown.Identification == "Unknown" && unknown.FileSize == "3 bytes" && unknown.Highlights.Count == 1 &&
            unknown.Highlights[0].Label == "File size" &&
            unknown.EvidenceSummary.Length > 0 && !unknown.KeyInformation.Contains("bytes"),
            "analysis presentation: unknown file keeps size and evidence without fabricated facts");

        var ambiguous = AnalysisSnapshot.Create(HeaderAnalyzer.Analyze("code.ts", [0, 255, 1], 3));
        check(ambiguous.Identification == "Ambiguous" && ambiguous.TechnicalDetails.Any(group => group.Name == "Filename hints") &&
            ambiguous.Highlights.Count == 1 && ambiguous.TechnicalDetails.SelectMany(group => group.Items).Any(item => item.Value == "Ambiguous"),
            "analysis presentation: ambiguous filename retains competing hints");

        var conflicting = AnalysisSnapshot.Create(HeaderAnalyzer.Analyze("disguised.pdf", pngBytes, 2048));
        check(conflicting.TypeName == png.TypeName && conflicting.Warnings.Contains("different type from the inspected content") &&
            conflicting.TechnicalDetails.Any(group => group.Name == "Filename hints"),
            "analysis presentation: content and filename mismatch remains visible");

        var partial = AnalysisSnapshot.Create(HeaderAnalyzer.Analyze("large.html", "first\nsecond"u8, 100000));
        check(partial.Highlights.Any(item => item.Label == "Lines" && item.Value == "2+" && item.Detail.Contains("At least")) &&
            partial.KeyInformation.Contains("2+") && !partial.Highlights.Any(item => item.Label == "Identification"),
            "analysis presentation: sampled text gives a visible lower bound instead of a full-file count");

        var facts = ImmutableArray.Create(
            new AnalysisFact("audio.duration-ms", "Audio", "Duration", Integer: 65_000, Availability: FactAvailability.Derived),
            new AnalysisFact("audio.sample-rate", "Audio", "Sample rate", Integer: 48_000),
            new AnalysisFact("audio.raw-codec", "Audio", "Raw codec identifier", Text: "0xFFFF"),
            new AnalysisFact("audio.precision", "Audio", "Precision", Availability: FactAvailability.NotEncoded),
            new AnalysisFact("audio.layout", "Audio", "Channel layout", Availability: FactAvailability.Unknown));
        var custom = new FileAnalysis("fixture.wav", 1024, new("wave", "WAVE audio", "Audio", "Audio playback.",
            IdentificationConfidence.Confirmed, ["Header declarations inspected; audio samples were not validated."], IdentificationBasis.Content),
            facts, ["A header field is inconsistent."], 64);
        var audio = AnalysisSnapshot.Create(custom);
        check(audio.Identification == "Identified from contents" && audio.Highlights.Count == 3 &&
            audio.Highlights[0].Label == "File size" && audio.Highlights[1] is { Label: "Duration", Value: "1:05" } &&
            audio.Highlights[2].Label == "Sample rate" &&
            audio.Warnings.Contains("inconsistent") && audio.TechnicalDetails.Any(group => group.Name == "Audio" &&
                group.Items.Any(item => item.Label.Contains("audio.raw-codec") && item.Value == "0xFFFF") &&
                group.Items.Any(item => item.Value == "Not encoded") && group.Items.Any(item => item.Value == "Unknown")),
            "analysis presentation: audio highlights and grouped raw, unavailable and warning details");

        var longAudio = AnalysisSnapshot.Create(custom with { Facts = [
            new("audio.duration-ms", "Audio", "Duration", Integer: 25 * 60 * 60 * 1000, Availability: FactAvailability.Derived),
            new("audio.probe.duration", "Audio probe", "Reported duration (seconds)", Text: "90000", Availability: FactAvailability.Derived)
        ] });
        check(longAudio.Highlights.Any(item => item is { Label: "Duration", Value: "25:00:00" }),
            "analysis presentation: duration beyond one day retains total hours");

        var probedAudio = AnalysisSnapshot.Create(custom with { Facts = [
            new("audio.probe.duration", "Audio probe", "Reported duration (seconds)", Text: "65.5", Availability: FactAvailability.Derived),
            new("audio.probe.stream.0.rate", "Stream 0: audio", "Reported sample rate (Hz)", Integer: 44_100, Availability: FactAvailability.Derived)
        ] });
        check(probedAudio.Highlights.Count == 3 && probedAudio.Highlights[1] is { Label: "Duration", Value: "1:05" } &&
            probedAudio.Highlights[2] is { Label: "Sample rate", Value: "44,100 Hz" },
            "analysis presentation: bounded audio probe facts feed available highlights");
    }
}
