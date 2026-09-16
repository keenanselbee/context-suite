using ContextSuite.Core.Images;

namespace ContextSuite.Application.Infrastructure;

// Failure categories are shared over IPC; Office wording must not imply that a
// native export failure diagnoses a damaged image or document.
internal static class OfficeFailureMessages
{
    internal static string Describe(ImageFailure failure, bool validatingPdf = false) => failure switch
    {
        ImageFailure.InvalidInput or ImageFailure.UnsupportedInput or ImageFailure.EngineFailure when validatingPdf =>
            "The generated PDF could not be validated and was not saved. Try exporting it from the document's usual application. Original kept.",
        ImageFailure.InvalidInput => "Context Suite could not read or convert this document. Open it in its usual application and try exporting to PDF there. Original kept.",
        ImageFailure.UnsupportedInput => "This document uses a format or feature that Context Suite cannot convert. Export it to PDF from its usual application. Original kept.",
        ImageFailure.SourceChanged => "The document changed after it was selected. Select it again and retry. Original kept.",
        ImageFailure.FileAccess => "A file needed for PDF conversion could not be accessed. Check available space, permissions and file locks. Original kept.",
        ImageFailure.ResourceLimit => "The document exceeds a PDF processing limit. Try fewer pages or a smaller page size. Original kept.",
        ImageFailure.EngineFailure => "The document converter could not finish. Try again, or export to PDF from the document's usual application. Original kept.",
        ImageFailure.ValidationFailed => "The generated PDF could not be validated and was not saved. Try exporting it from the document's usual application. Original kept.",
        ImageFailure.TimedOut when validatingPdf => "PDF validation took too long. Try a smaller document or export it from its usual application. Original kept.",
        ImageFailure.TimedOut => "PDF conversion took too long. Try a smaller document or export it from its usual application. Original kept.",
        ImageFailure.WorkerTerminated => "The document converter stopped unexpectedly. Try again, or export to PDF from the document's usual application. Original kept.",
        ImageFailure.OfficeDateFidelity => ImageFailureException.Describe(failure),
        _ => throw new InvalidDataException("Unknown Office failure category.")
    };
}
