namespace ContextSuite.Core.Images;

public enum ImageFailure
{
    InvalidInput, UnsupportedInput, SourceChanged, FileAccess, ResourceLimit,
    EngineFailure, ValidationFailed, TimedOut, WorkerTerminated, OfficeDateFidelity, EngineUnavailable,
    PdfDarkContrast, PdfDarkTransparency, PdfDarkStructure, PdfDarkColor, PdfDarkFont, PdfDarkProtected
}

// Only stable categories cross IPC, never native diagnostics or private metadata.
public sealed class ImageFailureException(ImageFailure failure, Exception? inner = null, int? pdfDarkPage = null)
    : IOException(Describe(failure), inner)
{
    public ImageFailure Failure { get; } = failure;
    public int? PdfDarkPage { get; } = pdfDarkPage is null or >= 1 and <= 512
        ? pdfDarkPage : throw new ArgumentOutOfRangeException(nameof(pdfDarkPage));

    public static string Describe(ImageFailure failure) => failure switch
    {
        ImageFailure.InvalidInput => "The image is damaged or invalid. Check the source file and try another copy.",
        ImageFailure.UnsupportedInput => "This image uses an unsupported format or feature. Choose a supported still image.",
        ImageFailure.SourceChanged => "The source changed after planning. Select it again to prepare a fresh plan.",
        ImageFailure.FileAccess => "A required file could not be accessed. Check permissions, available space and file locks.",
        ImageFailure.ResourceLimit => "The image exceeds a processing limit. Try a smaller image.",
        ImageFailure.EngineFailure => "The image engine could not finish. Retry the file or choose another format.",
        ImageFailure.EngineUnavailable => "The required conversion engine is unavailable. Repair or update Context Suite and try again. Analyze remains available.",
        ImageFailure.ValidationFailed => "The output failed validation and was not published. Try another format or policy.",
        ImageFailure.TimedOut => "The image worker exceeded its time limit. No unvalidated output was published.",
        ImageFailure.WorkerTerminated => "The image worker stopped unexpectedly. This item was not retried; later items can continue.",
        ImageFailure.OfficeDateFidelity => "The calculated workbook contains dates before March 1, 1900 that Context Suite cannot currently convert accurately. Export the PDF from Excel instead. Analyze remains available.",
        ImageFailure.PdfDarkContrast => "Some overlapping text or graphics cannot retain readable contrast in dark mode. Try a PDF exported from the source document. Original kept.",
        ImageFailure.PdfDarkTransparency => "This PDF uses transparency or blending that dark mode cannot safely recolor. Try a PDF exported from the source document. Original kept.",
        ImageFailure.PdfDarkStructure => "This PDF uses document structure or layers that dark mode cannot yet preserve. Try another PDF export. Original kept.",
        ImageFailure.PdfDarkColor => "This PDF uses an image or color profile that dark mode cannot yet recolor safely. Try another PDF export. Original kept.",
        ImageFailure.PdfDarkFont => "This PDF uses font features that dark mode cannot yet validate. Export a PDF with embedded fonts and retry. Original kept.",
        ImageFailure.PdfDarkProtected => "Dark mode cannot change protected, signed or interactive PDFs. Export a separate static PDF from the source application. Original kept.",
        _ => throw new InvalidDataException("Unknown image failure category.")
    };
}
