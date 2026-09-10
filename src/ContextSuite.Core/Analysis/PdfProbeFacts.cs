namespace ContextSuite.Core.Analysis;

// Structured declarations from a bounded probe, not a rendering, accessibility
// or signature-validation result. Field values and attachment names are not retained.
public sealed record PdfProbeFacts(int? PageCount, bool IsEncrypted, bool? HasForms,
    int? FormFieldCount, int? ReportedSignatureFieldCount, int? AttachmentCount, int? OutlineCount,
    bool? NeedsFormAppearances)
{
    public void Validate()
    {
        // Locked encryption permits known encryption with unavailable content facts.
        var counts = new[] { PageCount, FormFieldCount, ReportedSignatureFieldCount, AttachmentCount, OutlineCount };
        if (IsEncrypted && counts.All(count => count is null) && HasForms is null && NeedsFormAppearances is null) return;
        if (counts.Any(count => count is null or < 0 or > 4096) || HasForms is null || NeedsFormAppearances is null ||
            ReportedSignatureFieldCount > FormFieldCount || (!HasForms.Value && (FormFieldCount != 0 || NeedsFormAppearances.Value)))
            throw new InvalidDataException("Invalid PDF probe facts.");
    }
}
