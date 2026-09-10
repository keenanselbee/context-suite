using System.Collections.Immutable;

namespace ContextSuite.Core.Analysis;

public static class PdfAnalysis
{
    public static FileAnalysis AddProbe(FileAnalysis header, PdfProbeFacts probe, int inspectedBytes)
    {
        probe.Validate();
        if (header.Identity.FormatId != "pdf" || header.Identity.Basis != IdentificationBasis.Content ||
            inspectedBytes != header.FileBytes || inspectedBytes < header.InspectedBytes)
            throw new InvalidDataException("PDF probing requires a complete source snapshot.");
        var facts = header.Facts.Where(fact => fact.Id is not ("document.pages" or "document.encryption")).ToImmutableArray().ToBuilder();
        AddNumber("document.pages", "Reported pages", probe.PageCount);
        AddBoolean("document.encryption", "Encrypted", probe.IsEncrypted);
        AddBoolean("pdf.forms", "Reported interactive forms", probe.HasForms);
        AddBoolean("pdf.appearances", "Form appearances need updating", probe.NeedsFormAppearances);
        AddNumber("pdf.fields", "Reported form fields", probe.FormFieldCount);
        AddNumber("pdf.signature-fields", "Reported signature fields (not signature verification)", probe.ReportedSignatureFieldCount);
        AddNumber("pdf.attachments", "Reported attachments", probe.AttachmentCount);
        AddNumber("pdf.outlines", "Reported bookmarks", probe.OutlineCount);
        var warnings = header.Warnings;
        if (probe.IsEncrypted && probe.PageCount is null)
            warnings = warnings.Add("This PDF is encrypted. Its page and content details could not be read without a password.");
        return header with
        {
            Facts = facts.ToImmutable(), Warnings = warnings, InspectedBytes = inspectedBytes,
            Identity = header.Identity with { Evidence = [
                "PDF signature and bounded structural probe. Pages were not rendered; content, accessibility and cryptographic signatures were not validated.",
                "A zero reported signature-field count does not establish that the document is unsigned."] }
        };

        void AddNumber(string id, string label, int? value)
        {
            facts.Add(new(id, "Document", label, Integer: value,
                Availability: value is null ? FactAvailability.Unavailable : FactAvailability.Derived));
        }
        void AddBoolean(string id, string label, bool? value)
        {
            facts.Add(new(id, "Document", label, Boolean: value,
                Availability: value is null ? FactAvailability.Unavailable : FactAvailability.Derived));
        }
    }
}
