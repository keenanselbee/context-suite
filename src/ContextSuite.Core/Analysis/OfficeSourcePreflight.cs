namespace ContextSuite.Core.Analysis;

// Necessary source identification only, not a rendering permit or a safety scan.
// The caller must retain its read lease through copying/rendering and enforce
// separate engine, isolation, active-content and publication policies.
public sealed record OfficeSourcePreflight(FileAnalysis Analysis, string? FormatId, string? Refusal)
{
    public static async Task<OfficeSourcePreflight> InspectOpenXmlAsync(string path, Stream input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!input.CanRead || !input.CanSeek) throw new ArgumentException("Use a readable, seekable source lease.", nameof(input));
        input.Position = 0;
        var bytes = new byte[(int)Math.Min(input.Length, HeaderAnalyzer.MaximumBytes)];
        await input.ReadExactlyAsync(bytes, cancellationToken);
        var header = HeaderAnalyzer.Analyze(path, bytes, input.Length);
        if (input.Length == 0) return new(header, null, "The file is empty. Choose a document that contains data.");
        var analysis = await DocumentAnalysis.AddPackageAsync(header, input, cancellationToken);
        if (analysis.Identity.Basis != IdentificationBasis.Content ||
            analysis.Identity.Confidence is not (IdentificationConfidence.Likely or IdentificationConfidence.Confirmed) ||
            analysis.Identity.FormatId is not ("docx" or "xlsx" or "pptx"))
            return new(analysis, null, "The file contents do not identify a supported Office XML document. Analyze remains available.");

        // Analyze deliberately groups templates and macro-enabled packages with
        // their document family. Conversion must select the exact variant.
        var declarations = analysis.Facts.Where(fact => fact.Id == "document.content-type").ToArray();
        var format = declarations.Length == 1 && declarations[0].Availability == FactAvailability.Explicit
            ? declarations[0].Text switch
            {
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" => "docx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" => "xlsx",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml" => "pptx",
                _ => null
            } : null;
        if (format is null || format != analysis.Identity.FormatId)
            return new(analysis, null, "This Office document variant is not supported by the current conversion preflight. Analyze remains available.");
        return new(analysis, format, null);
    }
}
